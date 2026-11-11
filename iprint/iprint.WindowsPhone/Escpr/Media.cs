// Paper sizes / media types / options.
// Size table rows copied from epsMediaSize[] (epson-escpr-media.h, 360 dpi units):
// paper_x, paper_y, print_area_x_border, print_area_y_border,
// left_margin_borderless, top_margin_borderless, print_area_x_borderless, print_area_y_borderless.

using System.Collections.Generic;
using System.Linq;

namespace iprint.Escpr
{
    public sealed class MediaSize
    {
        public int Id { get; private set; }
        public string Name { get; private set; }
        public int PaperX { get; private set; }
        public int PaperY { get; private set; }
        public int AreaXBorder { get; private set; }
        public int AreaYBorder { get; private set; }
        public int LeftBorderless { get; private set; }
        public int TopBorderless { get; private set; }
        public int AreaXBorderless { get; private set; }
        public int AreaYBorderless { get; private set; }

        public MediaSize(int id, string name, int px, int py, int bx, int by, int lb, int tb, int ax, int ay)
        {
            Id = id; Name = name; PaperX = px; PaperY = py; AreaXBorder = bx; AreaYBorder = by;
            LeftBorderless = lb; TopBorderless = tb; AreaXBorderless = ax; AreaYBorderless = ay;
        }

        public override string ToString() { return Name; }
    }

    /// <summary>Geometry of one page in ESC/P-R units (360 dpi).</summary>
    public struct PageGeometry
    {
        public int PaperWidth, PaperHeight;
        public int TopMargin, LeftMargin;      // may be negative for borderless
        public int PrintableWidth, PrintableHeight;
    }

    public sealed class Option
    {
        public int Value { get; private set; }
        public string Name { get; private set; }
        public Option(int value, string name) { Value = value; Name = name; }
        public override string ToString() { return Name; }
    }

    public static class Media
    {
        public const int Dpi = 360;
        const int BordersMargin360 = 42; // EPS_BORDERS_MARGIN_360

        // ESC/P-R media size IDs (EPS_MSID_*)
        public static readonly List<MediaSize> Sizes = new List<MediaSize>
        {
            new MediaSize(0,  "A4 (210 x 297 mm)",        2976, 4209, 2892, 4125, -36, -42, 3048, 4321),
            new MediaSize(1,  "letter (8.5 x 11 in)",     3060, 3960, 2976, 3876, -36, -42, 3132, 4072),
            new MediaSize(2,  "legal (8.5 x 14 in)",      3060, 5040, 2976, 4956, -36, -42, 3132, 5152),
            new MediaSize(3,  "A5 (148 x 210 mm)",        2098, 2976, 2014, 2892, -36, -42, 2170, 3088),
            new MediaSize(4,  "A6 (105 x 148 mm)",        1488, 2098, 1404, 2014, -36, -42, 1560, 2210),
            new MediaSize(5,  "B5 (182 x 257 mm)",        2580, 3643, 2496, 3559, -36, -42, 2652, 3755),
            new MediaSize(10, "10 x 15 cm (4 x 6 in)",    1440, 2160, 1356, 2076, -36, -42, 1512, 2272),
            new MediaSize(28, "13 x 18 cm (5 x 7 in)",    1800, 2522, 1716, 2438, -36, -42, 1872, 2634),
            new MediaSize(15, "9 x 13 cm (3.5 x 5 in)",   1261, 1800, 1177, 1716, -36, -42, 1333, 1912),
            new MediaSize(43, "16:9 wide (102 x 181 mm)", 1440, 2560, 1356, 2476, -36, -42, 1512, 2672),
            new MediaSize(12, "20 x 25 cm (8 x 10 in)",   2880, 3600, 2796, 3516, -36, -42, 2952, 3712),
            new MediaSize(11, "13 x 20 cm (5 x 8 in)",    1800, 2880, 1716, 2796, -36, -42, 1872, 2992),
            new MediaSize(16, "postcard (100 x 148 mm)",  1417, 2098, 1333, 2014, -36, -42, 1489, 2210),
            new MediaSize(29, "envelope #10",             1485, 3420, 1345, 3095, -36, -42, 1557, 3532),
            new MediaSize(31, "envelope DL",              1559, 3118, 1419, 2793, -36, -42, 1631, 3230),
            new MediaSize(30, "envelope C6",              1616, 2296, 1476, 1971, -36, -42, 1688, 2408),
        };

        // ESC/P-R media type IDs (EPS_MTID_*) that L-series / photo models accept.
        public static readonly List<Option> Types = new List<Option>
        {
            new Option(0,  "plain paper"),
            new Option(11, "premium glossy photo paper"),
            new Option(12, "premium semigloss photo paper"),
            new Option(38, "ultra glossy photo paper"),
            new Option(43, "glossy photo paper"),
            new Option(5,  "matte paper - heavyweight"),
            new Option(29, "photo quality ink jet paper"),
            new Option(37, "envelope"),
        };

        // EPS_MQID_*
        public const int QualityDraft = 0x01, QualityNormal = 0x02, QualityHigh = 0x04;
        public static readonly List<Option> Qualities = new List<Option>
        {
            new Option(QualityDraft, "draft"),
            new Option(QualityNormal, "standard"),
            new Option(QualityHigh, "high"),
        };

        // EPS_MLID_*
        public const int LayoutBorderless = 0x01, LayoutBorders = 0x02;
        public static readonly List<Option> Layouts = new List<Option>
        {
            new Option(LayoutBorders, "with borders"),
            new Option(LayoutBorderless, "borderless"),
        };

        // EPS_MPID_*
        public const int SourceRear = 0x0001, SourceFront1 = 0x0002, SourceManual = 0x0010, SourceAuto = 0x0080;
        public static readonly List<Option> Sources = new List<Option>
        {
            new Option(SourceAuto, "auto select"),
            new Option(SourceRear, "rear paper feed"),
            new Option(SourceFront1, "front tray"),
            new Option(SourceManual, "rear manual feed"),
        };

        // EPS_CM_*
        public const int ColorColor = 0, ColorMono = 1;
        public static readonly List<Option> Colors = new List<Option>
        {
            new Option(ColorColor, "color"),
            new Option(ColorMono, "black and white"),
        };

        public static readonly List<Option> Sharpness = new List<Option>
        {
            new Option(0, "off"),
            new Option(1, "standard"),
            new Option(2, "strong"),
        };

        public static MediaSize SizeById(int id)
        {
            return Sizes.FirstOrDefault(s => s.Id == id) ?? Sizes[0];
        }

        public static Option Find(List<Option> list, int value)
        {
            return list.FirstOrDefault(o => o.Value == value) ?? list[0];
        }

        /// <summary>Port of CalcPrintMargin for EPS_IR_360X360 (factor 1).</summary>
        public static PageGeometry Geometry(MediaSize m, int layout)
        {
            var g = new PageGeometry { PaperWidth = m.PaperX, PaperHeight = m.PaperY };
            int top, bottom, left, right;
            if (layout == LayoutBorderless)
            {
                top = m.TopBorderless;
                bottom = -(m.AreaYBorderless - m.PaperY + m.TopBorderless);
                left = m.LeftBorderless;
                right = -(m.AreaXBorderless - m.PaperX + m.LeftBorderless);
            }
            else
            {
                bool envelope = (m.Id >= 18 && m.Id <= 27) || (m.Id >= 29 && m.Id <= 32) || m.Id == 44 || m.Id == 45 || m.Id == 47;
                left = envelope ? (m.PaperX - m.AreaXBorder) / 2 : BordersMargin360;
                right = m.PaperX - m.AreaXBorder - left;
                top = BordersMargin360;
                bottom = m.PaperY - m.AreaYBorder - top;
            }
            g.TopMargin = top;
            g.LeftMargin = left;
            g.PrintableWidth = m.PaperX - left - right;
            g.PrintableHeight = m.PaperY - top - bottom;
            return g;
        }
    }
}
