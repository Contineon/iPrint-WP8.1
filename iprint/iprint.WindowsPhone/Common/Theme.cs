using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace iprint
{
    /// <summary>Resolves brushes from App.xaml ThemeDictionaries for the current phone theme.</summary>
    public static class Theme
    {
        public static bool IsLight
        {
            get { return Application.Current.RequestedTheme == ApplicationTheme.Light; }
        }

        public static Brush Brush(string key)
        {
            var res = Application.Current.Resources;
            object dict;
            if (res.ThemeDictionaries.TryGetValue(IsLight ? "Light" : "Default", out dict))
            {
                var d = dict as ResourceDictionary;
                object v;
                if (d != null && d.TryGetValue(key, out v) && v is Brush) return (Brush)v;
            }
            object any;
            if (res.TryGetValue(key, out any) && any is Brush) return (Brush)any;
            return new SolidColorBrush(IsLight ? Colors.Black : Colors.White);
        }
    }
}
