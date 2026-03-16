using System.Windows;
using System.Windows.Controls;
using NoteCards.Localization;

namespace NoteCards.Views
{
    public partial class SettingsPanel : UserControl
    {
        public SettingsPanel()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(LocalizationService.GetString("LatestVersion"),
                            LocalizationService.GetString("AppUpdate"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }
    }
}