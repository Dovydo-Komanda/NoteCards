using System.Windows;
using System.Windows.Controls;

namespace NoteCards.Views
{
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }

        private void ThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;

            if (combo.SelectedItem is ComboBoxItem item)
            {
                string theme = item.Content.ToString();
                ThemeManager.SetTheme(theme);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("You are using the latest version.",
                            "App Update",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }
    }
}