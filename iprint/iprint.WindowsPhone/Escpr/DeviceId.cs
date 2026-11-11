// IEEE-1284 device ID parser (port of serParseDeviceID, epson-escpr-services.c).

using System;
using System.Collections.Generic;
using System.Text;

namespace iprint.Escpr
{
    public sealed class DeviceId
    {
        public string Raw { get; private set; }
        public string Manufacturer { get; private set; }
        public string Model { get; private set; }
        public string CommandSet { get; private set; }

        public bool IsEpson
        {
            get { return Manufacturer != null && Manufacturer.StartsWith("EPSON", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>True when CMD: contains ESCPR (ESC/P-R raster language).</summary>
        public bool SupportsEscpr
        {
            get { return CommandSet != null && CommandSet.ToUpperInvariant().Contains("ESCPR"); }
        }

        /// <summary>"L805 Series" -> "Epson L805".</summary>
        public string DisplayName
        {
            get
            {
                var m = (Model ?? "").Trim();
                if (m.EndsWith(" Series", StringComparison.OrdinalIgnoreCase)) m = m.Substring(0, m.Length - 7);
                if (m.StartsWith("Epson ", StringComparison.OrdinalIgnoreCase)) return m;
                return m.Length == 0 ? "Epson printer" : "Epson " + m;
            }
        }

        public static DeviceId Parse(byte[] value)
        {
            if (value == null || value.Length < 2) return null;
            int start = (value[0] == 0 || value[1] == 0) ? 2 : 0; // optional 2-byte length prefix
            var s = Encoding.UTF8.GetString(value, start, value.Length - start).TrimEnd('\0');
            return Parse(s);
        }

        public static DeviceId Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in s.Split(';'))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                var k = part.Substring(0, c).Trim();
                var v = part.Substring(c + 1).Trim();
                if (!fields.ContainsKey(k)) fields[k] = v;
            }
            var d = new DeviceId { Raw = s };
            string v2;
            d.Manufacturer = fields.TryGetValue("MFG", out v2) ? v2 : (fields.TryGetValue("MANUFACTURER", out v2) ? v2 : null);
            d.Model = fields.TryGetValue("MDL", out v2) ? v2 : (fields.TryGetValue("MODEL", out v2) ? v2 : null);
            d.CommandSet = fields.TryGetValue("CMD", out v2) ? v2 : (fields.TryGetValue("COMMAND SET", out v2) ? v2 : null);
            if (d.Manufacturer == null) return null;
            return d;
        }
    }
}
