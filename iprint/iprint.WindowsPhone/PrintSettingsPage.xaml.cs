using System;
using System.Collections.Generic;
using System.Linq;
using iprint.Escpr;
using iprint.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace iprint
{
    public sealed partial class PrintSettingsPage : Page
    {
        static readonly List<Option> RangeOptions = new List<Option>
        {
            new Option(0, "all pages"),
            new Option(1, "page range"),
        };

        public PrintSettingsPage()
        {
            this.InitializeComponent();
            CopiesBox.ItemsSource = Enumerable.Range(1, 30).ToList();
            RangeBox.ItemsSource = RangeOptions;
            SizeBox.ItemsSource = Media.Sizes;
            TypeBox.ItemsSource = Media.Types;
            LayoutBox.ItemsSource = Media.Layouts;
            QualityBox.ItemsSource = Media.Qualities;
            SourceBox.ItemsSource = Media.Sources;
            ColorBox.ItemsSource = Media.Colors;
            SharpBox.ItemsSource = Media.Sharpness;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            var o = PrintSession.Options;
            bool photo = PrintSession.Kind == PrintKind.Photo;
            KindTitle.Text = photo ? "PRINT PHOTOS" : "PRINT DOCUMENTS";
            RangePanel.Visibility = photo ? Visibility.Collapsed : Visibility.Visible;
            SharpBox.Visibility = photo ? Visibility.Visible : Visibility.Collapsed;

            CopiesBox.SelectedItem = Math.Max(1, Math.Min(30, o.Copies));
            bool range = o.RangeFrom > 0 || o.RangeTo > 0;
            RangeBox.SelectedIndex = range ? 1 : 0;
            int pages = PrintSession.DocumentPages == null ? 0 : PrintSession.DocumentPages.Count;
            FromBox.Text = (o.RangeFrom > 0 ? o.RangeFrom : 1).ToString();
            ToBox.Text = (o.RangeTo > 0 ? o.RangeTo : Math.Max(1, pages)).ToString();
            RangeFields.Visibility = range ? Visibility.Visible : Visibility.Collapsed;

            SizeBox.SelectedItem = Media.SizeById(o.MediaSizeId);
            TypeBox.SelectedItem = Media.Find(Media.Types, o.MediaTypeId);
            LayoutBox.SelectedItem = Media.Find(Media.Layouts, o.Layout);
            QualityBox.SelectedItem = Media.Find(Media.Qualities, o.Quality);
            SourceBox.SelectedItem = Media.Find(Media.Sources, o.PaperSource);
            ColorBox.SelectedItem = Media.Find(Media.Colors, o.ColorMode);
            SharpBox.SelectedItem = Media.Find(Media.Sharpness, o.Sharpness);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            var o = PrintSession.Options;
            if (CopiesBox.SelectedItem is int) o.Copies = (int)CopiesBox.SelectedItem;
            var size = SizeBox.SelectedItem as MediaSize;
            if (size != null) o.MediaSizeId = size.Id;
            o.MediaTypeId = Value(TypeBox, o.MediaTypeId);
            o.Layout = Value(LayoutBox, o.Layout);
            o.Quality = Value(QualityBox, o.Quality);
            o.PaperSource = Value(SourceBox, o.PaperSource);
            o.ColorMode = Value(ColorBox, o.ColorMode);
            o.Sharpness = Value(SharpBox, o.Sharpness);

            if (RangeBox.SelectedIndex == 1)
            {
                int from, to;
                o.RangeFrom = int.TryParse(FromBox.Text, out from) ? Math.Max(1, from) : 1;
                o.RangeTo = int.TryParse(ToBox.Text, out to) ? Math.Max(o.RangeFrom, to) : 0;
            }
            else
            {
                o.RangeFrom = o.RangeTo = 0;
            }
            AppSettings.Save(PrintSession.KindKey, o);
        }

        static int Value(ComboBox box, int fallback)
        {
            var opt = box.SelectedItem as Option;
            return opt == null ? fallback : opt.Value;
        }

        void RangeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RangeFields != null)
                RangeFields.Visibility = RangeBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
