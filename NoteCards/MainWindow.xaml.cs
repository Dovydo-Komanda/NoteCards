using System.Windows;

namespace NoteCards;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void HamburgerButton_Click(object sender, RoutedEventArgs e)
    {
        HamburgerPopup.IsOpen = !HamburgerPopup.IsOpen;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        HamburgerPopup.IsOpen = false;
        var about = new Views.AboutWindow { Owner = this };
        about.ShowDialog();
    }
}
