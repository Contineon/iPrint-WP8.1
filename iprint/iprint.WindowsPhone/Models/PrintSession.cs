// What is going to be printed right now (shared between preview, settings and progress pages)
// and the job runner that drives EscprJobWriter.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using iprint.Escpr;
using iprint.Imaging;
using Windows.Storage;

namespace iprint.Models
{
    public sealed class PrintPage
    {
        public StorageFile File { get; set; }
        public int Rotation { get; set; }       // user rotation, degrees
    }

    public enum PrintKind { Photo, Document }

    public static class PrintSession
    {
        public static PrintKind Kind = PrintKind.Photo;
        public static readonly ObservableCollection<PrintPage> Photos = new ObservableCollection<PrintPage>();
        public static List<PrintPage> DocumentPages = new List<PrintPage>();
        public static string DocumentName;
        public static PrintOptions Options = AppSettings.Load("photo");

        public static string KindKey { get { return Kind == PrintKind.Photo ? "photo" : "doc"; } }

        public static List<PrintPage> PagesToPrint()
        {
            if (Kind == PrintKind.Photo) return Photos.ToList();
            var pages = DocumentPages;
            int from = Options.RangeFrom > 0 ? Options.RangeFrom : 1;
            int to = Options.RangeTo > 0 ? Options.RangeTo : pages.Count;
            from = Math.Max(1, Math.Min(from, pages.Count));
            to = Math.Max(from, Math.Min(to, pages.Count));
            return pages.Skip(from - 1).Take(to - from + 1).ToList();
        }
    }

    public sealed class JobProgress
    {
        public int Page, PageCount;
        public double PageFraction;
        public string Message;
        /// <summary>Last status reported by the printer itself (SNMP ST2), null if unknown.</summary>
        public string PrinterState;
    }

    public static class PrintJobRunner
    {
        public static async Task RunAsync(string address, PrintKind kind, IList<PrintPage> pages, PrintOptions opt,
            IProgress<JobProgress> progress, CancellationToken ct)
        {
            if (pages.Count == 0) throw new InvalidOperationException("nothing to print");
            var job = opt.ToJobSettings();
            int copies = Math.Max(1, Math.Min(99, opt.Copies));
            var sequence = new List<PrintPage>();
            if (kind == PrintKind.Document)
                for (int c = 0; c < copies; c++) sequence.AddRange(pages);         // collated
            else
                foreach (var p in pages) for (int c = 0; c < copies; c++) sequence.Add(p);

            progress.Report(new JobProgress { Message = "checking printer", PageCount = sequence.Count });
            St2Status st = null;
            try { st = await EpsonPrinter.GetStatusAsync(address, ct); } catch (Exception) { }
            if (st != null && st.ErrorCode >= 0)
                throw new InvalidOperationException(st.StateText);

            // Poll the printer while the job is being sent, so a paper / media error shows up
            // instead of a progress bar that only tracks network transfer.
            string printerState = st != null ? st.StateText : null;
            string printerError = null;
            int curPage = 0;
            double curFraction = 0;
            var monitorCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var monitor = Task.Run(async () =>
            {
                while (!monitorCts.IsCancellationRequested)
                {
                    try { await Task.Delay(4000, monitorCts.Token); } catch (OperationCanceledException) { return; }
                    St2Status s2 = null;
                    try { s2 = await EpsonPrinter.GetStatusAsync(address, monitorCts.Token); } catch (Exception) { }
                    if (s2 == null) continue;
                    printerState = s2.StateText;
                    if (s2.ErrorCode >= 0) printerError = s2.StateText;
                    progress.Report(new JobProgress
                    {
                        Page = curPage, PageCount = sequence.Count, PageFraction = curFraction,
                        Message = "sending", PrinterState = printerState
                    });
                }
            });

            try
            {
                using (var conn = new RawConnection())
                {
                    progress.Report(new JobProgress { Message = "connecting", PageCount = sequence.Count, PrinterState = printerState });
                    await conn.ConnectAsync(address, ct);
                    var writer = new EscprJobWriter(conn, job);
                    var g = writer.Geometry;
                    bool borderless = opt.Layout == Media.LayoutBorderless;

                    for (int i = 0; i < sequence.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (printerError != null) throw new InvalidOperationException("printer reports: " + printerError);
                        int remaining = sequence.Count - i;
                        if (i == 0) await writer.StartJobAsync(remaining, ct);
                        else await writer.StartPageAsync(remaining, ct);

                        int pageNo = i + 1;
                        curPage = pageNo;
                        var pageProgress = new Progress<double>(f =>
                        {
                            curFraction = f;
                            progress.Report(new JobProgress
                            {
                                Page = pageNo, PageCount = sequence.Count, PageFraction = f,
                                Message = "sending", PrinterState = printerState
                            });
                        });
                        await PhotoRasterizer.RenderAsync(sequence[i].File, g.PrintableWidth, g.PrintableHeight,
                            sequence[i].Rotation,
                            fill: kind == PrintKind.Photo && borderless,
                            autoRotate: kind == PrintKind.Photo,
                            sharpness: kind == PrintKind.Photo ? opt.Sharpness : 0,
                            grayscale: opt.ColorMode == Media.ColorMono,
                            sink: writer.WriteLineAsync,
                            progress: pageProgress,
                            ct: ct);
                        await writer.EndPageAsync(remaining - 1, ct);
                    }
                    await writer.EndJobAsync(ct);
                    progress.Report(new JobProgress
                    {
                        Page = sequence.Count, PageCount = sequence.Count, PageFraction = 1,
                        Message = "data sent, waiting for the printer", PrinterState = printerState
                    });
                    await Task.Delay(1500, ct);
                }
            }
            finally
            {
                monitorCts.Cancel();
                try { await monitor; } catch (Exception) { }
            }

            // After the data is sent, watch the printer for up to ~20 s: an error here
            // (paper out, wrong paper, cover open...) is the usual reason nothing comes out.
            for (int t = 0; t < 7; t++)
            {
                St2Status s3 = null;
                try { s3 = await EpsonPrinter.GetStatusAsync(address, ct); } catch (OperationCanceledException) { throw; } catch (Exception) { }
                if (s3 != null)
                {
                    printerState = s3.StateText;
                    progress.Report(new JobProgress
                    {
                        Page = sequence.Count, PageCount = sequence.Count, PageFraction = 1,
                        Message = "data sent", PrinterState = printerState
                    });
                    if (s3.ErrorCode >= 0) throw new InvalidOperationException("printer reports: " + s3.StateText);
                    if (s3.State == PrinterState.SelfPrinting || s3.State == PrinterState.Busy) return;
                }
                await Task.Delay(3000, ct);
            }
        }
    }
}
