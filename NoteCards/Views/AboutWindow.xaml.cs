using System.ComponentModel;
using System.Windows;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class AboutWindow : Window
{
    private bool _isClosing = false;

    public AboutWindow()
    {
        InitializeComponent();
    }

    private void AboutWindow_Closing(object sender, CancelEventArgs e)
    {
        if (_isClosing)
            return;

        e.Cancel = true;
        AnimateAndClose();
    }

    private void AnimateAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var sb = ((Storyboard)Resources["CloseStoryboard"]).Clone();
        sb.Completed += (_, _) => Close();
        sb.Begin(this);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
