using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteCards.Models;

namespace NoteCards.ViewModels;

public class MainViewModel : ViewModelBase
{
    private bool _enableScrollbar = true;
    public bool EnableScrollbar
    {
        get => _enableScrollbar;
        set
        {
            if (_enableScrollbar != value)
            {
                _enableScrollbar = value;
                OnPropertyChanged(nameof(EnableScrollbar));
            }
        }
    }

    public MainViewModel()
    {
        Notes = new ObservableCollection<NoteCardViewModel>();
        Notes.CollectionChanged += (_, _) => RefreshRecentNotes();
        RefreshRecentNotes();
        AddNoteCommand = new RelayCommand(AddNote);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);

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

    private bool _isSidebarExpanded = true;

    public double SidebarWidth
    {
        get => _isSidebarExpanded ? 220 : 60;
    }

    public ICommand ToggleSidebarCommand { get; }

    private void ToggleSidebar()
    {
        _isSidebarExpanded = !_isSidebarExpanded;
        OnPropertyChanged(nameof(SidebarWidth));
    }
    public ObservableCollection<NoteCardViewModel> RecentNotes { get; } = new();
    public void RefreshRecentNotes()
    {
        var recent = Notes
            .OrderByDescending(n => n.Document.LastModified)
            .Take(5)
            .ToList();
        RecentNotes.Clear();
        foreach (var note in recent)
            RecentNotes.Add(note);
    }

}
