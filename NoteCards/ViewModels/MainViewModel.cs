using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public class MainViewModel : ViewModelBase
{
    public MainViewModel()
    {
        Notes = new ObservableCollection<NoteCardViewModel>();
        AddNoteCommand = new RelayCommand(AddNote);

        // Seed a test note
        var testDocument = new NoteDocument
        {
            Title = "Pirmasis konspektas",
            Content = "Tai yra testinis dokumentas, skirtas patikrinti NoteCards funkcionalumą."
        };
        Notes.Add(new NoteCardViewModel(testDocument, DeleteNote));
    }

    public ObservableCollection<NoteCardViewModel> Notes { get; }

    public ICommand AddNoteCommand { get; }

    private void AddNote()
    {
        var document = new NoteDocument
        {
            Title = "Naujas konspektas",
            Content = string.Empty
        };
        Notes.Add(new NoteCardViewModel(document, DeleteNote));
    }

    private void DeleteNote(NoteCardViewModel noteCard)
    {
        Notes.Remove(noteCard);
    }
}
