using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using iprint.Imaging;
using iprint.Models;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed partial class DocumentPreviewPage : Page, IFileOpenPickerContinuable
    {
        static readonly string[] TextTypes = { ".txt", ".log", ".csv", ".md", ".ini", ".xml", ".json" };
        static readonly string[] ImageTypes = { ".jpg", ".jpeg", ".png", ".bmp" };

        readonly ObservableCollection<ThumbItem> _pages = new ObservableCollection<ThumbItem>();
        bool _busy;

        public DocumentPreviewPage()
        {
            this.InitializeComponent();
            Pages.ItemsSource = _pages;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            PrintSession.Kind = PrintKind.Document;
            if (App.PendingPick != null)
            {
                var pick = App.PendingPick;
                App.PendingPick = null;
                ContinueFileOpenPicker(pick);
                return;
            }
            if (e.NavigationMode == NavigationMode.New && "new".Equals(e.Parameter))
            {
                PrintSession.DocumentPages = new List<PrintPage>();
                PrintSession.DocumentName = null;
                ShowPages();
                OpenPicker();
                return;
            }
            ShowPages();
        }

        void OpenPicker()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary
            };
            foreach (var ext in TextTypes.Concat(ImageTypes)) picker.FileTypeFilter.Add(ext);
            picker.ContinuationData["target"] = "doc";
            picker.PickMultipleFilesAndContinue();
        }

        public async void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            var files = args.Files.ToList();
            if (files.Count == 0)
            {
                if (PrintSession.DocumentPages.Count == 0 && Frame.CanGoBack) Frame.GoBack();
                return;
            }
            _busy = true;
            PrintButton.IsEnabled = false;
            _pages.Clear();
            EmptyText.Visibility = Visibility.Collapsed;
            BusyPanel.Visibility = Visibility.Visible;
            RenderBox.Visibility = Visibility.Visible;
            Pages.Visibility = Visibility.Collapsed;
            var result = new List<PrintPage>();
            try
            {
                var root = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync("doc",
                    CreationCollisionOption.ReplaceExisting);
                int index = 0;
                foreach (var f in files)
                {
                    var ext = (f.FileType ?? "").ToLowerInvariant();
                    if (ImageTypes.Contains(ext))
                    {
                        result.Add(new PrintPage { File = f });
                        continue;
                    }
                    BusyText.Text = "preparing " + f.Name;
                    var text = await TextDocumentRenderer.ReadTextAsync(f);
                    var name = f.Name;
                    var folder = await root.CreateFolderAsync("f" + (index++), CreationCollisionOption.ReplaceExisting);
                    var rendered = await TextDocumentRenderer.RenderAsync(text, RenderHost, folder,
                        new Progress<int>(n => BusyText.Text = "preparing " + name + " · page " + n));
                    result.AddRange(rendered.Select(r => new PrintPage { File = r }));
                }
                PrintSession.DocumentPages = result;
                PrintSession.DocumentName = files.Count == 1 ? files[0].Name : files.Count + " files";
            }
            catch (Exception ex)
            {
                var ignore = new MessageDialog("can't open the document: " + ex.Message).ShowAsync();
            }
            _busy = false;
            BusyPanel.Visibility = Visibility.Collapsed;
            RenderBox.Visibility = Visibility.Collapsed;
            Pages.Visibility = Visibility.Visible;
            ShowPages();
        }

        async void ShowPages()
        {
            _pages.Clear();
            var pages = PrintSession.DocumentPages ?? new List<PrintPage>();
            DocTitle.Text = PrintSession.DocumentName ?? "print documents";
            EmptyText.Visibility = pages.Count == 0 && !_busy ? Visibility.Visible : Visibility.Collapsed;
            PrintButton.IsEnabled = pages.Count > 0 && !_busy;
            foreach (var p in pages) _pages.Add(new ThumbItem { Page = p });
            UpdateCounter();
            foreach (var item in _pages.ToList())
            {
                try
                {
                    using (var s = await item.Page.File.OpenReadAsync())
                    {
                        var bmp = new BitmapImage { DecodePixelWidth = 600 };
                        await bmp.SetSourceAsync(s);
                        item.Thumbnail = bmp;
                    }
                }
                catch (Exception) { }
            }
        }

        void UpdateCounter()
        {
            CounterText.Text = _pages.Count == 0 ? "" :
                string.Format("page {0} / {1}", Math.Max(0, Pages.SelectedIndex) + 1, _pages.Count);
        }

        void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateCounter(); }

        void Open_Click(object sender, RoutedEventArgs e) { if (!_busy) OpenPicker(); }

        void Settings_Click(object sender, RoutedEventArgs e) { Frame.Navigate(typeof(PrintSettingsPage)); }

        void Print_Click(object sender, RoutedEventArgs e)
        {
            if (_busy || PrintSession.DocumentPages.Count == 0) return;
            if (AppSettings.Printer == null) { Frame.Navigate(typeof(MainPage)); return; }
            Frame.Navigate(typeof(PrintingPage));
        }
    }
}
