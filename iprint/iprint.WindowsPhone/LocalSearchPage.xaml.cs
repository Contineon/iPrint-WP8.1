using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using iprint.Escpr;
using iprint.Models;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed class FoundPrinter
    {
        public PrinterInfo Info { get; set; }
        public string Name { get { return Info.Name; } }
        public string Subtitle
        {
            get { return Info.Address + (Info.SupportsEscpr ? "" : "  ·  not supported (no ESC/P-R)"); }
        }
    }

    public sealed partial class LocalSearchPage : Page
    {
        readonly ObservableCollection<FoundPrinter> _found = new ObservableCollection<FoundPrinter>();
        CancellationTokenSource _cts;

        public LocalSearchPage()
        {
            this.InitializeComponent();
            ResultList.ItemsSource = _found;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            Search();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        async void Search()
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _found.Clear();
            SearchingPanel.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            StateText.Text = "searching for printers";
            SearchButton.IsEnabled = false;
            try
            {
                await EpsonPrinter.DiscoverAsync(p =>
                {
                    var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
                    {
                        if (_found.Any(f => f.Info.Address == p.Address)) return;
                        _found.Add(new FoundPrinter { Info = p });
                    });
                }, 6000, token);
            }
            catch (Exception)
            {
                // network not available
            }
            if (token.IsCancellationRequested) return;
            Progress.IsIndeterminate = false;
            SearchButton.IsEnabled = true;
            if (_found.Count == 0)
            {
                StateText.Text = "no printers found. Make sure the phone and the printer are on the same Wi-Fi network, or use manual ip.";
            }
            else
            {
                SearchingPanel.Visibility = Visibility.Collapsed;
            }
        }

        void Search_Click(object sender, RoutedEventArgs e)
        {
            Search();
        }

        async void ResultList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var p = e.ClickedItem as FoundPrinter;
            if (p == null) return;
            if (!p.Info.SupportsEscpr)
            {
                await new MessageDialog("This printer does not support the ESC/P-R language, so it can't be used by this app.",
                    p.Name).ShowAsync();
                return;
            }
            AppSettings.Printer = p.Info;
            if (_cts != null) _cts.Cancel();
            Frame.GoBack();
        }
    }
}
