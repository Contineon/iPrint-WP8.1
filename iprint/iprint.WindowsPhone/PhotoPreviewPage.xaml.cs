using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using iprint.Imaging;
using iprint.Models;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed class ThumbItem : INotifyPropertyChanged
    {
        ImageSource _thumb;
        public PrintPage Page { get; set; }
        public ImageSource Thumbnail
        {
            get { return _thumb; }
            set { _thumb = value; var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs("Thumbnail")); }
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public sealed partial class PhotoPreviewPage : Page, IFileOpenPickerContinuable
    {
        readonly ObservableCollection<ThumbItem> _items = new ObservableCollection<ThumbItem>();
        CancellationTokenSource _previewCts;

        public PhotoPreviewPage()
        {
            this.InitializeComponent();
            Strip.ItemsSource = _items;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            PrintSession.Kind = PrintKind.Photo;
            if (App.PendingPick != null)
            {
                var pick = App.PendingPick;
                App.PendingPick = null;
                PrintSession.Photos.Clear();
                RebuildStrip();
                ContinueFileOpenPicker(pick);
                return;
            }
            if (e.NavigationMode == NavigationMode.New && "new".Equals(e.Parameter))
            {
                PrintSession.Photos.Clear();
                RebuildStrip();
                OpenPicker();
                return;
            }
            RebuildStrip();
        }

        void OpenPicker()
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.Thumbnail,
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" })
                picker.FileTypeFilter.Add(ext);
            picker.ContinuationData["target"] = "photo";
            picker.PickMultipleFilesAndContinue();
        }

        public void ContinueFileOpenPicker(FileOpenPickerContinuationEventArgs args)
        {
            foreach (var f in args.Files) PrintSession.Photos.Add(new PrintPage { File = f });
            if (PrintSession.Photos.Count == 0)
            {
                if (Frame.CanGoBack) Frame.GoBack();
                return;
            }
            RebuildStrip();
            Strip.SelectedIndex = _items.Count - args.Files.Count;
        }

        async void RebuildStrip()
        {
            _items.Clear();
            foreach (var p in PrintSession.Photos) _items.Add(new ThumbItem { Page = p });
            UpdateState();
            if (_items.Count > 0 && Strip.SelectedIndex < 0) Strip.SelectedIndex = 0;
            else ShowPreview();

            foreach (var item in _items.ToList())
            {
                try
                {
                    using (var thumb = await item.Page.File.GetThumbnailAsync(ThumbnailMode.PicturesView, 120))
                    {
                        if (thumb == null) continue;
                        var bmp = new BitmapImage();
                        await bmp.SetSourceAsync(thumb);
                        item.Thumbnail = bmp;
                    }
                }
                catch (Exception) { }
            }
        }

        void UpdateState()
        {
            bool any = _items.Count > 0;
            EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            ClearButton.IsEnabled = RotateButton.IsEnabled = PrintButton.IsEnabled = any;
            CounterText.Text = any ? string.Format("{0} / {1}", Math.Max(0, Strip.SelectedIndex) + 1, _items.Count) : "";
        }

        ThumbItem Current
        {
            get { return Strip.SelectedItem as ThumbItem; }
        }

        void Strip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateState();
            ShowPreview();
        }

        async void ShowPreview()
        {
            if (_previewCts != null) _previewCts.Cancel();
            var item = Current;
            if (item == null) { PreviewImage.Source = null; return; }
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;
            PreviewBusy.IsActive = true;
            try
            {
                var bmp = await PreviewRenderer.RenderAsync(item.Page.File, item.Page.Rotation, PrintSession.Options,
                    true, 480, token);
                if (!token.IsCancellationRequested) PreviewImage.Source = bmp;
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                if (!token.IsCancellationRequested) PreviewImage.Source = null;
            }
            if (!token.IsCancellationRequested) PreviewBusy.IsActive = false;
        }

        void Add_Click(object sender, RoutedEventArgs e) { OpenPicker(); }

        void Clear_Click(object sender, RoutedEventArgs e)
        {
            var item = Current;
            if (item == null) return;
            int idx = Strip.SelectedIndex;
            PrintSession.Photos.Remove(item.Page);
            _items.Remove(item);
            if (_items.Count > 0) Strip.SelectedIndex = Math.Min(idx, _items.Count - 1);
            else ShowPreview();
            UpdateState();
        }

        void Rotate_Click(object sender, RoutedEventArgs e)
        {
            var item = Current;
            if (item == null) return;
            item.Page.Rotation = (item.Page.Rotation + 90) % 360;
            ShowPreview();
        }

        void Settings_Click(object sender, RoutedEventArgs e)
        {
            Frame.Navigate(typeof(PrintSettingsPage));
        }

        void Print_Click(object sender, RoutedEventArgs e)
        {
            if (PrintSession.Photos.Count == 0) return;
            if (AppSettings.Printer == null) { Frame.Navigate(typeof(MainPage)); return; }
            Frame.Navigate(typeof(PrintingPage));
        }
    }
}
