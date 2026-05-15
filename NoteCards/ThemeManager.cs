using NoteCards.Services;
using System.Windows;

namespace NoteCards
{
    public static class ThemeManager
    {
        public static event EventHandler? ThemeChanged;

        private static string _currentTheme = "Light";
        private static int _nativeThemeRefreshVersion;

        public static string CurrentTheme => _currentTheme;

        public static void SetTheme(string theme)
        {
            _currentTheme = theme;
            ApplyTheme(theme);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void ApplyTheme(string theme)
        {
            var dict = new ResourceDictionary();

            if (theme == "Dark")
                dict.Source = new Uri("pack://application:,,,/NoteCards;component/Themes/DarkTheme.xaml", UriKind.Absolute);
            else
                dict.Source = new Uri("pack://application:,,,/NoteCards;component/Themes/LightTheme.xaml", UriKind.Absolute);

            // Find and replace existing theme dictionary, preserving other dictionaries
            var themeDictIndex = -1;
            for (int i = 0; i < Application.Current.Resources.MergedDictionaries.Count; i++)
            {
                var md = Application.Current.Resources.MergedDictionaries[i];
                if (md.Source != null && (md.Source.OriginalString.Contains("DarkTheme") || md.Source.OriginalString.Contains("LightTheme")))
                {
                    themeDictIndex = i;
                    break;
                }
            }

            if (themeDictIndex >= 0)
            {
                Application.Current.Resources.MergedDictionaries[themeDictIndex] = dict;
            }
            else
            {
                Application.Current.Resources.MergedDictionaries.Insert(0, dict);
            }

            // Invalidate all windows
            var refreshVersion = ++_nativeThemeRefreshVersion;
            ApplyNativeThemeToOpenWindows(_currentTheme, invalidateVisuals: true, rebuildFrame: false);
            RefreshNativeThemeAfterLayout(_currentTheme, refreshVersion);
        }

        private static void ApplyNativeThemeToOpenWindows(string theme, bool invalidateVisuals, bool rebuildFrame)
        {
            foreach (Window window in Application.Current.Windows)
            {
                WindowThemeService.ApplyThemeWhenReady(window, theme, rebuildFrame);
                if (invalidateVisuals)
                    window.InvalidateVisual();
            }
        }

        private static void RefreshNativeThemeAfterLayout(string theme, int refreshVersion)
        {
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (refreshVersion == _nativeThemeRefreshVersion)
                {
                    ApplyNativeThemeToOpenWindows(theme, invalidateVisuals: false, rebuildFrame: true);
                }
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }
}
