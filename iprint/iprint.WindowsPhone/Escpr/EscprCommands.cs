// ESC/P-R and remote-mode command bytes.
// Copied byte-for-byte from epson-escpr-api.c (Epson ESC/P-R driver 1.8.10, GPL-2.0+).

using System;
using System.Collections.Generic;

namespace iprint.Escpr
{
    public static class EscprCommands
    {
        /// <summary>Exits IEEE-1284.4 packet mode: 00 00 00 ESC 01 "@EJL 1284.4\n@EJL     \n".</summary>
        public static readonly byte[] ExitPacketMode =
        {
            0x00, 0x00, 0x00, 0x1B, 0x01, 0x40, 0x45, 0x4A, 0x4C, 0x20,
            0x31, 0x32, 0x38, 0x34, 0x2E, 0x34, 0x0A, 0x40, 0x45, 0x4A,
            0x4C, 0x20, 0x20, 0x20, 0x20, 0x20, 0x0A
        };

        public static readonly byte[] EscprMode = { 0x1B, (byte)'(', (byte)'R', 0x06, 0x00, 0x00, (byte)'E', (byte)'S', (byte)'C', (byte)'P', (byte)'R' };

        public static readonly byte[] InitPrinter = { 0x1B, 0x40 };

        public static readonly byte[] EnterRemoteMode = { 0x1B, 0x28, 0x52, 0x08, 0x00, 0x00, (byte)'R', (byte)'E', (byte)'M', (byte)'O', (byte)'T', (byte)'E', (byte)'1' };

        public static readonly byte[] ExitRemoteMode = { 0x1B, 0x00, 0x00, 0x00 };

        public static readonly byte[] RemoteJS = { (byte)'J', (byte)'S', 0x04, 0x00, 0x00, 0x00, 0x00, 0x00 };

        public static readonly byte[] RemoteJH =
        {
            (byte)'J', (byte)'H', 0x0e, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x45, 0x53, 0x43, 0x50, 0x52, 0x4c, 0x69, 0x62 // "ESCPRLib"
        };

        public static readonly byte[] RemoteHD = { (byte)'H', (byte)'D', 0x03, 0x00, 0x00, 0x03, 0xFF };
        public static readonly byte[] RemotePP = { (byte)'P', (byte)'P', 0x03, 0x00, 0x00, 0x00, 0x00 };
        public static readonly byte[] RemoteQM1 = { (byte)'U', (byte)'S', 0x03, 0x00, 0x00, 0x05, 0x00 };
        public static readonly byte[] RemoteQM2 = { (byte)'U', (byte)'S', 0x03, 0x00, 0x00, 0x08, 0x00 };
        public static readonly byte[] RemoteLD = { (byte)'L', (byte)'D', 0x00, 0x00 };
        public static readonly byte[] RemoteJE = { (byte)'J', (byte)'E', 0x01, 0x00, 0x00 };
        public static readonly byte[] RemoteCH = { (byte)'C', (byte)'H', 0x02, 0x00, 0x00, 0x00 };
        public static readonly byte[] RemoteNC = { (byte)'N', (byte)'C', 0x02, 0x00, 0x00, 0x00 };
        public static readonly byte[] RemoteVI = { (byte)'V', (byte)'I', 0x02, 0x00, 0x00, 0x00 };
        public static readonly byte[] RemoteEX33 = { (byte)'E', (byte)'X', 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x33, 0xFF, 0x01, 0x00 };

        public static readonly byte[] DataCR = { 0x0D };
        public static readonly byte[] DataLF = { 0x0A };
        public static readonly byte[] DataFF = { 0x0C };

        /// <summary>Platform byte in remote "HD" (0x04 = linux in the open driver).</summary>
        public const byte Platform = 0x04;

        /// <summary>"TI" remote command with current local time (MakeRemoteTICmd).</summary>
        public static byte[] RemoteTI(DateTime now)
        {
            return new byte[]
            {
                (byte)'T', (byte)'I', 0x08, 0x00, 0x00,
                (byte)(now.Year >> 8), (byte)(now.Year & 0xFF),
                (byte)now.Month, (byte)now.Day, (byte)now.Hour, (byte)now.Minute, (byte)now.Second
            };
        }

        /// <summary>
        /// ESC/P-R command: ESC, class, param length (4 bytes little endian), 4-char name, params.
        /// </summary>
        public static byte[] Command(char cls, string name, byte[] parameters)
        {
            int n = parameters == null ? 0 : parameters.Length;
            var b = new byte[10 + n];
            b[0] = 0x1B;
            b[1] = (byte)cls;
            b[2] = (byte)(n & 0xFF);
            b[3] = (byte)((n >> 8) & 0xFF);
            b[4] = (byte)((n >> 16) & 0xFF);
            b[5] = (byte)((n >> 24) & 0xFF);
            for (int i = 0; i < 4; i++) b[6 + i] = (byte)name[i];
            if (n > 0) Array.Copy(parameters, 0, b, 10, n);
            return b;
        }

        public static void PutBE16(byte[] b, int off, int v) { b[off] = (byte)(v >> 8); b[off + 1] = (byte)v; }
        public static void PutBE32(byte[] b, int off, int v)
        {
            b[off] = (byte)(v >> 24); b[off + 1] = (byte)(v >> 16); b[off + 2] = (byte)(v >> 8); b[off + 3] = (byte)v;
        }

        /// <summary>Remote "PP" (paper path) bytes for an EPS_MPID_* value (SendStartJob).</summary>
        public static byte[] PaperPath(int source)
        {
            var pp = (byte[])RemotePP.Clone();
            switch (source)
            {
                case 0x0001: pp[5] = 0x01; pp[6] = 0x00; break; // rear
                case 0x0002: pp[5] = 0x01; pp[6] = 0x01; break; // front1
                case 0x0004: pp[5] = 0x01; pp[6] = 0x02; break; // front2
                case 0x0020: pp[5] = 0x01; pp[6] = 0x03; break; // front3
                case 0x0040: pp[5] = 0x01; pp[6] = 0x04; break; // front4
                case 0x0008: pp[5] = 0x02; pp[6] = 0x01; break; // CD tray
                case 0x0100: pp[5] = 0x03; pp[6] = 0x00; break; // roll
                case 0x0010: pp[5] = 0x02; pp[6] = 0x00; break; // manual
                case 0x0200: pp[5] = 0x02; pp[6] = 0x02; break; // manual2
                default: pp[5] = 0x01; pp[6] = 0xFF; break;     // auto select
            }
            return pp;
        }

        /// <summary>
        /// Maintenance command stream, port of epsMakeMainteCmd().
        /// </summary>
        public static byte[] Maintenance(bool nozzleCheck, DateTime now)
        {
            var o = new List<byte>();
            o.AddRange(ExitPacketMode);
            o.AddRange(InitPrinter);
            o.AddRange(InitPrinter);
            o.AddRange(EnterRemoteMode);
            o.AddRange(RemoteTI(now));
            if (nozzleCheck)
            {
                o.AddRange(RemoteJS);
                var pp = (byte[])RemotePP.Clone();
                pp[5] = 0x01; pp[6] = 0xFE;
                o.AddRange(pp);
                o.AddRange(RemoteEX33);
                o.AddRange(RemoteNC);
                o.AddRange(ExitRemoteMode);
                o.AddRange(DataCR); o.AddRange(DataLF); o.AddRange(DataCR); o.AddRange(DataLF);
                o.AddRange(EnterRemoteMode);
                o.AddRange(RemoteVI);
                o.AddRange(RemoteLD);
                o.AddRange(ExitRemoteMode);
                o.AddRange(DataFF);
            }
            else
            {
                o.AddRange(RemoteCH);
                o.AddRange(ExitRemoteMode);
            }
            o.AddRange(InitPrinter);
            o.AddRange(InitPrinter);
            o.AddRange(EnterRemoteMode);
            o.AddRange(RemoteJE);
            o.AddRange(ExitRemoteMode);
            return o.ToArray();
        }
    }
}
