using NoteCards.Models;
using NoteCards.Localization;
using NoteCards.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace NoteCards.ViewModels;

public class MainViewModel : ViewModelBase
{
    private const string DefaultGroupBackground = "#F8FAFF";
    private const int RecentNotesLimit = 20;
    private const string SortLastModifiedDesc = "last-modified-desc";
    private const string SortLastModifiedAsc = "last-modified-asc";
    private const string SortCreatedAtDesc = "created-at-desc";
    private const string SortCreatedAtAsc = "created-at-asc";
    private const string SortTitleAsc = "title-asc";
    private const string SortTitleDesc = "title-desc";

    private bool _isLoadingSettings;
    private bool _saveNotesQueued;
    private bool _enableScrollbar = true;
    private string _selectedLanguage = LocalizationService.English;
    private string _selectedTheme = "Light";
    private string _selectedSortOptionKey = SortLastModifiedDesc;
    private readonly Dictionary<Guid, NoteGroupData> _groupMetadata = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);

    public bool EnableScrollbar
    {
        get => _enableScrollbar;
        set
        {
            if (_enableScrollbar != value)
            {
                _enableScrollbar = value;
                OnPropertyChanged(nameof(EnableScrollbar));
                SaveAppSettings();
            }
        }
    }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            var normalized = LocalizationService.NormalizeLanguage(value);
            if (_selectedLanguage != normalized)
            {
                _selectedLanguage = normalized;
                OnPropertyChanged(nameof(SelectedLanguage));
                LocalizationService.SetCulture(_selectedLanguage);
                RefreshSortOptions();
                OnPropertyChanged(nameof(SortButtonText));
                SaveAppSettings();
            }
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            var normalized = string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
            if (_selectedTheme != normalized)
            {
                _selectedTheme = normalized;
                OnPropertyChanged(nameof(SelectedTheme));
                ThemeManager.SetTheme(_selectedTheme);
                SaveAppSettings();
            }
        }
    }

    public MainViewModel()
    {
        LoadAppSettings();


        Notes = new ObservableCollection<NoteCardViewModel>();
        NoteGroups = new ObservableCollection<NoteGroupViewModel>();
        TagFilters = new ObservableCollection<TagFilterItemViewModel>();
        SortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        // Create a view for Notes so we can apply filtering for search
        _notesView = CollectionViewSource.GetDefaultView(Notes);
        _notesView.Filter = FilterUngroupedNotes;
        ApplySortToUngroupedView();
        Notes.CollectionChanged += (_, _) =>
        {
            RefreshAvailableTags();
            ApplyFilters();
            RefreshActivityStats();
        };
        
        NoteCards.Services.ActivityTracker.ActivityUpdated += RefreshActivityStats;
        
        RefreshSortOptions();
        RefreshAvailableTags();
        RefreshRecentNotes();
        AddNoteCommand = new RelayCommand(AddNote);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ClearTagFiltersCommand = new RelayCommand(ClearTagFilters, () => HasActiveTagFilters);

        // Try to load saved notes from disk. If none exist, seed a test note.
        if (!LoadNotes())
        {
            var testDocument = new NoteDocument
            {
                Title = LocalizationService.GetString("FirstNoteTitle"),
                Content = LocalizationService.GetString("FirstNoteContent")
            };
            Notes.Add(CreateNoteCard(testDocument));
            SaveNotes();
        }

        RebuildGroups();
    }

    public ObservableCollection<NoteCardViewModel> Notes { get; }
    public ObservableCollection<NoteGroupViewModel> NoteGroups { get; }
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> SortOptions { get; }
    public bool HasGroups => NoteGroups.Count > 0;
    public bool HasTagFilters => TagFilters.Count > 0;
    public bool HasActiveTagFilters => _selectedTags.Count > 0;
    public string TagFilterButtonText => HasActiveTagFilters
        ? $"{LocalizationService.GetString("FilterTags")} ({_selectedTags.Count})"
        : LocalizationService.GetString("FilterTags");
    public string SortButtonText => string.Format(
        LocalizationService.GetString("SortButtonFormat"),
        GetSortOptionDisplayName(_selectedSortOptionKey));

    public string SelectedSortOptionKey
    {
        get => _selectedSortOptionKey;
        set
        {
            var normalized = NormalizeSortOptionKey(value);
            if (!SetProperty(ref _selectedSortOptionKey, normalized))
                return;

            UpdateSortOptionSelection();
            ApplySortToUngroupedView();
            OnPropertyChanged(nameof(SortButtonText));
            ApplyFilters();
            SaveAppSettings();
        }
    }

    private readonly ICollectionView _notesView;
    public ICollectionView NotesView => _notesView;

    private bool _isRecentSectionExpanded = true;
    public bool IsRecentSectionExpanded
    {
        get => _isRecentSectionExpanded;
        set
        {
            if (SetProperty(ref _isRecentSectionExpanded, value))
                SaveAppSettings();
        }
    }

    private bool _isGroupsSectionExpanded = true;
    public bool IsGroupsSectionExpanded
    {
        get => _isGroupsSectionExpanded;
        set
        {
            if (SetProperty(ref _isGroupsSectionExpanded, value))
                SaveAppSettings();
        }
    }

    private bool _isUngroupedSectionExpanded = true;
    public bool IsUngroupedSectionExpanded
    {
        get => _isUngroupedSectionExpanded;
        set
        {
            if (SetProperty(ref _isUngroupedSectionExpanded, value))
                SaveAppSettings();
        }
    }

    private bool _isGroupsFirst = true;
    public bool IsGroupsFirst
    {
        get => _isGroupsFirst;
        set
        {
            if (SetProperty(ref _isGroupsFirst, value))
            {
                CommandManager.InvalidateRequerySuggested();
                SaveAppSettings();
            }
        }
    }

    // Activity Summary Properties
    public string UserActivitySummaryTitle => LocalizationService.GetString("UserActivitySummaryTitle") ?? "User Activity Summary";
    public string StatsTotalNotes => $"Total Notes: {Notes.Count}";
    public string StatsWordsTyped => $"Total Words: {AppSettingsService.Load().TotalWordsTyped}";
    public string StatsCharactersTyped => $"Characters Typed: {AppSettingsService.Load().TotalCharactersTyped}";
    public string StatsTimeSpent => $"Time Spent: {GetTotalTimeSpent()}";
    public string StatsLastActive => $"Last Active: {AppSettingsService.Load().LastActiveDate?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}";

    private string GetTotalTimeSpent()
    {
        var span = TimeSpan.FromSeconds(AppSettingsService.Load().TotalTimeSpentSeconds);
        return $"{(int)span.TotalHours}h {span.Minutes}m";
    }

    public void RefreshActivityStats()
    {
        OnPropertyChanged(nameof(StatsTotalNotes));
        OnPropertyChanged(nameof(StatsWordsTyped));
        OnPropertyChanged(nameof(StatsCharactersTyped));
        OnPropertyChanged(nameof(StatsTimeSpent));
        OnPropertyChanged(nameof(StatsLastActive));
    }

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
                ApplyFilters();
            }
        }
    }

    public ICommand AddNoteCommand { get; }
    public ICommand ToggleRecentSectionCommand { get; }
    public ICommand ToggleGroupsSectionCommand { get; }
    public ICommand ToggleUngroupedSectionCommand { get; }
    public ICommand MoveGroupsUpCommand { get; }
    public ICommand MoveGroupsDownCommand { get; }
    public ICommand MoveUngroupedUpCommand { get; }
    public ICommand MoveUngroupedDownCommand { get; }
    public ICommand ClearTagFiltersCommand { get; }

    public void SetTagFilterSelected(string tag, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        if (isSelected)
            _selectedTags.Add(tag);
        else
            _selectedTags.Remove(tag);

        OnPropertyChanged(nameof(HasActiveTagFilters));
        OnPropertyChanged(nameof(TagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyFilters();
    }

    public NoteCardViewModel AddNoteFromDocument(NoteDocument document)
    {
        var note = CreateNoteCard(document);
        Notes.Add(note);
        SaveNotes();
        return note;
    }

    private void AddNote()
    {
        var document = new NoteDocument
        {
            Title = LocalizationService.GetString("NewNoteTitle"),
            Content = string.Empty
        };
        AddNoteFromDocument(document);
    }

    private void DuplicateNote(NoteCardViewModel noteCard)
    {
        // Create a copy of the document with new ID and timestamps
        var duplicateDocument = new NoteDocument
        {
            Title = $"{noteCard.Document.Title} (Copy)",
            Content = noteCard.Document.Content,
            Tags = noteCard.Document.Tags?.ToList(), // Copy tags if they exist
            FontFamily = noteCard.Document.FontFamily,
            FontSize = noteCard.Document.FontSize,
            CreatedAt = DateTime.Now,
            LastModified = DateTime.Now,
            // GroupId is intentionally NOT copied - duplicate starts ungrouped
            GroupId = null
        };

        // Add the duplicated note
        AddNoteFromDocument(duplicateDocument);
    }

    private void TogglePin(NoteCardViewModel noteCard)
    {
        noteCard.Document.IsPinned = !noteCard.Document.IsPinned;
        RebuildGroups();
        ApplyFilters();
        SaveNotes();
    }

    private void DeleteNote(NoteCardViewModel noteCard)
    {
        Notes.Remove(noteCard);
        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
    }

    public bool TryGroupNotes(NoteCardViewModel draggedNote, NoteCardViewModel targetNote)
    {
        if (ReferenceEquals(draggedNote, targetNote))
            return false;

        var targetGroupId = targetNote.Document.GroupId;
        var finalGroupId = targetGroupId ?? draggedNote.Document.GroupId ?? Guid.NewGuid();

        if (draggedNote.Document.GroupId == finalGroupId && targetNote.Document.GroupId == finalGroupId)
            return false;

        draggedNote.Document.GroupId = finalGroupId;
        targetNote.Document.GroupId = finalGroupId;
        EnsureGroupMetadata(finalGroupId);

        draggedNote.NotifyGroupChanged();
        targetNote.NotifyGroupChanged();

        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
        return true;
    }

    public bool MoveGroupUp(NoteGroupViewModel group)
    {
        return TryMoveGroup(group, moveUp: true);
    }

    public bool MoveGroupDown(NoteGroupViewModel group)
    {
        return TryMoveGroup(group, moveUp: false);
    }

    public bool TryReorderNotesWithinGroup(NoteCardViewModel draggedNote, NoteCardViewModel targetNote, bool placeAfter)
    {
        if (ReferenceEquals(draggedNote, targetNote))
            return false;

        var groupId = draggedNote.Document.GroupId;
        if (!groupId.HasValue || targetNote.Document.GroupId != groupId)
            return false;

        var draggedIndex = Notes.IndexOf(draggedNote);
        var targetIndex = Notes.IndexOf(targetNote);
        if (draggedIndex < 0 || targetIndex < 0)
            return false;

        var newIndex = placeAfter ? targetIndex + 1 : targetIndex;
        if (draggedIndex < newIndex)
            newIndex--;

        if (newIndex == draggedIndex)
            return false;

        Notes.Move(draggedIndex, Math.Clamp(newIndex, 0, Notes.Count - 1));
        RebuildGroups();
        SaveNotes();
        return true;
    }

    public bool TryMoveNoteToGroup(NoteCardViewModel draggedNote, NoteGroupViewModel targetGroup)
    {
        if (draggedNote.Document.GroupId == targetGroup.GroupId)
            return false;

        draggedNote.Document.GroupId = targetGroup.GroupId;
        EnsureGroupMetadata(targetGroup.GroupId);
        draggedNote.NotifyGroupChanged();
        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
        return true;
    }

    public void RemoveFromGroup(NoteCardViewModel note)
    {
        if (!note.Document.GroupId.HasValue)
            return;

        note.Document.GroupId = null;
        note.NotifyGroupChanged();
        NormalizeGroups();
        RebuildGroups();
        _notesView.Refresh();
        SaveNotes();
    }

    public bool TryDropToUngrouped(NoteCardViewModel draggedNote)
    {
        if (!draggedNote.Document.GroupId.HasValue)
            return false;

        RemoveFromGroup(draggedNote);
        return true;
    }

    public bool RenameGroup(NoteGroupViewModel group, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var metadata = EnsureGroupMetadata(group.GroupId);
        if (string.Equals(metadata.Name, trimmed, StringComparison.Ordinal))
            return false;

        metadata.Name = trimmed;
        group.Name = trimmed;
        SaveNotes();
        return true;
    }

    public bool SetGroupBackgroundColor(NoteGroupViewModel group, string backgroundColor)
    {
        if (string.IsNullOrWhiteSpace(backgroundColor))
            return false;

        var metadata = EnsureGroupMetadata(group.GroupId);
        if (string.Equals(metadata.BackgroundColor, backgroundColor, StringComparison.OrdinalIgnoreCase))
            return false;

        metadata.BackgroundColor = backgroundColor;
        group.SetBackground(backgroundColor);
        SaveNotes();
        return true;
    }

    public void DisbandGroup(NoteGroupViewModel group, bool deleteNotes)
    {
        var notesInGroup = Notes.Where(n => n.Document.GroupId == group.GroupId).ToList();
        if (deleteNotes)
        {
            foreach (var note in notesInGroup)
                Notes.Remove(note);
        }
        else
        {
            foreach (var note in notesInGroup)
            {
                note.Document.GroupId = null;
                note.NotifyGroupChanged();
            }
        }

        _groupMetadata.Remove(group.GroupId);
        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
    }

    private bool _isSidebarExpanded = false;

    public bool IsSidebarExpanded => _isSidebarExpanded;

    public double SidebarWidth
    {
        get => _isSidebarExpanded ? 232 : 64;
    }

    public ICommand ToggleSidebarCommand { get; }

    private void ToggleSidebar()
    {
        _isSidebarExpanded = !_isSidebarExpanded;
        OnPropertyChanged(nameof(SidebarWidth));
        OnPropertyChanged(nameof(IsSidebarExpanded));
    }

    private void MoveGroupsUp()
    {
        IsGroupsFirst = true;
    }

    private void MoveGroupsDown()
    {
        IsGroupsFirst = false;
    }

    private void MoveUngroupedUp()
    {
        IsGroupsFirst = false;
    }

    private void MoveUngroupedDown()
    {
        IsGroupsFirst = true;
    }

    public ObservableCollection<NoteCardViewModel> RecentNotes { get; } = new();
    public void RefreshRecentNotes()
    {
        var recent = Notes
            .Where(MatchesSearch)
            .OrderByDescending(n => n.Document.LastModified)
            .Take(RecentNotesLimit)
            .ToList();
        RecentNotes.Clear();
        foreach (var note in recent)
            RecentNotes.Add(note);
    }

    private bool FilterUngroupedNotes(object? obj)
    {
        if (obj is not NoteCardViewModel note)
            return false;

        if (note.Document.GroupId.HasValue)
            return false;

        return MatchesSearch(note);
    }

    private bool MatchesSearch(NoteCardViewModel note)
    {
        if (_selectedTags.Count > 0)
        {
            var noteTags = note.Document.Tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!_selectedTags.All(noteTags.Contains))
                return false;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
            return true;

        var tokens = SearchQuery
            .Split([' ', ',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
            return true;

        var searchable = BuildSearchText(note);
        return tokens.All(token => searchable.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildSearchText(NoteCardViewModel note)
    {
        var groupName = GetNoteGroupName(note);
        var lastModified = note.Document.LastModified.ToString("yyyy-MM-dd HH:mm dd MMM yyyy", CultureInfo.CurrentCulture);
        var createdAt = note.Document.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm dd MMM yyyy", CultureInfo.CurrentCulture);
        var groupState = note.Document.GroupId.HasValue
            ? LocalizationService.GetString("Groups")
            : LocalizationService.GetString("UngroupedNotes");

        return string.Join(' ',
            note.Title,
            note.Content,
            note.TagsSearchText,
            groupName,
            groupState,
            note.Document.FontFamily,
            note.Document.FontSize.ToString(CultureInfo.InvariantCulture),
            lastModified,
            createdAt);
    }

    private string GetNoteGroupName(NoteCardViewModel note)
    {
        if (!note.Document.GroupId.HasValue)
            return LocalizationService.GetString("UngroupedNotes");

        if (_groupMetadata.TryGetValue(note.Document.GroupId.Value, out var metadata)
            && !string.IsNullOrWhiteSpace(metadata.Name))
        {
            return metadata.Name;
        }

        return string.Format(
            LocalizationService.GetString("GroupTitleFormat"),
            note.Document.GroupId.Value.ToString()[..4].ToUpperInvariant());
    }

    private void RefreshAvailableTags()
    {
        var tags = Notes
            .SelectMany(note => note.Document.Tags)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _selectedTags.RemoveWhere(selected => !tags.Any(tag => string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase)));

        TagFilters.Clear();
        foreach (var tag in tags)
        {
            var isSelected = _selectedTags.Contains(tag);
            TagFilters.Add(new TagFilterItemViewModel(tag, isSelected, SetTagFilterSelected));
        }

        OnPropertyChanged(nameof(HasTagFilters));
        OnPropertyChanged(nameof(HasActiveTagFilters));
        OnPropertyChanged(nameof(TagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearTagFilters()
    {
        if (_selectedTags.Count == 0)
            return;

        _selectedTags.Clear();
        foreach (var tag in TagFilters)
            tag.IsSelected = false;

        OnPropertyChanged(nameof(HasActiveTagFilters));
        OnPropertyChanged(nameof(TagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _notesView.Refresh();
        RebuildGroups();
        RefreshRecentNotes();
    }

    private void SetSortOptionSelected(string key, bool isSelected)
    {
        if (!isSelected)
            return;

        SelectedSortOptionKey = key;
    }

    private void RefreshSortOptions()
    {
        var selectedKey = NormalizeSortOptionKey(_selectedSortOptionKey);
        _selectedSortOptionKey = selectedKey;

        SortOptions.Clear();
        SortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedDesc, LocalizationService.GetString("SortByLastModifiedDesc"), selectedKey == SortLastModifiedDesc, SetSortOptionSelected));
        SortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedAsc, LocalizationService.GetString("SortByLastModifiedAsc"), selectedKey == SortLastModifiedAsc, SetSortOptionSelected));
        SortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtDesc, LocalizationService.GetString("SortByCreatedAtDesc"), selectedKey == SortCreatedAtDesc, SetSortOptionSelected));
        SortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtAsc, LocalizationService.GetString("SortByCreatedAtAsc"), selectedKey == SortCreatedAtAsc, SetSortOptionSelected));
        SortOptions.Add(new NoteSortOptionItemViewModel(SortTitleAsc, LocalizationService.GetString("SortByTitleAsc"), selectedKey == SortTitleAsc, SetSortOptionSelected));
        SortOptions.Add(new NoteSortOptionItemViewModel(SortTitleDesc, LocalizationService.GetString("SortByTitleDesc"), selectedKey == SortTitleDesc, SetSortOptionSelected));
    }

    private void UpdateSortOptionSelection()
    {
        foreach (var option in SortOptions)
            option.IsSelected = string.Equals(option.Key, _selectedSortOptionKey, StringComparison.Ordinal);
    }

    private void ApplySortToUngroupedView()
    {
        _notesView.SortDescriptions.Clear();

        // Always sort pinned notes first
        _notesView.SortDescriptions.Add(new SortDescription("Document.IsPinned", ListSortDirection.Descending));

        switch (_selectedSortOptionKey)
        {
            case SortLastModifiedAsc:
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Ascending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.Title", ListSortDirection.Ascending));
                break;
            case SortCreatedAtDesc:
                _notesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortCreatedAtAsc:
                _notesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Ascending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortTitleAsc:
                _notesView.SortDescriptions.Add(new SortDescription("Document.Title", ListSortDirection.Ascending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortTitleDesc:
                _notesView.SortDescriptions.Add(new SortDescription("Document.Title", ListSortDirection.Descending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            default:
                _notesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                _notesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                break;
        }
    }

    private List<NoteCardViewModel> SortNotes(IEnumerable<NoteCardViewModel> notes)
    {
        return _selectedSortOptionKey switch
        {
            SortLastModifiedAsc => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenBy(n => n.Document.LastModified)
                .ThenBy(n => n.Document.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            SortCreatedAtDesc => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenByDescending(n => n.Document.CreatedAt)
                .ThenByDescending(n => n.Document.LastModified)
                .ToList(),
            SortCreatedAtAsc => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenBy(n => n.Document.CreatedAt)
                .ThenByDescending(n => n.Document.LastModified)
                .ToList(),
            SortTitleAsc => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenBy(n => n.Document.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(n => n.Document.LastModified)
                .ToList(),
            SortTitleDesc => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenByDescending(n => n.Document.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(n => n.Document.LastModified)
                .ToList(),
            _ => notes
                .OrderByDescending(n => n.Document.IsPinned)
                .ThenByDescending(n => n.Document.LastModified)
                .ThenByDescending(n => n.Document.CreatedAt)
                .ToList()
        };
    }

    private static string NormalizeSortOptionKey(string? value)
    {
        if (string.Equals(value, SortLastModifiedAsc, StringComparison.OrdinalIgnoreCase))
            return SortLastModifiedAsc;
        if (string.Equals(value, SortCreatedAtDesc, StringComparison.OrdinalIgnoreCase))
            return SortCreatedAtDesc;
        if (string.Equals(value, SortCreatedAtAsc, StringComparison.OrdinalIgnoreCase))
            return SortCreatedAtAsc;
        if (string.Equals(value, SortTitleAsc, StringComparison.OrdinalIgnoreCase))
            return SortTitleAsc;
        if (string.Equals(value, SortTitleDesc, StringComparison.OrdinalIgnoreCase))
            return SortTitleDesc;

        return SortLastModifiedDesc;
    }

    private static string GetSortOptionDisplayName(string sortKey)
    {
        return NormalizeSortOptionKey(sortKey) switch
        {
            SortLastModifiedAsc => LocalizationService.GetString("SortByLastModifiedAsc"),
            SortCreatedAtDesc => LocalizationService.GetString("SortByCreatedAtDesc"),
            SortCreatedAtAsc => LocalizationService.GetString("SortByCreatedAtAsc"),
            SortTitleAsc => LocalizationService.GetString("SortByTitleAsc"),
            SortTitleDesc => LocalizationService.GetString("SortByTitleDesc"),
            _ => LocalizationService.GetString("SortByLastModifiedDesc")
        };
    }

    public void RefreshTagFiltersAfterNoteEdit()
    {
        RefreshAvailableTags();
        ApplyFilters();
    }

    private void NormalizeGroups()
    {
        var grouped = Notes
            .Where(n => n.Document.GroupId.HasValue)
            .GroupBy(n => n.Document.GroupId!.Value)
            .ToList();

        foreach (var group in grouped)
        {
            if (group.Count() >= 2)
                continue;

            foreach (var note in group)
            {
                note.Document.GroupId = null;
                note.NotifyGroupChanged();
            }

            _groupMetadata.Remove(group.Key);
        }
    }

    private void RebuildGroups()
    {
        _notesView.Refresh();

        var groupedByRecent = Notes
            .Where(n => n.Document.GroupId.HasValue)
            .GroupBy(n => n.Document.GroupId!.Value)
            .OrderByDescending(g => g.Max(n => n.Document.LastModified))
            .ToList();

        var nextOrder = 0;
        foreach (var group in groupedByRecent)
        {
            var metadata = EnsureGroupMetadata(group.Key);
            if (!metadata.SortOrder.HasValue)
                metadata.SortOrder = nextOrder;

            nextOrder = Math.Max(nextOrder, metadata.SortOrder.Value + 1);
        }

        var grouped = groupedByRecent
            .OrderBy(g => EnsureGroupMetadata(g.Key).SortOrder ?? int.MaxValue)
            .ThenByDescending(g => g.Max(n => n.Document.LastModified))
            .ToList();

        NoteGroups.Clear();

        foreach (var group in grouped)
        {
            var visibleNotes = SortNotes(group.Where(MatchesSearch));
            if (visibleNotes.Count == 0)
                continue;

            var metadata = EnsureGroupMetadata(group.Key);
            NoteGroups.Add(new NoteGroupViewModel(group.Key, metadata.Name, metadata.BackgroundColor, visibleNotes));
        }

        OnPropertyChanged(nameof(HasGroups));
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

            var store = new NotesStoreData
            {
                Notes = docs,
                Groups = _groupMetadata.Values
                    .OrderBy(g => g.SortOrder ?? int.MaxValue)
                    .ThenBy(g => g.Name)
                    .ToList()
            };

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(store, opts);
            File.WriteAllText(GetNotesFilePath(), json);
        }
        catch
        {
            // Ignore persistence errors for now
        }
    }

    private void LoadAppSettings()
    {
        _isLoadingSettings = true;
        var settings = AppSettingsService.Load();

        _enableScrollbar = settings.EnableScrollbar;
        _selectedLanguage = LocalizationService.NormalizeLanguage(settings.Language);
        _selectedTheme = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";
        _selectedSortOptionKey = NormalizeSortOptionKey(settings.NoteSortOptionKey);
        _isRecentSectionExpanded = settings.IsRecentSectionExpanded;
        _isGroupsSectionExpanded = settings.IsGroupsSectionExpanded;
        _isUngroupedSectionExpanded = settings.IsUngroupedSectionExpanded;
        _isGroupsFirst = settings.IsGroupsFirst;
        _viewMode = settings.DefaultViewMode;

        LocalizationService.SetCulture(_selectedLanguage);
        ThemeManager.SetTheme(_selectedTheme);

        _isLoadingSettings = false;
    }

    private void SaveAppSettings()
    {
        if (_isLoadingSettings)
            return;

        AppSettingsService.Save(new AppSettings
        {
            Language = _selectedLanguage,
            Theme = _selectedTheme,
            NoteSortOptionKey = _selectedSortOptionKey,
            EnableScrollbar = _enableScrollbar,
            IsRecentSectionExpanded = _isRecentSectionExpanded,
            IsGroupsSectionExpanded = _isGroupsSectionExpanded,
            IsUngroupedSectionExpanded = _isUngroupedSectionExpanded,
            IsGroupsFirst = _isGroupsFirst,
            DefaultViewMode = _viewMode,
        });
    }

    private bool LoadNotes()
    {
        try
        {
            var path = GetNotesFilePath();
            if (!File.Exists(path))
                return false;

            var json = File.ReadAllText(path);
            var docs = new List<NoteDocument>();
            var store = JsonSerializer.Deserialize<NotesStoreData>(json);
            if (store?.Notes != null && store.Notes.Count > 0)
            {
                docs = store.Notes;
                _groupMetadata.Clear();
                foreach (var metadata in store.Groups ?? new List<NoteGroupData>())
                    _groupMetadata[metadata.GroupId] = metadata;
            }
            else
            {
                var legacyDocs = JsonSerializer.Deserialize<List<NoteDocument>>(json);
                if (legacyDocs != null)
                    docs = legacyDocs;
            }

            if (docs.Count == 0)
                return false;

            Notes.Clear();
            foreach (var doc in docs)
                Notes.Add(CreateNoteCard(doc));

            RefreshAvailableTags();
            NormalizeGroups();
            RebuildGroups();
            RefreshRecentNotes();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private NoteCardViewModel CreateNoteCard(NoteDocument doc)
    {
        return new NoteCardViewModel(
            doc, 
            DeleteNote, 
            RemoveFromGroup,
            DuplicateNote,
            TogglePin
            );
    }

    private NoteGroupData EnsureGroupMetadata(Guid groupId)
    {
        if (_groupMetadata.TryGetValue(groupId, out var metadata))
        {
            metadata.SortOrder ??= GetNextGroupSortOrder();
            return metadata;
        }

        metadata = new NoteGroupData
        {
            GroupId = groupId,
            Name = string.Format(
                LocalizationService.GetString("GroupTitleFormat"),
                groupId.ToString()[..4].ToUpperInvariant()),
            BackgroundColor = DefaultGroupBackground,
            SortOrder = GetNextGroupSortOrder()
        };
        _groupMetadata[groupId] = metadata;
        return metadata;
    }

    private int GetNextGroupSortOrder()
    {
        if (_groupMetadata.Count == 0)
            return 0;

        return _groupMetadata.Values
            .Select(m => m.SortOrder ?? -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;
    }

    private bool TryMoveGroup(NoteGroupViewModel group, bool moveUp)
    {
        var currentIndex = NoteGroups.IndexOf(group);
        if (currentIndex < 0)
            return false;

        var targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
        if (targetIndex < 0 || targetIndex >= NoteGroups.Count)
            return false;

        var targetGroup = NoteGroups[targetIndex];
        var current = EnsureGroupMetadata(group.GroupId);
        var target = EnsureGroupMetadata(targetGroup.GroupId);

        (current.SortOrder, target.SortOrder) = (target.SortOrder, current.SortOrder);
        NoteGroups.Move(currentIndex, targetIndex);
        QueueSaveNotes();
        return true;
    }

    private void QueueSaveNotes()
    {
        if (_saveNotesQueued)
            return;

        _saveNotesQueued = true;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _saveNotesQueued = false;
            SaveNotes();
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            _saveNotesQueued = false;
            SaveNotes();
        }), DispatcherPriority.Background);
    }
    private string _viewMode = "Grid";

    public string ViewMode
    {
        get => _viewMode;
        set
        {
            if (SetProperty(ref _viewMode, value))
            {
                OnPropertyChanged(nameof(IsGridView));
                OnPropertyChanged(nameof(IsListView));
                SaveAppSettings();
            }
        }
    }

    public bool IsGridView => ViewMode == "Grid";
    public bool IsListView => ViewMode == "List";

}
