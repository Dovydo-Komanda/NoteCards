using System.Collections.ObjectModel;
using System.Windows.Input;
using NoteCards.Models;
using System.Text.Json;
using System.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Windows.Data;

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
        // Create a view for Notes so we can apply filtering for search
        _notesView = CollectionViewSource.GetDefaultView(Notes);
        _notesView.Filter = (o) => FilterNotes(o);
        Notes.CollectionChanged += (_, _) => RefreshRecentNotes();
        RefreshRecentNotes();
        AddNoteCommand = new RelayCommand(AddNote);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);

        // Try to load saved notes from disk. If none exist, seed a test note.
        if (!LoadNotes())
        {
            var testDocument = new NoteDocument
            {
                Title = "Pirmasis konspektas",
                Content = "Tai yra testinis dokumentas, skirtas patikrinti NoteCards funkcionalumą."
            };
            Notes.Add(new NoteCardViewModel(testDocument, DeleteNote));
            SaveNotes();
        }
    }

    public ObservableCollection<NoteCardViewModel> Notes { get; }

    private readonly ICollectionView _notesView;
    public ICollectionView NotesView => _notesView;

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value ?? string.Empty;
                OnPropertyChanged(nameof(SearchQuery));
                _notesView.Refresh();
            }
        }
    }

    public ICommand AddNoteCommand { get; }

    private void AddNote()
    {
        var document = new NoteDocument
        {
            Title = "Naujas konspektas",
            Content = string.Empty
        };
        Notes.Add(new NoteCardViewModel(document, DeleteNote));
        SaveNotes();
    }

    private void DeleteNote(NoteCardViewModel noteCard)
    {
        Notes.Remove(noteCard);
        SaveNotes();
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

    private bool FilterNotes(object? obj)
    {
        if (obj is not NoteCardViewModel note)
            return false;

        if (string.IsNullOrWhiteSpace(SearchQuery))
            return true;

        var q = SearchQuery.Trim();
        // Case-insensitive contains on title or content
        return (note.Title?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            || (note.Content?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
    }
    // Persistence: save/load notes to a local JSON file
    private static string GetNotesFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "notes.json");
    }

    public void SaveNotes()
    {
        try
        {
            var docs = new List<NoteDocument>();
            foreach (var vm in Notes)
                docs.Add(vm.Document);

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(docs, opts);
            File.WriteAllText(GetNotesFilePath(), json);
        }
        catch
        {
            // Ignore persistence errors for now
        }
    }

    private bool LoadNotes()
    {
        try
        {
            var path = GetNotesFilePath();
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var docs = JsonSerializer.Deserialize<List<NoteDocument>>(json);
            if (docs == null || docs.Count == 0)
                return false;

            Notes.Clear();
            foreach (var doc in docs)
                Notes.Add(new NoteCardViewModel(doc, DeleteNote));

            return true;
        }
        catch
        {
            return false;
        }
    }
}
