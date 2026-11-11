// Streams an image file into RGB raster lines of the printable area, band by band,
// so a full A4 page at 360 dpi never has to sit in memory at once.
// BitmapTransform order is: scale -> flip -> rotate -> crop (Bounds).

using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace iprint.Imaging
{
    public delegate Task LineSink(byte[] rgb, int offset, int y, CancellationToken ct);

    public sealed class PhotoRasterizer
    {
        const int BandHeight = 384;

        /// <param name="userRotation">extra rotation set by user: 0, 90, 180, 270</param>
        /// <param name="fill">true = cover the whole area (crop), false = fit inside (letterbox)</param>
        /// <param name="autoRotate">rotate 90 degrees if image and page orientation differ</param>
        /// <param name="sharpness">0 = off, 1 = standard, 2 = strong</param>
        public static async Task RenderAsync(StorageFile file, int width, int height, int userRotation, bool fill,
            bool autoRotate, int sharpness, bool grayscale, LineSink sink, IProgress<double> progress, CancellationToken ct)
        {
            using (IRandomAccessStream stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                int pw = (int)decoder.PixelWidth, ph = (int)decoder.PixelHeight;

                int rotation = (await ExifRotationAsync(decoder) + userRotation) % 360;
                bool swapped = rotation == 90 || rotation == 270;
                int iw = swapped ? ph : pw, ih = swapped ? pw : ph;
                if (autoRotate && iw != ih && ((iw > ih) != (width > height)))
                {
                    rotation = (rotation + 90) % 360;
                    swapped = !swapped;
                    int t = iw; iw = ih; ih = t;
                }

                double scale = fill ? Math.Max((double)width / iw, (double)height / ih)
                                    : Math.Min((double)width / iw, (double)height / ih);
                uint sw0 = (uint)Math.Max(1, Math.Round(pw * scale));
                uint sh0 = (uint)Math.Max(1, Math.Round(ph * scale));
                int rw = (int)(swapped ? sh0 : sw0), rh = (int)(swapped ? sw0 : sh0);
                int ox = (width - rw) / 2, oy = (height - rh) / 2;

                // visible part of the rotated, scaled image
                int srcX = Math.Max(0, -ox), dstX = Math.Max(0, ox);
                int visW = Math.Min(rw - srcX, width - dstX);

                var line = new byte[width * 3];
                var white = new byte[width * 3];
                for (int i = 0; i < white.Length; i++) white[i] = 0xFF;

                float k = sharpness == 0 ? 0f : (sharpness == 1 ? 0.18f : 0.35f);

                for (int y0 = 0; y0 < height; y0 += BandHeight)
                {
                    ct.ThrowIfCancellationRequested();
                    int y1 = Math.Min(height, y0 + BandHeight);
                    // image rows covering [y0,y1), plus one row of context for sharpening
                    int iy0 = Math.Max(0, y0 - oy), iy1 = Math.Min(rh, y1 - oy);
                    byte[] band = null;
                    int bandTop = 0, bandRows = 0;
                    if (iy1 > iy0 && visW > 0)
                    {
                        bandTop = Math.Max(0, iy0 - 1);
                        int bandBottom = Math.Min(rh, iy1 + 1);
                        bandRows = bandBottom - bandTop;
                        var transform = new BitmapTransform
                        {
                            ScaledWidth = sw0,
                            ScaledHeight = sh0,
                            Rotation = ToRotation(rotation),
                            InterpolationMode = BitmapInterpolationMode.Fant,
                            Bounds = new BitmapBounds { X = (uint)srcX, Y = (uint)bandTop, Width = (uint)visW, Height = (uint)bandRows }
                        };
                        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                            transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
                        var bgra = pixels.DetachPixelData();
                        band = ToRgb(bgra, visW, bandRows, grayscale);
                        if (k > 0) band = Sharpen(band, visW, bandRows, k);
                    }

                    for (int y = y0; y < y1; y++)
                    {
                        int iy = y - oy;
                        if (band == null || iy < iy0 || iy >= iy1)
                        {
                            await sink(white, 0, y, ct);
                            continue;
                        }
                        System.Buffer.BlockCopy(white, 0, line, 0, line.Length);
                        System.Buffer.BlockCopy(band, (iy - bandTop) * visW * 3, line, dstX * 3, visW * 3);
                        await sink(line, 0, y, ct);
                    }
                    if (progress != null) progress.Report((double)y1 / height);
                }
            }
        }

        static BitmapRotation ToRotation(int deg)
        {
            switch (deg)
            {
                case 90: return BitmapRotation.Clockwise90Degrees;
                case 180: return BitmapRotation.Clockwise180Degrees;
                case 270: return BitmapRotation.Clockwise270Degrees;
                default: return BitmapRotation.None;
            }
        }

        /// <summary>EXIF orientation (1,3,6,8) as clockwise degrees.</summary>
        public static async Task<int> ExifRotationAsync(BitmapDecoder decoder)
        {
            try
            {
                var props = await decoder.BitmapProperties.GetPropertiesAsync(new[] { "System.Photo.Orientation" });
                BitmapTypedValue v;
                if (props.TryGetValue("System.Photo.Orientation", out v) && v != null)
                {
                    switch (Convert.ToInt32(v.Value))
                    {
                        case 3: case 4: return 180;
                        case 6: case 5: return 90;
                        case 8: case 7: return 270;
                    }
                }
            }
            catch (Exception) { }
            return 0;
        }

        /// <summary>BGRA (straight alpha) to RGB composited on white.</summary>
        static byte[] ToRgb(byte[] bgra, int w, int h, bool gray)
        {
            var rgb = new byte[w * h * 3];
            int n = w * h;
            for (int i = 0, s = 0, d = 0; i < n; i++, s += 4, d += 3)
            {
                int a = bgra[s + 3];
                int b = bgra[s], g = bgra[s + 1], r = bgra[s + 2];
                if (a != 255)
                {
                    r = (r * a + 255 * (255 - a)) / 255;
                    g = (g * a + 255 * (255 - a)) / 255;
                    b = (b * a + 255 * (255 - a)) / 255;
                }
                if (gray)
                {
                    int l = (r * 77 + g * 150 + b * 29) >> 8;
                    r = g = b = l;
                }
                rgb[d] = (byte)r; rgb[d + 1] = (byte)g; rgb[d + 2] = (byte)b;
            }
            return rgb;
        }

        /// <summary>Simple 4-neighbour unsharp mask.</summary>
        static byte[] Sharpen(byte[] src, int w, int h, float k)
        {
            var dst = new byte[src.Length];
            int stride = w * 3;
            float c = 1 + 4 * k;
            for (int y = 0; y < h; y++)
            {
                int up = Math.Max(0, y - 1) * stride, down = Math.Min(h - 1, y + 1) * stride, row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int l = Math.Max(0, x - 1) * 3, r = Math.Min(w - 1, x + 1) * 3, m = x * 3;
                    for (int ch = 0; ch < 3; ch++)
                    {
                        float v = c * src[row + m + ch] - k * (src[row + l + ch] + src[row + r + ch] + src[up + m + ch] + src[down + m + ch]);
                        dst[row + m + ch] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                    }
                }
            }
            return dst;
        }
    }
}
