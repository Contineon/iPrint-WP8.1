using System;
using System.Threading;
using iprint.Escpr;
using iprint.Models;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed partial class ManualIpPage : Page
    {
        CancellationTokenSource _cts;

        public ManualIpPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var p = AppSettings.Printer;
            if (p != null) AddressBox.Text = p.Address;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        static bool IsIpv4(string s)
        {
            var parts = s.Split('.');
            if (parts.Length != 4) return false;
            foreach (var part in parts)
            {
                int v;
                if (!int.TryParse(part, out v) || v < 0 || v > 255) return false;
            }
            return true;
        }

        void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Enter) Probe();
        }

        void Done_Click(object sender, RoutedEventArgs e)
        {
            Probe();
        }

        async void Probe()
        {
            var address = (AddressBox.Text ?? "").Trim().Replace(',', '.');
            if (!IsIpv4(address))
            {
                ResultText.Text = "enter an IPv4 address like 192.168.1.20";
                return;
            }
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            DoneButton.IsEnabled = false;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            ResultText.Text = "connecting to " + address;
            PrinterInfo info = null;
            try
            {
                info = await EpsonPrinter.ProbeAsync(address, token);
            }
            catch (Exception)
            {
            }
            if (token.IsCancellationRequested) return;
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Collapsed;
            DoneButton.IsEnabled = true;

            if (info == null)
            {
                ResultText.Text = "no Epson printer answered at " + address +
                                  ". Check the address on the printer's network status sheet and that SNMP is enabled.";
                return;
            }
            if (!info.SupportsEscpr)
            {
                ResultText.Text = info.Name + " was found, but it does not support ESC/P-R printing.";
                return;
            }
            AppSettings.Printer = info;
            Frame.GoBack();
        }
    }
}
