// SNMP object identifiers used by Epson iPrint (libescprlib.so strings) and
// epson-net-snmp.c. Values cross-checked with epson_print_conf (EUPL-1.2).

using System.Text;

namespace iprint.Escpr
{
    public static class EpsonOids
    {
        public const string EpsonMib = "1.3.6.1.4.1.1248";

        /// <summary>IEEE-1284 device ID string ("MFG:EPSON;CMD:ESCPL2,...,ESCPR1;MDL:L805 Series;...").</summary>
        public const string DeviceId = EpsonMib + ".1.2.2.1.1.1.1.1";

        /// <summary>Prefix for GetNext discovery (same object as DeviceId).</summary>
        public const string DeviceIdPrefix = EpsonMib + ".1.2.2.1.1.1.1";

        /// <summary>Epson "control" tunnel. Command bytes are appended as OID arcs.</summary>
        public const string Control = EpsonMib + ".1.2.2.44.1.1.2.1";

        /// <summary>hrDeviceDescr — fallback model name.</summary>
        public const string HrDeviceDescr = "1.3.6.1.2.1.25.3.2.1.3.1";

        /// <summary>sysName.</summary>
        public const string SysName = "1.3.6.1.2.1.1.5.0";

        /// <summary>
        /// Builds a control OID: Control + "." + cmd chars + "." + args bytes.
        /// e.g. Ctrl("st", 0x01, 0x00, 0x01) = status (ST2) request.
        /// </summary>
        public static string Ctrl(string cmd, params byte[] args)
        {
            var sb = new StringBuilder(Control);
            foreach (var c in cmd) sb.Append('.').Append((int)c);
            foreach (var b in args) sb.Append('.').Append(b);
            return sb.ToString();
        }

        /// <summary>Direct status object (alternative to the "st" control command).</summary>
        public const string StatusDirect = EpsonMib + ".1.2.2.1.1.1.4.1";
        public const string StatusDirectPrefix = EpsonMib + ".1.2.2.1.1.1.4";

        /// <summary>"st" 01 00 01 — returns "@BDC ST2\r\n" + binary status.</summary>
        public static readonly string Status = Ctrl("st", 0x01, 0x00, 0x01);
    }
}
