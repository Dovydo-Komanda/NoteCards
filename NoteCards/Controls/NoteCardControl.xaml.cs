using NoteCards.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace NoteCards.Controls;

public partial class NoteCardControl : UserControl
{
    public NoteCardControl()
    {
        InitializeComponent();

        // Use PreviewMouseLeftButtonUp so clicks inside child controls still trigger opening the editor
        this.PreviewMouseLeftButtonUp += NoteCardControl_PreviewMouseLeftButtonUp;
        this.Cursor = Cursors.Hand;
    }

    private void NoteCardControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Don't open the editor when the three-dot menu button was clicked
        if (MenuButton.IsMouseOver)
            return;

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

    private void OnOpenMenuClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is NoteCardViewModel viewModel)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            mainWindow?.OpenNoteEditor(viewModel);
        }
    }

    private void OnOpenFromFileClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NoteCardViewModel viewModel)
            return;

        var dlg = new Microsoft.Win32.OpenFileDialog();
        dlg.Filter = "Text Files (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|All files (*.*)|*.*";
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var path = dlg.FileName;
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

                if (ext == ".rtf")
                {
                    // Read raw bytes from the RTF file and store as Base64 so we don't corrupt encoding
                    var bytes = System.IO.File.ReadAllBytes(path);
                    viewModel.Document.Content = Convert.ToBase64String(bytes);
                }
                else
                {
                    // Read raw bytes first
                    var rawBytes = System.IO.File.ReadAllBytes(path);

                    string content = null;

                    // 1) Try strict UTF-8 (throws on invalid sequences)
                    try
                    {
                        content = new System.Text.UTF8Encoding(false, true).GetString(rawBytes);
                    }
                    catch
                    {
                        // 2) Try BOM detection / StreamReader fallback
                        try
                        {
                            using (var ms = new System.IO.MemoryStream(rawBytes))
                            using (var sr = new System.IO.StreamReader(ms, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                            {
                                content = sr.ReadToEnd();
                            }
                        }
                        catch
                        {
                            // 3) Try system ANSI
                            try { content = System.Text.Encoding.Default.GetString(rawBytes); }
                            catch
                            {
                                // 4) Try Lithuanian code page
                                try { content = System.Text.Encoding.GetEncoding(1257).GetString(rawBytes); }
                                catch { content = string.Empty; }
                            }
                        }
                    }

                    viewModel.Document.Content = content ?? string.Empty;
                    // Set note title to file name (without extension) if empty
                    if (string.IsNullOrWhiteSpace(viewModel.Document.Title))
                        viewModel.Document.Title = System.IO.Path.GetFileNameWithoutExtension(path);
                }

                var mainWindow = Window.GetWindow(this) as MainWindow;
                mainWindow?.OpenNoteEditor(viewModel);
            }
            catch
            {
                MessageBox.Show("Failed to open file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
