using System;
using System.Collections.Generic;
using System.Threading;
using iprint.Escpr;
using iprint.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed class MenuEntry
    {
        public string Title { get; set; }
        public Action Invoke { get; set; }
    }

    public sealed partial class MainPage : Page
    {
        CancellationTokenSource _cts;
        string _lastAddress;

        public MainPage()
        {
            this.InitializeComponent();
            this.NavigationCacheMode = NavigationCacheMode.Required;

            AddMenu.ItemsSource = new List<MenuEntry>
            {
                new MenuEntry { Title = "local", Invoke = () => Frame.Navigate(typeof(LocalSearchPage)) },
                new MenuEntry { Title = "manual ip", Invoke = () => Frame.Navigate(typeof(ManualIpPage)) },
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var printer = AppSettings.Printer;
            if (printer == null)
            {
                _lastAddress = null;
                PrinterItem.Header = "no printer";
                PrinterStatusText.Text = "swipe to add printer and pick your Epson printer";
                PrinterMenu.ItemsSource = new List<MenuEntry>
                {
                    new MenuEntry { Title = "add printer", Invoke = () => MainPivot.SelectedIndex = 1 },
                };
                return;
            }

            PrinterItem.Header = printer.Name;
            PrinterStatusText.Text = printer.Address;
            PrinterMenu.ItemsSource = new List<MenuEntry>
            {
                new MenuEntry { Title = "print photos", Invoke = () => Start(PrintKind.Photo) },
                new MenuEntry { Title = "print documents", Invoke = () => Start(PrintKind.Document) },
                new MenuEntry { Title = "settings", Invoke = () => Frame.Navigate(typeof(MaintenancePage)) },
            };
            if (_lastAddress != printer.Address) MainPivot.SelectedIndex = 0;
            _lastAddress = printer.Address;
            RefreshStatus(printer);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        async void RefreshStatus(PrinterInfo printer)
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            try
            {
                var st = await EpsonPrinter.GetStatusAsync(printer.Address, token);
                if (token.IsCancellationRequested) return;
                PrinterStatusText.Text = printer.Address + "  ·  " + (st == null ? "not responding" : st.StateText);
            }
            catch (Exception)
            {
                if (!token.IsCancellationRequested) PrinterStatusText.Text = printer.Address + "  ·  not responding";
            }
        }

        void Start(PrintKind kind)
        {
            PrintSession.Kind = kind;
            PrintSession.Options = AppSettings.Load(PrintSession.KindKey);
            if (kind == PrintKind.Photo) Frame.Navigate(typeof(PhotoPreviewPage), "new");
            else Frame.Navigate(typeof(DocumentPreviewPage), "new");
        }

        void Menu_ItemClick(object sender, ItemClickEventArgs e)
        {
            var entry = e.ClickedItem as MenuEntry;
            if (entry != null && entry.Invoke != null) entry.Invoke();
        }
    }
}
