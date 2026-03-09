using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using NoteCards.ViewModels;

namespace NoteCards.Controls;

public partial class NoteCardControl : UserControl
{
    public NoteCardControl()
    {
        InitializeComponent();

        // Add click event to the whole control
        this.MouseLeftButtonUp += NoteCardControl_MouseLeftButtonUp;
        this.Cursor = Cursors.Hand;
    }

    private void NoteCardControl_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Get the ViewModel from DataContext
        var viewModel = this.DataContext as NoteCardViewModel;
        var mainWindow = Window.GetWindow(this) as MainWindow;

        // Only proceed if both exist
        if (viewModel != null && mainWindow != null)
        {
            mainWindow.OpenNoteEditor(viewModel);
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is NoteCardViewModel old)
            old.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is NoteCardViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(NoteCardViewModel.IsDeleting)) return;
        if (sender is not NoteCardViewModel vm || !vm.IsDeleting) return;

        var sb = ((Storyboard)Resources["ExitStoryboard"]).Clone();
        sb.Completed += (_, _) => vm.ExecuteDelete();
        sb.Begin(this);
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
