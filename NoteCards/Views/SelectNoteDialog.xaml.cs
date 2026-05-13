using NoteCards.ViewModels;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace NoteCards.Views;

public partial class SelectNoteDialog : Window
{
    private readonly List<NoteCardViewModel> _allNotes;
    private readonly ObservableCollection<NoteCardViewModel> _filteredNotes;
    private NoteCardViewModel? _selectedNote;

    public SelectNoteDialog(IEnumerable<NoteCardViewModel> availableNotes)
    {
        InitializeComponent();
        _allNotes = availableNotes.ToList();
        _filteredNotes = new ObservableCollection<NoteCardViewModel>(_allNotes);
        NotesItemsControl.ItemsSource = _filteredNotes;
    }

    public NoteCardViewModel? SelectedNote => _selectedNote;

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchTextBox.Text.Trim().ToLowerInvariant();

        _filteredNotes.Clear();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            foreach (var note in _allNotes)
            {
                _filteredNotes.Add(note);
            }
        }
        else
        {
            foreach (var note in _allNotes)
            {
                if (note.Document.Title.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    note.Document.Content.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    _filteredNotes.Add(note);
                }
            }
        }
    }

    private void NoteItem_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as Border)?.DataContext is NoteCardViewModel note)
        {
            _selectedNote = note;
            SelectButton.IsEnabled = true;
        }
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        _selectedNote = null;
        Close();
    }
}
