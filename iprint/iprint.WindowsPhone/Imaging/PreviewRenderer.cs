// Builds a small "sheet of paper" bitmap using the exact same placement code as printing,
// so the preview matches the print (layout, borders, rotation, crop).

using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using iprint.Escpr;
using iprint.Models;
using Windows.Storage;
using Windows.UI.Xaml.Media.Imaging;

namespace iprint.Imaging
{
    public static class PreviewRenderer
    {
        public static async Task<WriteableBitmap> RenderAsync(StorageFile file, int rotation, PrintOptions opt,
            bool photo, int targetWidth, CancellationToken ct)
        {
            var size = Media.SizeById(opt.MediaSizeId);
            var g = Media.Geometry(size, opt.Layout);
            double s = (double)targetWidth / size.PaperX;
            int paperW = Math.Max(1, (int)Math.Round(size.PaperX * s));
            int paperH = Math.Max(1, (int)Math.Round(size.PaperY * s));
            int areaW = Math.Max(1, (int)Math.Round(g.PrintableWidth * s));
            int areaH = Math.Max(1, (int)Math.Round(g.PrintableHeight * s));
            int offX = (int)Math.Round(g.LeftMargin * s);
            int offY = (int)Math.Round(g.TopMargin * s);

            var bgra = new byte[paperW * paperH * 4];
            for (int i = 0; i < bgra.Length; i++) bgra[i] = 0xFF;

            bool borderless = opt.Layout == Media.LayoutBorderless;
            await PhotoRasterizer.RenderAsync(file, areaW, areaH, rotation,
                photo && borderless, photo, 0, opt.ColorMode == Media.ColorMono,
                (rgb, o, y, token) =>
                {
                    int py = y + offY;
                    if (py >= 0 && py < paperH)
                    {
                        for (int x = 0; x < areaW; x++)
                        {
                            int px = x + offX;
                            if (px < 0 || px >= paperW) continue;
                            int d = (py * paperW + px) * 4, sIdx = o + x * 3;
                            bgra[d] = rgb[sIdx + 2]; bgra[d + 1] = rgb[sIdx + 1]; bgra[d + 2] = rgb[sIdx]; bgra[d + 3] = 0xFF;
                        }
                    }
                    return Task.FromResult(0);
                }, null, ct);

            var wb = new WriteableBitmap(paperW, paperH);
            using (var stream = wb.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(bgra, 0, bgra.Length);
            }
            wb.Invalidate();
            return wb;
        }
    }
}
