using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using iprint.Escpr;
using iprint.Models;
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

namespace iprint
{
    public sealed partial class MaintenancePage : Page
    {
        CancellationTokenSource _cts;
        PrinterInfo _printer;
        bool _busy;

        public MaintenancePage()
        {
            this.InitializeComponent();
            ActionList.ItemsSource = new List<MenuEntry>
            {
                new MenuEntry { Title = "head cleaning", Invoke = () => RunMaintenance(false) },
                new MenuEntry { Title = "nozzle check", Invoke = () => RunMaintenance(true) },
                new MenuEntry { Title = "delete device", Invoke = DeleteDevice },
            };
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            _printer = AppSettings.Printer;
            if (_printer == null) { Frame.GoBack(); return; }
            PrinterTitle.Text = _printer.Name.ToUpperInvariant();
            Refresh();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_cts != null) _cts.Cancel();
        }

        void Refresh_Click(object sender, RoutedEventArgs e) { Refresh(); }

        async void Refresh()
        {
            if (_cts != null) _cts.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            StatusText.Text = "reading printer status";
            St2Status st = null;
            try { st = await EpsonPrinter.GetStatusAsync(_printer.Address, token); }
            catch (Exception) { }
            if (token.IsCancellationRequested) return;
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Collapsed;
            Show(st);
        }

        void Show(St2Status st)
        {
            InkPanel.Children.Clear();
            BoxPanel.Children.Clear();
            if (st == null)
            {
                StatusText.Text = _printer.Address + "  ·  printer is not responding" +
                    (string.IsNullOrEmpty(EpsonPrinter.LastStatusError) ? "" : " (" + EpsonPrinter.LastStatusError + ")");
                InkNote.Text = "turn the printer on and connect the phone to the same Wi-Fi network.";
                InkNote.Visibility = Visibility.Visible;
                BoxSection.Visibility = Visibility.Collapsed;
                return;
            }
            StatusText.Text = _printer.Address + "  ·  " + st.StateText;

            bool anyLevel = false;
            foreach (var ink in st.Inks)
            {
                InkPanel.Children.Add(MakeBar(ink.Label, ParseColor(ink.ColorHex), ink.Level < 0 ? 0 : ink.Level,
                    InkCaption(ink)));
                if (ink.Level >= 0) anyLevel = true;
            }
            if (st.Inks.Count == 0 || !anyLevel)
            {
                InkNote.Text = st.Inks.Count == 0
                    ? "the printer did not report ink information."
                    : "this printer does not measure ink. Check the ink tanks visually and refill before the ink drops below the lower line.";
                InkNote.Visibility = Visibility.Visible;
            }
            else InkNote.Visibility = Visibility.Collapsed;

            if (st.MaintenanceBoxes.Count > 0)
            {
                BoxSection.Visibility = Visibility.Visible;
                // ST2 field 0x37 only reports a state per waste-ink counter (no percentage).
                // L-series has two: the main pad counter and the extra (borderless) one.
                for (int i = 0; i < st.MaintenanceBoxes.Count; i++)
                {
                    string name = st.MaintenanceBoxes.Count == 2 ? (i == 0 ? "main counter" : "extra counter")
                                                               : "counter " + (i + 1);
                    string state;
                    switch (st.MaintenanceBoxes[i])
                    {
                        case MaintenanceBoxState.NotFull: state = "not full"; break;
                        case MaintenanceBoxState.NearFull: state = "nearly full"; break;
                        case MaintenanceBoxState.Full: state = "full — service required"; break;
                        default: state = "unknown"; break;
                    }
                    var row = new TextBlock { Style = (Style)Application.Current.Resources["ItemTextStyle"], TextWrapping = TextWrapping.Wrap };
                    row.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = name + "  " });
                    row.Inlines.Add(new Windows.UI.Xaml.Documents.Run { Text = state, Foreground = Theme.Brush("AppSubtleBrush") });
                    BoxPanel.Children.Add(row);
                }
                BoxPanel.Children.Add(new TextBlock
                {
                    Style = (Style)Application.Current.Resources["SubtleTextStyle"],
                    Text = "the printer reports only the state of each counter, not the percentage."
                });
            }
            else BoxSection.Visibility = Visibility.Collapsed;
            LoadWasteCounters();
        }

        /// <summary>Replaces the ST2 state rows with real percentages read from EEPROM (known models).</summary>
        async void LoadWasteCounters()
        {
            if (!EpsonPrinter.HasWasteCounters(_printer.Model)) return;
            var token = _cts == null ? CancellationToken.None : _cts.Token;
            List<EpsonPrinter.WasteCounter> counters;
            try { counters = await EpsonPrinter.ReadWasteCountersAsync(_printer.Address, _printer.Model, token); }
            catch (OperationCanceledException) { return; }
            if (counters == null || token.IsCancellationRequested) return;
            BoxSection.Visibility = Visibility.Visible;
            BoxPanel.Children.Clear();
            foreach (var c in counters)
            {
                BoxPanel.Children.Add(new TextBlock { Text = c.Name, Style = (Style)Application.Current.Resources["ItemTextStyle"] });
                string caption = c.Percent.ToString("0.00") + "%" + (c.Percent >= 100 ? " · full" : c.Percent >= 90 ? " · almost full" : "");
                BoxPanel.Children.Add(MakeBar("", Color.FromArgb(255, 128, 128, 128), (int)Math.Round(Math.Min(100, c.Percent)), caption));
            }
        }

        static string InkCaption(InkInfo ink)
        {
            switch (ink.State)
            {
                case InkState.End: return "empty";
                case InkState.Low: return ink.Level + "% · low";
                case InkState.Fail: return "error";
                case InkState.NotPresent: return "missing";
                case InkState.NotRead: return "can't read";
                case InkState.NotAvailable: return "—";
                default: return ink.Level >= 0 ? ink.Level + "%" : "—";
            }
        }

        /// <summary>Figma ink bar: label, outlined track (stroke = foreground), coloured fill.</summary>
        FrameworkElement MakeBar(string label, Color color, int percent, string caption)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(label.Length == 0 ? 0 : 52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });

            var text = new TextBlock
            {
                Text = label,
                FontSize = 24,
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(text);

            var track = new Grid();
            percent = Math.Max(0, Math.Min(100, percent));
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(percent, 0.001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - percent, 0.001), GridUnitType.Star) });
            var fill = new Rectangle { Fill = new SolidColorBrush(color) };
            track.Children.Add(fill);

            var border = new Border
            {
                Height = 36,
                BorderThickness = new Thickness(5),
                Padding = new Thickness(5),
                BorderBrush = Theme.Brush("AppBarOutlineBrush"),
                Background = Theme.Brush("AppTrackBrush"),
                Child = track
            };
            Grid.SetColumn(border, 1);
            row.Children.Add(border);

            var cap = new TextBlock
            {
                Text = caption,
                FontSize = 15,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Brush("AppSubtleBrush")
            };
            Grid.SetColumn(cap, 2);
            row.Children.Add(cap);
            return row;
        }

        static Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            return Color.FromArgb(255, Convert.ToByte(hex.Substring(0, 2), 16),
                Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
        }

        void ActionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var entry = e.ClickedItem as MenuEntry;
            if (entry != null && !_busy) entry.Invoke();
        }

        static async Task<bool> ConfirmAsync(string text, string title, string yes)
        {
            var dlg = new MessageDialog(text, title);
            bool ok = false;
            dlg.Commands.Add(new UICommand(yes, c => ok = true));
            dlg.Commands.Add(new UICommand("cancel"));
            dlg.DefaultCommandIndex = 0;
            dlg.CancelCommandIndex = 1;
            await dlg.ShowAsync();
            return ok;
        }

        async void RunMaintenance(bool nozzle)
        {
            string text = nozzle
                ? "Load a sheet of A4 plain paper. The printer will print the nozzle check pattern."
                : "Head cleaning uses some ink. Run it only if the nozzle check pattern has gaps.";
            if (!await ConfirmAsync(text, nozzle ? "nozzle check" : "head cleaning", "start")) return;

            _busy = true;
            Progress.Visibility = Visibility.Visible;
            Progress.IsIndeterminate = true;
            StatusText.Text = "checking printer";
            string result;
            try
            {
                var token = CancellationToken.None;
                var st = await EpsonPrinter.GetStatusAsync(_printer.Address, token);
                if (st != null && st.ErrorCode >= 0)
                {
                    result = "can't start: " + st.StateText;
                }
                else if (st != null && !st.IsReady)
                {
                    result = "the printer is " + st.StateText + ". Try again when it is ready.";
                }
                else
                {
                    StatusText.Text = "sending command";
                    await EpsonPrinter.RunMaintenanceAsync(_printer.Address, nozzle, token);
                    result = nozzle
                        ? "nozzle check sent. If there are gaps in the pattern, run head cleaning."
                        : "head cleaning started. Wait until the power light stops flashing (about a minute), then run nozzle check.";
                }
            }
            catch (Exception ex)
            {
                result = "failed: " + ex.Message;
            }
            _busy = false;
            Progress.IsIndeterminate = false;
            Progress.Visibility = Visibility.Collapsed;
            await new MessageDialog(result, nozzle ? "nozzle check" : "head cleaning").ShowAsync();
            Refresh();
        }

        async void DeleteDevice()
        {
            if (!await ConfirmAsync("Remove " + _printer.Name + " (" + _printer.Address + ") from this phone?",
                "delete device", "delete")) return;
            AppSettings.Printer = null;
            Frame.GoBack();
        }
    }
}
