// "@BDC ST2" binary status parser.
// Port of serAnalyzeStatus / serGetSupplyInfo / parseCartridge (epson-escpr-services.c)
// plus maintenance-box field 0x37 and ink level byte as decoded by iPrint / epson_print_conf.

using System;
using System.Collections.Generic;
using System.Text;

namespace iprint.Escpr
{
    public enum PrinterState
    {
        Unknown, Error, SelfPrinting, Busy, Waiting, Idle, Cleaning, FactoryShipment, Shutdown, NozzleCheck
    }

    public enum InkState { Ok, Low, End, Fail, NotPresent, NotRead, NotAvailable }

    public enum MaintenanceBoxState { NotFull, NearFull, Full, Unknown }

    public sealed class InkInfo
    {
        public byte CartridgeCode { get; set; }
        public byte ColorCode { get; set; }
        public string Label { get; set; }
        /// <summary>#RRGGBB from the Figma ink palette.</summary>
        public string ColorHex { get; set; }
        /// <summary>0..100, or -1 when printer does not report a level.</summary>
        public int Level { get; set; }
        public InkState State { get; set; }
    }

    public sealed class St2Status
    {
        public PrinterState State { get; set; }
        public int ErrorCode { get; set; } = -1;
        public List<int> Warnings { get; private set; } = new List<int>();
        public List<InkInfo> Inks { get; private set; } = new List<InkInfo>();
        public List<MaintenanceBoxState> MaintenanceBoxes { get; private set; } = new List<MaintenanceBoxState>();
        public bool CancelRequested { get; set; }

        public bool IsReady
        {
            get { return (State == PrinterState.Idle || State == PrinterState.Waiting) && ErrorCode < 0; }
        }

        public static St2Status Parse(byte[] data)
        {
            if (data == null) return null;
            int p = IndexOf(data, Encoding.UTF8.GetBytes("ST2"));
            if (p < 0) return null;
            p += 5; // "ST2\r\n"
            if (p + 2 > data.Length) return null;
            int len = data[p] | (data[p + 1] << 8);
            p += 2;
            int end = Math.Min(data.Length, p + len);
            var st = new St2Status { State = PrinterState.Idle };
            while (p + 2 <= end)
            {
                byte header = data[p];
                int plen = data[p + 1];
                int prm = p + 2;
                p = prm + plen;
                if (p > end) break;
                switch (header)
                {
                    case 0x01:
                        if (plen > 0) st.State = MapState(data[prm]);
                        break;
                    case 0x02:
                        if (plen > 0) st.ErrorCode = data[prm];
                        break;
                    case 0x04:
                        for (int i = 0; i < plen; i++) st.Warnings.Add(data[prm + i]);
                        break;
                    case 0x0F:
                        ParseInk(data, prm, plen, st);
                        break;
                    case 0x13:
                        if (plen > 0) st.CancelRequested = data[prm] == 0x81;
                        break;
                    case 0x37:
                        ParseMaintenanceBox(data, prm, plen, st);
                        break;
                }
            }
            // Low-ink warnings (0x10..0x1A) mark low inks that still report a level.
            bool anyLowWarn = st.Warnings.Exists(w => w >= 0x10 && w <= 0x1A);
            foreach (var ink in st.Inks)
            {
                if (ink.State == InkState.Ok && ink.Level >= 0 && ink.Level <= 10 && anyLowWarn) ink.State = InkState.Low;
            }
            return st;
        }

        static void ParseInk(byte[] d, int prm, int plen, St2Status st)
        {
            if (plen < 1) return;
            int block = d[prm];
            if (block < 3) return;
            int count = (plen - 1) / block;
            int q = prm + 1;
            for (int i = 0; i < count; i++, q += block)
            {
                byte cart = d[q], color = d[q + 1], lvl = d[q + 2];
                var ink = Describe(cart, color);
                switch ((char)lvl)
                {
                    case 'w':
                    case 'r': ink.State = InkState.Fail; ink.Level = -1; break;
                    case 'n': ink.State = InkState.NotPresent; ink.Level = -1; break;
                    case 'i': ink.State = InkState.NotRead; ink.Level = -1; break;
                    case 'g': ink.State = InkState.NotAvailable; ink.Level = -1; break;
                    default:
                        if (lvl == 0) { ink.State = InkState.End; ink.Level = 0; }
                        else if (lvl <= 100) { ink.State = InkState.Ok; ink.Level = lvl; }
                        else { ink.State = InkState.NotAvailable; ink.Level = -1; }
                        break;
                }
                st.Inks.Add(ink);
            }
        }

        static void ParseMaintenanceBox(byte[] d, int prm, int plen, St2Status st)
        {
            if (plen < 2) return;
            int per = d[prm];
            if (per < 1 || per > 2) return;
            for (int i = prm + 1; i + per <= prm + plen; i += per)
            {
                switch (d[i])
                {
                    case 0: st.MaintenanceBoxes.Add(MaintenanceBoxState.NotFull); break;
                    case 1: st.MaintenanceBoxes.Add(MaintenanceBoxState.NearFull); break;
                    case 2: st.MaintenanceBoxes.Add(MaintenanceBoxState.Full); break;
                    default: st.MaintenanceBoxes.Add(MaintenanceBoxState.Unknown); break;
                }
            }
        }

        public static InkInfo Describe(byte cartridge, byte color)
        {
            string label, hex;
            switch (color)
            {
                case 0x00: label = "BK"; hex = "#16181B"; break;
                case 0x01: label = "C"; hex = "#0FC1F3"; break;
                case 0x02: label = "M"; hex = "#FF3C92"; break;
                case 0x03: label = "Y"; hex = "#FFF646"; break;
                case 0x04: label = "Lc"; hex = "#98E7F9"; break;
                case 0x05: label = "Lm"; hex = "#F5C4C5"; break;
                case 0x06: label = "LY"; hex = "#FFF9A6"; break;
                case 0x07: label = "DY"; hex = "#C8A800"; break;
                case 0x08: label = "GY"; hex = "#454B55"; break;
                case 0x09: label = "R"; hex = "#D7175D"; break;
                case 0x0A: label = "V"; hex = "#5F0989"; break;
                case 0x0B: label = "GO"; hex = "#FFFFFF"; break;
                case 0x0C: label = "LGY"; hex = "#919BA9"; break;
                case 0x0D: label = "O"; hex = "#E26D1A"; break;
                case 0x0E: label = "G"; hex = "#29F617"; break;
                case 0x0F: label = "W"; hex = "#E0E0E0"; break;
                case 0x40: label = "CL"; hex = "#808080"; break;
                default: label = "?"; hex = "#808080"; break;
            }
            if (cartridge == 0x0B && color == 0x00) label = "MK";
            return new InkInfo { CartridgeCode = cartridge, ColorCode = color, Label = label, ColorHex = hex, Level = -1 };
        }

        static PrinterState MapState(byte b)
        {
            switch (b)
            {
                case 0x00: return PrinterState.Error;
                case 0x01: return PrinterState.SelfPrinting;
                case 0x02: return PrinterState.Busy;
                case 0x03: return PrinterState.Waiting;
                case 0x04: return PrinterState.Idle;
                case 0x07: return PrinterState.Cleaning;
                case 0x08: return PrinterState.FactoryShipment;
                case 0x0A: return PrinterState.Shutdown;
                case 0x0D: return PrinterState.Busy;
                case 0x0F: return PrinterState.NozzleCheck;
                default: return PrinterState.Error;
            }
        }

        public string StateText
        {
            get
            {
                if (ErrorCode >= 0) return ErrorText(ErrorCode);
                switch (State)
                {
                    case PrinterState.Idle: return "ready";
                    case PrinterState.Waiting: return "waiting";
                    case PrinterState.Busy: return "busy";
                    case PrinterState.SelfPrinting: return "printing";
                    case PrinterState.Cleaning: return "cleaning";
                    case PrinterState.NozzleCheck: return "nozzle check";
                    case PrinterState.Shutdown: return "turning off";
                    case PrinterState.FactoryShipment: return "not initialized";
                    case PrinterState.Error: return "error";
                    default: return "unknown";
                }
            }
        }

        public static string ErrorText(int code)
        {
            switch (code)
            {
                case 0x00: return "fatal error — turn the printer off and on";
                case 0x01: return "another interface is in use";
                case 0x02:
                case 0x25: return "cover is open";
                case 0x04: return "paper jam";
                case 0x05:
                case 0x47: return "ink out";
                case 0x06:
                case 0x4C:
                case 0x57: return "paper out";
                case 0x0A:
                case 0x0C:
                case 0x17: return "paper size, type or source mismatch";
                case 0x10: return "service required (ink pad)";
                case 0x12: return "double feed";
                case 0x1A: return "ink cover is open";
                case 0x29: return "no tray";
                case 0x2C: return "ink cartridge overflow";
                case 0x33: return "initial ink charging not done";
                case 0x37: return "scanner or front cover is open";
                default: return "printer error 0x" + code.ToString("X2");
            }
        }

        static int IndexOf(byte[] hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Length; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
