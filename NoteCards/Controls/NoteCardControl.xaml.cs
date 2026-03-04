using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NoteCards.ViewModels;

namespace NoteCards.Controls;

public partial class NoteCardControl : UserControl
{
    public NoteCardControl()
    {
        InitializeComponent();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (DataContext is NoteCardViewModel vm)
            vm.IsMenuVisible = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (DataContext is NoteCardViewModel vm)
            vm.IsMenuVisible = false;
    }

    private void OnMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu != null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }
}
