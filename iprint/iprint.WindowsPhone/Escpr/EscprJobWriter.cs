// ESC/P-R RGB raster job generator.
// Port of SendStartJob / MakeQualityCmd / MakeJobCmd / epsStartPage / SendLine /
// RunLengthEncode / epsEndPage / SendEndJob from epson-escpr-api.c (driver 1.8.10).

using System;
using System.Threading;
using System.Threading.Tasks;

namespace iprint.Escpr
{
    public interface IJobOutput
    {
        Task WriteAsync(byte[] data, int offset, int count, CancellationToken ct);
        Task FlushAsync(CancellationToken ct);
    }

    public sealed class JobSettings
    {
        public int MediaSizeId = 0;            // EPS_MSID_A4
        public int MediaTypeId = 0;            // EPS_MTID_PLAIN
        public int Quality = Media.QualityNormal;
        public int Layout = Media.LayoutBorders;
        public int PaperSource = Media.SourceAuto;
        public int ColorMode = Media.ColorColor;
        public int Brightness = 0, Contrast = 0, Saturation = 0; // -50..50
        public int QuietMode = 3;              // 3 = printer setting
        public bool BiDirectional = true;
    }

    public sealed class EscprJobWriter
    {
        const int Bpp = 3;
        const int FlushThreshold = 64 * 1024;

        readonly IJobOutput _out;
        readonly JobSettings _s;
        readonly PageGeometry _g;
        readonly byte[] _buffer = new byte[FlushThreshold + 32 * 1024];
        int _used;
        int _pageNumber;
        bool _pageOpen;
        byte[] _rleBuf;
        byte[] _lineBuf;

        public PageGeometry Geometry { get { return _g; } }

        public EscprJobWriter(IJobOutput output, JobSettings settings)
        {
            _out = output;
            _s = settings;
            _g = Media.Geometry(Media.SizeById(settings.MediaSizeId), settings.Layout);
            _rleBuf = new byte[_g.PrintableWidth * Bpp + _g.PrintableWidth / 2 + 16];
            _lineBuf = new byte[_g.PrintableWidth * Bpp + 64];
        }

        async Task PutAsync(byte[] data, CancellationToken ct)
        {
            await PutAsync(data, 0, data.Length, ct);
        }

        async Task PutAsync(byte[] data, int off, int count, CancellationToken ct)
        {
            if (_used + count > _buffer.Length)
            {
                await DrainAsync(ct);
                if (count > _buffer.Length)
                {
                    await _out.WriteAsync(data, off, count, ct);
                    return;
                }
            }
            System.Buffer.BlockCopy(data, off, _buffer, _used, count);
            _used += count;
            if (_used >= FlushThreshold) await DrainAsync(ct);
        }

        async Task DrainAsync(CancellationToken ct)
        {
            if (_used == 0) return;
            await _out.WriteAsync(_buffer, 0, _used, ct);
            _used = 0;
        }

        /// <summary>
        /// SendStartJob(FALSE) + epsStartPage(): job header, then "sttp" + "setn".
        /// totalPages = number of pages in the job (pages remaining, ESC/P-R "page remainder").
        /// </summary>
        public async Task StartJobAsync(int totalPages, CancellationToken ct)
        {
            var now = DateTime.Now;
            await PutAsync(EscprCommands.ExitPacketMode, ct);
            await PutAsync(EscprCommands.InitPrinter, ct);
            await PutAsync(EscprCommands.EnterRemoteMode, ct);
            await PutAsync(EscprCommands.RemoteTI(now), ct);
            await PutAsync(EscprCommands.RemoteJS, ct);
            await PutAsync(EscprCommands.RemoteJH, ct);
            var hd = (byte[])EscprCommands.RemoteHD.Clone();
            hd[6] = EscprCommands.Platform;
            await PutAsync(hd, ct);
            await PutAsync(EscprCommands.PaperPath(_s.PaperSource), ct);
            byte quiet = QuietByte(_s.QuietMode);
            var qm1 = (byte[])EscprCommands.RemoteQM1.Clone(); qm1[6] = quiet;
            var qm2 = (byte[])EscprCommands.RemoteQM2.Clone(); qm2[6] = quiet;
            await PutAsync(qm1, ct);
            await PutAsync(qm2, ct);
            await PutAsync(EscprCommands.ExitRemoteMode, ct);
            await PutAsync(EscprCommands.EscprMode, ct);

            // "setq" print quality (MakeQualityCmd), 9 bytes
            var q = new byte[9];
            q[0] = (byte)_s.MediaTypeId;
            q[1] = (byte)(_s.Quality == Media.QualityDraft ? 0 : _s.Quality == Media.QualityHigh ? 2 : 1);
            q[2] = (byte)_s.ColorMode;
            q[3] = (byte)(sbyte)_s.Brightness;
            q[4] = (byte)(sbyte)_s.Contrast;
            q[5] = (byte)(sbyte)_s.Saturation;
            q[6] = 0;               // EPS_CP_FULLCOLOR
            q[7] = 0; q[8] = 0;     // palette size (BE16) = 0
            await PutAsync(EscprCommands.Command('q', "setq", q), ct);

            // "seti" image processing mode, 10 bytes, last = binding position (no duplex)
            await PutAsync(EscprCommands.Command('q', "seti", new byte[10]), ct);

            // "setj" job (MakeJobCmd), 22 bytes
            var j = new byte[22];
            EscprCommands.PutBE32(j, 0, _g.PaperWidth);
            EscprCommands.PutBE32(j, 4, _g.PaperHeight);
            EscprCommands.PutBE16(j, 8, _g.TopMargin);
            EscprCommands.PutBE16(j, 10, _g.LeftMargin);
            EscprCommands.PutBE32(j, 12, _g.PrintableWidth);
            EscprCommands.PutBE32(j, 16, _g.PrintableHeight);
            j[20] = 0x00;                                   // EPS_IR_360X360
            j[21] = (byte)(_s.BiDirectional ? 0 : 1);       // print direction
            await PutAsync(EscprCommands.Command('j', "setj", j), ct);

            _pageNumber = 0;
            await StartPageAsync(totalPages, ct);
        }

        /// <summary>epsStartPage: "sttp" + "setn"(pages remaining including this one).</summary>
        public async Task StartPageAsync(int remainingPages, CancellationToken ct)
        {
            if (_pageOpen) throw new InvalidOperationException("page not closed");
            _pageNumber++;
            await PutAsync(EscprCommands.Command('p', "sttp", null), ct);
            await PutAsync(EscprCommands.Command('p', "setn", new[] { (byte)Math.Min(255, Math.Max(1, remainingPages)) }), ct);
            _pageOpen = true;
        }

        /// <summary>
        /// Sends one raster line (RGB, PrintableWidth pixels). Completely white lines are skipped
        /// like PrintBand() does.
        /// </summary>
        public async Task WriteLineAsync(byte[] rgb, int offset, int y, CancellationToken ct)
        {
            int width = _g.PrintableWidth;
            int bytes = width * Bpp;
            bool white = true;
            for (int i = offset; i < offset + bytes; i++)
            {
                if (rgb[i] != 0xFF) { white = false; break; }
            }
            if (white) return;

            bool compressed;
            int compSize = RunLengthEncode(rgb, offset, _rleBuf, width, out compressed);
            byte[] src = compressed ? _rleBuf : rgb;
            int srcOff = compressed ? 0 : offset;

            int paramSize = 7 + compSize;
            int cmdSize = 10 + paramSize;
            if (_lineBuf.Length < cmdSize) _lineBuf = new byte[cmdSize];
            var b = _lineBuf;
            b[0] = 0x1B; b[1] = (byte)'d';
            b[2] = (byte)paramSize; b[3] = (byte)(paramSize >> 8); b[4] = (byte)(paramSize >> 16); b[5] = (byte)(paramSize >> 24);
            b[6] = (byte)'d'; b[7] = (byte)'s'; b[8] = (byte)'n'; b[9] = (byte)'d';
            EscprCommands.PutBE16(b, 10, 0);        // x offset
            EscprCommands.PutBE16(b, 12, y);        // y offset
            b[14] = (byte)(compressed ? 1 : 0);     // EPS_COMP_RLE / EPS_COMP_NON
            EscprCommands.PutBE16(b, 15, compSize);
            System.Buffer.BlockCopy(src, srcOff, b, 17, compSize);
            await PutAsync(b, 0, cmdSize, ct);
        }

        /// <summary>"endp" with the number of pages that follow (0 = last page), GCOMSW_UPDATE_PAGE_REMAINDER.</summary>
        public async Task EndPageAsync(int pagesAfter, CancellationToken ct)
        {
            await PutAsync(EscprCommands.Command('p', "endp", new[] { (byte)Math.Min(255, Math.Max(0, pagesAfter)) }), ct);
            _pageOpen = false;
        }

        /// <summary>SendEndJob(FALSE): "endj" + reset + remote JE.</summary>
        public async Task EndJobAsync(CancellationToken ct)
        {
            if (_pageOpen) await EndPageAsync(0, ct);
            await PutAsync(EscprCommands.Command('j', "endj", null), ct);
            await PutAsync(EscprCommands.InitPrinter, ct);
            await PutAsync(EscprCommands.EnterRemoteMode, ct);
            await PutAsync(EscprCommands.RemoteJE, ct);
            await PutAsync(EscprCommands.ExitRemoteMode, ct);
            await DrainAsync(ct);
            await _out.FlushAsync(ct);
        }

        static byte QuietByte(int mode)
        {
            switch (mode)
            {
                case 0: return 0x00;
                case 1: return 0x01;
                case 2: return 0x02;
                default: return 0xFF;
            }
        }

        static bool Same(byte[] s, int a, int b)
        {
            return s[a] == s[b] && s[a + 1] == s[b + 1] && s[a + 2] == s[b + 2];
        }

        /// <summary>Port of RunLengthEncode() for bpp = 3.</summary>
        public static int RunLengthEncode(byte[] src, int srcOff, byte[] dst, int pixel, out bool compressed)
        {
            const int bpp = Bpp;
            int widthBytes = pixel * bpp;
            int srcCnt = 0, retCnt = 0, sp = srcOff, dp = 0;
            compressed = true;
            while (srcCnt < pixel)
            {
                if (srcCnt + 1 < pixel && Same(src, sp, sp + bpp))
                {
                    int rep = 2;
                    while (srcCnt + rep < pixel && rep < 0x81 && Same(src, sp + (rep - 1) * bpp, sp + rep * bpp)) rep++;
                    retCnt += 1 + bpp;
                    if (retCnt > widthBytes) { compressed = false; break; }
                    dst[dp++] = (byte)(0xFF - rep + 2);
                    dst[dp++] = src[sp]; dst[dp++] = src[sp + 1]; dst[dp++] = src[sp + 2];
                    srcCnt += rep;
                    sp += bpp * rep;
                }
                else
                {
                    int rep = 1;
                    while (srcCnt + rep + 1 < pixel && rep < 0x80 && !Same(src, sp + rep * bpp, sp + (rep + 1) * bpp)) rep++;
                    retCnt += 1 + rep * bpp;
                    if (retCnt > widthBytes) { compressed = false; break; }
                    dst[dp++] = (byte)(rep - 1);
                    int copy = rep * bpp;
                    System.Buffer.BlockCopy(src, sp, dst, dp, copy);
                    dp += copy;
                    srcCnt += rep;
                    sp += copy;
                }
            }
            if (!compressed) return widthBytes;
            return retCnt;
        }
    }
}
