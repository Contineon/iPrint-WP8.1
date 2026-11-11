// Persistent settings (selected printer, print options) in LocalSettings.

using System;
using iprint.Escpr;
using Windows.Storage;

namespace iprint.Models
{
    public sealed class PrintOptions
    {
        public int Copies = 1;
        public int MediaSizeId = 0;
        public int MediaTypeId = 0;
        public int Layout = Media.LayoutBorders;
        public int Quality = Media.QualityNormal;
        public int PaperSource = Media.SourceAuto;
        public int ColorMode = Media.ColorColor;
        public int Sharpness = 0;
        // document page range (1-based, 0 = all)
        public int RangeFrom = 0, RangeTo = 0;

        public JobSettings ToJobSettings()
        {
            return new JobSettings
            {
                MediaSizeId = MediaSizeId,
                MediaTypeId = MediaTypeId,
                Quality = Quality,
                Layout = Layout,
                PaperSource = PaperSource,
                ColorMode = ColorMode
            };
        }
    }

    public static class AppSettings
    {
        static ApplicationDataContainer Store { get { return ApplicationData.Current.LocalSettings; } }

        static T Get<T>(string key, T def)
        {
            object v;
            if (Store.Values.TryGetValue(key, out v) && v is T) return (T)v;
            return def;
        }

        static void Set(string key, object value)
        {
            if (value == null) Store.Values.Remove(key);
            else Store.Values[key] = value;
        }

        public static PrinterInfo Printer
        {
            get
            {
                var addr = Get<string>("printer.address", null);
                if (string.IsNullOrEmpty(addr)) return null;
                return new PrinterInfo
                {
                    Address = addr,
                    Name = Get("printer.name", "Epson printer"),
                    Model = Get<string>("printer.model", null),
                    DeviceIdRaw = Get<string>("printer.deviceid", null),
                    SupportsEscpr = Get("printer.escpr", true)
                };
            }
            set
            {
                Set("printer.address", value == null ? null : value.Address);
                Set("printer.name", value == null ? null : value.Name);
                Set("printer.model", value == null ? null : value.Model);
                Set("printer.deviceid", value == null ? null : value.DeviceIdRaw);
                Set("printer.escpr", value == null ? (object)null : value.SupportsEscpr);
            }
        }

        public static PrintOptions Load(string kind)
        {
            var p = "opt." + kind + ".";
            var o = new PrintOptions
            {
                Copies = Get(p + "copies", 1),
                MediaSizeId = Get(p + "size", kind == "photo" ? 10 : 0),
                MediaTypeId = Get(p + "type", kind == "photo" ? 11 : 0),
                Layout = Get(p + "layout", kind == "photo" ? Media.LayoutBorderless : Media.LayoutBorders),
                Quality = Get(p + "quality", Media.QualityNormal),
                PaperSource = Get(p + "source", Media.SourceAuto),
                ColorMode = Get(p + "color", Media.ColorColor),
                Sharpness = Get(p + "sharp", 0)
            };
            return o;
        }

        public static void Save(string kind, PrintOptions o)
        {
            var p = "opt." + kind + ".";
            Set(p + "copies", o.Copies);
            Set(p + "size", o.MediaSizeId);
            Set(p + "type", o.MediaTypeId);
            Set(p + "layout", o.Layout);
            Set(p + "quality", o.Quality);
            Set(p + "source", o.PaperSource);
            Set(p + "color", o.ColorMode);
            Set(p + "sharp", o.Sharpness);
        }
    }
}
