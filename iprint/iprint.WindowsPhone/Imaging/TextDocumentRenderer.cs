// Turns a plain-text document into page images (PNG in the temp folder) using XAML text layout
// and RenderTargetBitmap. Printing then treats every page as an image.
// Windows Phone 8.1 has no PDF / Office rendering API, so text is what can be printed natively.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace iprint.Imaging
{
    public static class TextDocumentRenderer
    {
        // Logical page = A4 at 96 dpi; rendered at 2x for print quality.
        public const double PageW = 794, PageH = 1123;
        const double Margin = 72, FontSize = 14;
        const int Scale = 2;

        public static async Task<string> ReadTextAsync(StorageFile file)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            var bytes = buffer.ToArray();
            // UTF-8 (with/without BOM) first, fall back to windows-1251 for old Cyrillic logs.
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                int skip = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return utf8.GetString(bytes, skip, bytes.Length - skip);
            }
            catch (Exception)
            {
                return DecodeCp1251(bytes);
            }
        }

        /// <summary>windows-1251 decoder (Ukrainian / Russian), the codepage is not available in WinRT .NET.</summary>
        static string DecodeCp1251(byte[] b)
        {
            var sb = new StringBuilder(b.Length);
            foreach (var x in b)
            {
                if (x < 0x80) sb.Append((char)x);
                else if (x >= 0xC0) sb.Append((char)(0x0410 + (x - 0xC0)));
                else
                {
                    switch (x)
                    {
                        case 0xA8: sb.Append('\u0401'); break;
                        case 0xB8: sb.Append('\u0451'); break;
                        case 0xAA: sb.Append('\u0404'); break;
                        case 0xBA: sb.Append('\u0454'); break;
                        case 0xAF: sb.Append('\u0407'); break;
                        case 0xBF: sb.Append('\u0457'); break;
                        case 0xB2: sb.Append('\u0406'); break;
                        case 0xB3: sb.Append('\u0456'); break;
                        case 0xA5: sb.Append('\u0490'); break;
                        case 0xB4: sb.Append('\u0491'); break;
                        case 0xB9: sb.Append('\u2116'); break;
                        case 0xAB: sb.Append('\u00AB'); break;
                        case 0xBB: sb.Append('\u00BB'); break;
                        case 0x96: sb.Append('\u2013'); break;
                        case 0x97: sb.Append('\u2014'); break;
                        case 0xA0: sb.Append(' '); break;
                        default: sb.Append('?'); break;
                    }
                }
            }
            return sb.ToString();
        }

        static TextBlock MakeBlock(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = FontSize,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Colors.Black),
                Width = PageW - 2 * Margin
            };
        }

        static double Measure(string text)
        {
            var tb = MakeBlock(text);
            tb.Measure(new Size(PageW - 2 * Margin, double.PositiveInfinity));
            return tb.DesiredSize.Height;
        }

        /// <summary>Splits text into pages by measuring wrapped paragraphs.</summary>
        public static List<string> Paginate(string text)
        {
            double avail = PageH - 2 * Margin;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var pages = new List<string>();
            var current = new List<string>();
            foreach (var raw in lines)
            {
                var line = raw.Replace("\t", "    ");
                current.Add(line);
                if (Measure(string.Join("\n", current)) <= avail) continue;
                current.RemoveAt(current.Count - 1);
                if (current.Count == 0)
                {
                    // one paragraph longer than a page: cut it by words
                    var words = line.Split(' ');
                    var chunk = new StringBuilder();
                    foreach (var w in words)
                    {
                        var trial = chunk.Length == 0 ? w : chunk + " " + w;
                        if (Measure(trial) > avail && chunk.Length > 0)
                        {
                            pages.Add(chunk.ToString());
                            chunk.Clear();
                            chunk.Append(w);
                        }
                        else
                        {
                            chunk.Clear();
                            chunk.Append(trial);
                        }
                    }
                    current.Add(chunk.ToString());
                }
                else
                {
                    pages.Add(string.Join("\n", current));
                    current.Clear();
                    current.Add(line);
                }
            }
            if (current.Count > 0 && (current.Count > 1 || current[0].Length > 0 || pages.Count == 0))
                pages.Add(string.Join("\n", current));
            return pages;
        }

        /// <summary>
        /// Renders pages to PNG files. host must be a visible Panel of PageW x PageH
        /// (it may sit inside a Viewbox: RenderTargetBitmap renders at the element's own size).
        /// </summary>
        public static async Task<List<StorageFile>> RenderAsync(string text, Panel host, StorageFolder folder,
            IProgress<int> progress)
        {
            var pages = Paginate(text);
            var result = new List<StorageFile>();
            for (int i = 0; i < pages.Count; i++)
            {
                var page = new Grid
                {
                    Width = PageW,
                    Height = PageH,
                    Background = new SolidColorBrush(Colors.White)
                };
                var tb = MakeBlock(pages[i]);
                tb.Margin = new Thickness(Margin);
                tb.VerticalAlignment = VerticalAlignment.Top;
                page.Children.Add(tb);
                host.Children.Add(page);
                page.UpdateLayout();

                var rtb = new RenderTargetBitmap();
                try
                {
                    await rtb.RenderAsync(page, (int)PageW * Scale, (int)PageH * Scale);
                }
                catch (Exception)
                {
                    await rtb.RenderAsync(page);
                }
                var px = (await rtb.GetPixelsAsync()).ToArray();
                host.Children.Remove(page);

                var file = await folder.CreateFileAsync(string.Format("page{0:D3}.png", i + 1),
                    CreationCollisionOption.ReplaceExisting);
                using (var s = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var enc = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, s);
                    enc.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                        (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96 * Scale, 96 * Scale, px);
                    await enc.FlushAsync();
                }
                result.Add(file);
                if (progress != null) progress.Report(i + 1);
            }
            return result;
        }
    }
}
