using System;
using System.Windows;

namespace NoteCards
{
    public static class ThemeManager
    {
        public static void SetTheme(string theme)
        {
            var dict = new ResourceDictionary();

            if (theme == "Dark")
                dict.Source = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
            else
                dict.Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative);

            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(dict);
        }
    }
}