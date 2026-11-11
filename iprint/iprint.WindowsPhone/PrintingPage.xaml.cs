using System;
using System.Threading;
using System.Threading.Tasks;
using iprint.Models;
using Windows.System.Display;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed partial class PrintingPage : Page
    {
        CancellationTokenSource _cts;
        bool _finished;
        DisplayRequest _display;

        public PrintingPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.NavigationMode != NavigationMode.New) return;
            Run();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            App.BlockBack = false;
            if (_cts != null && !_finished) _cts.Cancel();
            ReleaseDisplay();
        }

        void ReleaseDisplay()
        {
            if (_display != null) { try { _display.RequestRelease(); } catch (Exception) { } _display = null; }
        }

        async void Run()
        {
            var printer = AppSettings.Printer;
            var pages = PrintSession.PagesToPrint();
            var options = PrintSession.Options;
            var kind = PrintSession.Kind;
            PrinterTitle.Text = printer.Name.ToUpperInvariant();

            _cts = new CancellationTokenSource();
            _finished = false;
            App.BlockBack = true;
            ActionButton.Content = "cancel";
            try { _display = new DisplayRequest(); _display.RequestActive(); } catch (Exception) { _display = null; }

            var progress = new Progress<JobProgress>(p =>
            {
                if (p.Page > 0)
                {
                    PageText.Text = string.Format("page {0} of {1}", p.Page, p.PageCount);
                    Progress.Value = ((p.Page - 1) + p.PageFraction) / Math.Max(1, p.PageCount);
                }
                else
                {
                    PageText.Text = p.Message;
                }
                DetailText.Text = p.Message + " · " + printer.Address +
                    (string.IsNullOrEmpty(p.PrinterState) ? "" : "\nprinter: " + p.PrinterState);
            });

            string error = null;
            var token = _cts.Token;
            try
            {
                await Task.Run(() => PrintJobRunner.RunAsync(printer.Address, kind, pages, options, progress, token));
            }
            catch (OperationCanceledException)
            {
                error = "canceled";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            _finished = true;
            App.BlockBack = false;
            ReleaseDisplay();
            if (error == null)
            {
                HeaderText.Text = "done";
                PageText.Text = pages.Count == 1 ? "sent 1 page" : string.Format("sent {0} pages", pages.Count);
                Progress.Value = 1;
                DetailText.Text = "the printer accepted the job";
            }
            else
            {
                HeaderText.Text = error == "canceled" ? "canceled" : "error";
                PageText.Text = error == "canceled" ? "printing canceled" : "printing failed";
                DetailText.Text = error == "canceled"
                    ? "pages already sent may still come out of the printer"
                    : error + ". Check that the printer is on and connected to the same Wi-Fi network.";
            }
            ActionButton.Content = "back";
        }

        void Action_Click(object sender, RoutedEventArgs e)
        {
            if (!_finished)
            {
                ActionButton.IsEnabled = false;
                DetailText.Text = "canceling";
                _cts.Cancel();
                ActionButton.IsEnabled = true;
                return;
            }
            if (Frame.CanGoBack) Frame.GoBack();
        }
    }
}
