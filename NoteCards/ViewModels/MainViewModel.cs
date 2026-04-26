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
    private const string SortFlashcardCardsDesc = "flashcard-cards-desc";
    private const string SortFlashcardCardsAsc = "flashcard-cards-asc";
    private const string SortMindMapNodesDesc = "mindmap-nodes-desc";
    private const string SortMindMapNodesAsc = "mindmap-nodes-asc";
    private const string DashboardNotes = "Notes";
    private const string DashboardFlashcards = "Flashcards";
    private const string DashboardMindMaps = "MindMaps";

    private bool _isLoadingSettings;
    private bool _saveNotesQueued;
    private bool _enableScrollbar = true;
    private string _selectedLanguage = LocalizationService.English;
    private string _selectedTheme = "Light";
    private string _activeDashboard = DashboardNotes;
    private string _selectedSortOptionKey = SortLastModifiedDesc;
    private string _selectedFlashcardSortOptionKey = SortLastModifiedDesc;
    private string _selectedMindMapSortOptionKey = SortLastModifiedDesc;
    private readonly Dictionary<Guid, NoteGroupData> _groupMetadata = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedFlashcardTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedMindMapTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _massSelectedNoteIds = new();
    private bool _isMassSelectMode;
    private DateTime _calendarSelectedDate = DateTime.Today;
    private AppSettings _settings;

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
    private string GetFlashcardsFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "flashcards.json");
    }

    private string GetFlashcardSetsFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "flashcard-sets.json");
    }

    private string GetMindMapsFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "mind-maps.json");
    }
    
   

    private bool _isCalendarFirst = true;
    public bool IsCalendarFirst
    {
        get => _isCalendarFirst;
        set
        {
            if (SetProperty(ref _isCalendarFirst, value))
            {
                CommandManager.InvalidateRequerySuggested();
                SaveAppSettings();
            }
        }
    }

    private bool _isCalendarSectionVisible = true;
    public bool IsCalendarSectionVisible
    {
        get => _isCalendarSectionVisible;
        set
        {
            if (SetProperty(ref _isCalendarSectionVisible, value))
                SaveAppSettings();
        }
    }

    private bool _isRecentSectionVisible = true;
    public bool IsRecentSectionVisible
    {
        get => _isRecentSectionVisible;
        set
        {
            if (SetProperty(ref _isRecentSectionVisible, value))
                SaveAppSettings();
        }
    }

    private bool _isCalendarSectionExpanded = true;
    public bool IsCalendarSectionExpanded
    {
        get => _isCalendarSectionExpanded;
        set
        {
            if (SetProperty(ref _isCalendarSectionExpanded, value))
                SaveAppSettings();
        }
    }

    private bool _isGroupsSectionVisible = true;
    public bool IsGroupsSectionVisible
    {
        get => _isGroupsSectionVisible;
        set
        {
            if (SetProperty(ref _isGroupsSectionVisible, value))
                SaveAppSettings();
        }
    }

    private bool _isUngroupedSectionVisible = true;
    private bool _isFlashcardGroupsSectionVisible = true;
    private bool _isFlashcardGroupsSectionExpanded = true;
    private bool _isMindMapGroupsSectionVisible = true;
    private bool _isMindMapGroupsSectionExpanded = true;
    public bool IsUngroupedSectionVisible
    {
        get => _isUngroupedSectionVisible;
        set
        {
            if (SetProperty(ref _isUngroupedSectionVisible, value))
                SaveAppSettings();
        }
    }

    public bool IsFlashcardGroupsSectionVisible
    {
        get => _isFlashcardGroupsSectionVisible;
        set
        {
            if (SetProperty(ref _isFlashcardGroupsSectionVisible, value))
            {
                ApplyFlashcardFilters();
                SaveAppSettings();
            }
        }
    }

    public bool IsMindMapGroupsSectionVisible
    {
        get => _isMindMapGroupsSectionVisible;
        set
        {
            if (SetProperty(ref _isMindMapGroupsSectionVisible, value))
            {
                ApplyMindMapFilters();
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
                RefreshFlashcardSortOptions();
                RefreshMindMapSortOptions();
                OnPropertyChanged(nameof(SortButtonText));
                OnPropertyChanged(nameof(ActiveSortButtonText));
                OnPropertyChanged(nameof(UserActivitySummaryTitle));
                OnPropertyChanged(nameof(CalendarSelectedDateDisplay));
                RefreshActivityStats();
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
    public object CurrentView { get; set; }
    public string ActiveDashboard => _activeDashboard;
    public bool IsNotesView => string.Equals(_activeDashboard, DashboardNotes, StringComparison.Ordinal);
    public bool IsFlashcardsView
    {
        get => string.Equals(_activeDashboard, DashboardFlashcards, StringComparison.Ordinal);
        set => SetActiveDashboard(value ? DashboardFlashcards : DashboardNotes);
    }
    public bool IsMindMapsView => string.Equals(_activeDashboard, DashboardMindMaps, StringComparison.Ordinal);
    public FlashcardItem SelectedFlashcard { get; set; }

    public bool IsShowingAnswer { get; set; }

    private string _newFlashcardAnswer = "";
    private string _newFlashcardQuestion = "";
    private void NextFlashcard()
    {
        if (SelectedFlashcard == null || Flashcards.Count == 0)
            return;

        var index = Flashcards.IndexOf(SelectedFlashcard);
        if (index < Flashcards.Count - 1)
        {
            SelectedFlashcard = Flashcards[index + 1];
        }
        else
        {
            SelectedFlashcard = Flashcards[0]; // wrap (optional)
        }

        IsShowingAnswer = false;

        OnPropertyChanged(nameof(SelectedFlashcard));
        OnPropertyChanged(nameof(IsShowingAnswer));
    }

    private void PreviousFlashcard()
    {
        if (SelectedFlashcard == null || Flashcards.Count == 0)
            return;

        var index = Flashcards.IndexOf(SelectedFlashcard);
        if (index > 0)
        {
            SelectedFlashcard = Flashcards[index - 1];
        }
        else
        {
            SelectedFlashcard = Flashcards[^1]; // wrap
        }

        IsShowingAnswer = false;

        OnPropertyChanged(nameof(SelectedFlashcard));
        OnPropertyChanged(nameof(IsShowingAnswer));
    }

    private bool CanNavigateFlashcards()
    {
        return SelectedFlashcard != null && Flashcards.Count > 1;
    }

    private void SetActiveDashboard(string? dashboard)
    {
        var normalized = NormalizeDashboard(dashboard);
        if (string.Equals(_activeDashboard, normalized, StringComparison.Ordinal))
            return;

        _activeDashboard = normalized;
        CurrentView = normalized;
        OnPropertyChanged(nameof(ActiveDashboard));
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(IsNotesView));
        OnPropertyChanged(nameof(IsFlashcardsView));
        OnPropertyChanged(nameof(IsMindMapsView));
        NotifyActiveDashboardChromeChanged();
        SaveAppSettings();
    }


    public MainViewModel()
    {
        LoadAppSettings();


        Notes = new ObservableCollection<NoteCardViewModel>();
        NoteGroups = new ObservableCollection<NoteGroupViewModel>();
        TagFilters = new ObservableCollection<TagFilterItemViewModel>();
        FlashcardTagFilters = new ObservableCollection<TagFilterItemViewModel>();
        MindMapTagFilters = new ObservableCollection<TagFilterItemViewModel>();
        SortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        FlashcardSortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        MindMapSortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        CalendarScheduledNotes = new ObservableCollection<CalendarScheduledItemViewModel>();
        // Create a view for Notes so we can apply filtering for search
        _notesView = CollectionViewSource.GetDefaultView(Notes);
        _notesView.Filter = FilterUngroupedNotes;
        ApplySortToUngroupedView();
        _flashcardSetsView = CollectionViewSource.GetDefaultView(FlashcardSets);
        _flashcardSetsView.Filter = FilterFlashcardSet;
        ApplySortToFlashcardSetsView();
        _mindMapsView = CollectionViewSource.GetDefaultView(MindMaps);
        _mindMapsView.Filter = FilterMindMap;
        ApplySortToMindMapsView();
        Notes.CollectionChanged += (_, _) =>
        {
            RefreshAvailableTags();
            ApplyFilters();
            RefreshActivityStats();
            EnsureMassSelectionConsistency();
        };
        FlashcardSets.CollectionChanged += (_, _) =>
        {
            RefreshAvailableFlashcardTags();
            ApplyFlashcardFilters();
            NotifyFlashcardSetsChanged();
        };
        MindMaps.CollectionChanged += (_, _) =>
        {
            RefreshAvailableMindMapTags();
            ApplyMindMapFilters();
            NotifyMindMapsChanged();
        };
        
        NoteCards.Services.ActivityTracker.ActivityUpdated += RefreshActivityStats;
        
        RefreshSortOptions();
        RefreshFlashcardSortOptions();
        RefreshMindMapSortOptions();
        RefreshAvailableTags();
        RefreshAvailableFlashcardTags();
        RefreshAvailableMindMapTags();
        RefreshRecentNotes();
        RefreshCalendarScheduledNotes();
        LoadFlashcards();
        LoadMindMaps();
        AddNoteCommand = new RelayCommand(AddNote);
        ToggleSidebarCommand = new RelayCommand(ToggleSidebar);
        ClearTagFiltersCommand = new RelayCommand(ClearActiveTagFilters, () => ActiveHasActiveTagFilters);
        ExitMassSelectCommand = new RelayCommand(ExitMassSelect, () => IsMassSelectMode);
        SelectAllVisibleNotesCommand = new RelayCommand(SelectAllVisibleNotes, () => IsMassSelectMode);
        DeleteSelectedNotesCommand = new RelayCommand(DeleteSelectedNotes, () => IsMassSelectMode && SelectedNotesCount > 0);
        RemoveSelectedFromGroupsCommand = new RelayCommand(RemoveSelectedFromGroups, CanUngroupSelectedNotes);
        GroupSelectedNotesCommand = new RelayCommand(GroupSelectedNotes, CanGroupSelectedNotes);
        PinSelectedNotesCommand = new RelayCommand(PinSelectedNotes, CanPinSelectedNotes);
        UnpinSelectedNotesCommand = new RelayCommand(UnpinSelectedNotes, CanUnpinSelectedNotes);
        DuplicateSelectedNotesCommand = new RelayCommand(DuplicateSelectedNotes, CanDuplicateSelectedNotes);
        PreviousFlashcardCommand = new RelayCommand(PreviousFlashcard, CanNavigateFlashcards);
        NextFlashcardCommand = new RelayCommand(NextFlashcard, CanNavigateFlashcards);


        ShowFlashcardsCommand = new RelayCommand(() => { });
        OpenFlashcardCommand = new RelayCommand<FlashcardItem>(f =>
        {
            SelectedFlashcard = f;
            IsShowingAnswer = false;

            OnPropertyChanged(nameof(SelectedFlashcard));
            OnPropertyChanged(nameof(IsShowingAnswer));
        });
        CloseFlashcardCommand = new RelayCommand(() =>
        {
            SelectedFlashcard = null;
            OnPropertyChanged(nameof(SelectedFlashcard));
        });

        FlipFlashcardCommand = new RelayCommand(() =>
        {
            IsShowingAnswer = !IsShowingAnswer;
            OnPropertyChanged(nameof(IsShowingAnswer));
        });


        CurrentView = _activeDashboard;

        ShowNotesCommand = new RelayCommand(() =>
        {
            SetActiveDashboard(DashboardNotes);
        });

        ShowFlashcardsCommand = new RelayCommand(() =>
        {
            SetActiveDashboard(DashboardFlashcards);
        });
        ShowMindMapsCommand = new RelayCommand(() =>
        {
            SetActiveDashboard(DashboardMindMaps);
        });
        // 🔧 FIX – inicializuojam visus command, kad ViewModel nelūžtų
        MoveGroupsUpCommand = new RelayCommand(() => { });
        MoveGroupsDownCommand = new RelayCommand(() => { });
        MoveUngroupedUpCommand = new RelayCommand(() => { });
        MoveUngroupedDownCommand = new RelayCommand(() => { });
        ToggleGroupsSectionCommand = new RelayCommand(() => { });
        ToggleRecentSectionCommand = new RelayCommand(() => { });
        ToggleUngroupedSectionCommand = new RelayCommand(() => { });


        AddFlashcardCommand = new RelayCommand(AddFlashcard);


        _settings = AppSettingsService.Load();

        SetActiveDashboard(_settings.LastView);
        

        // Try to load saved notes from disk. If none exist, create a starter note.
        if (!LoadNotes())
        {
            var starterDocument = new NoteDocument
            {
                Title = LocalizationService.GetString("FirstNoteTitle"),
                Content = LocalizationService.GetString("FirstNoteContent")
            };
            Notes.Add(CreateNoteCard(starterDocument));
            SaveNotes();
        }

        RebuildGroups();
    }
    private void AddFlashcard()
    {
        Flashcards.Add(new FlashcardItem
        {
            Question = NewFlashcardQuestion,
            Answer = NewFlashcardAnswer
        });

        NewFlashcardQuestion = "";
        NewFlashcardAnswer = "";

        SaveFlashcards(); // 🔥 svarbiausia
    }

    private void LoadFlashcards()
    {
        FlashcardSets.Clear();

        var setsPath = GetFlashcardSetsFilePath();
        if (File.Exists(setsPath))
        {
            var json = File.ReadAllText(setsPath);
            var sets = JsonSerializer.Deserialize<List<FlashcardSetDocument>>(json) ?? new();

            foreach (var set in sets
                         .Where(set => set != null)
                         .OrderByDescending(set => set.LastModified)
                         .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                NormalizeFlashcardSetDocument(set);
                FlashcardSets.Add(new FlashcardSetViewModel(set));
            }

            NotifyFlashcardSetsChanged();
            return;
        }

        var legacyPath = GetFlashcardsFilePath();
        if (File.Exists(legacyPath))
        {
            var json = File.ReadAllText(legacyPath);
            var data = JsonSerializer.Deserialize<List<FlashcardItem>>(json) ?? new();
            Flashcards = new ObservableCollection<FlashcardItem>(data);

            if (data.Count > 0)
            {
                AddOrUpdateFlashcardSet(new FlashcardSetDocument
                {
                    Title = LocalizationService.GetString("FlashcardsPreviewTitle"),
                    Cards = data.ToList(),
                    Tags = new List<string>(),
                    CreatedAt = DateTime.UtcNow,
                    LastModified = DateTime.Now
                });
            }
        }
        else
        {
            Flashcards = new ObservableCollection<FlashcardItem>();
        }

        NotifyFlashcardSetsChanged();
    }
    private void SaveFlashcards()
    {
        var path = GetFlashcardsFilePath();
        var json = JsonSerializer.Serialize(Flashcards);
        File.WriteAllText(path, json);
    }

    public FlashcardSetViewModel AddOrUpdateFlashcardSet(FlashcardSetDocument document)
    {
        NormalizeFlashcardSetDocument(document);

        var existing = FlashcardSets.FirstOrDefault(set => set.Document.Id == document.Id);
        if (existing is null)
        {
            existing = new FlashcardSetViewModel(document);
            FlashcardSets.Add(existing);
        }
        else
        {
            existing.Document.Title = document.Title;
            existing.Document.Tags = document.Tags;
            existing.Document.SetNames = document.SetNames;
            existing.Document.Cards = document.Cards;
            existing.Document.CreatedAt = document.CreatedAt;
            existing.Document.LastModified = document.LastModified;
            existing.Document.AiModelDisplayName = document.AiModelDisplayName;
            existing.NotifyChanged();
        }

        ReorderFlashcardSets();
        RefreshAvailableFlashcardTags();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
        NotifyFlashcardSetsChanged();
        return existing;
    }

    public void SaveFlashcardSets()
    {
        var path = GetFlashcardSetsFilePath();
        var documents = FlashcardSets
            .Select(set => set.Document)
            .OrderByDescending(set => set.LastModified)
            .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(documents, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void ReorderFlashcardSets()
    {
        var ordered = FlashcardSets
            .OrderByDescending(set => set.Document.LastModified)
            .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FlashcardSets.Clear();
        foreach (var set in ordered)
            FlashcardSets.Add(set);
    }

    private void NotifyFlashcardSetsChanged()
    {
        OnPropertyChanged(nameof(HasFlashcardSets));
        OnPropertyChanged(nameof(FlashcardSetCount));
        OnPropertyChanged(nameof(FlashcardSetCountText));
    }

    private static void NormalizeFlashcardSetDocument(FlashcardSetDocument document)
    {
        document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
        document.Title = string.IsNullOrWhiteSpace(document.Title)
            ? LocalizationService.GetString("FlashcardSetUntitled")
            : document.Title.Trim();
        document.Tags = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        document.SetNames = document.SetNames?
            .Where(pair => pair.Key >= 1 && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim()) ?? new Dictionary<int, string>();
        document.Cards = document.Cards?
            .Where(card => card != null)
            .Select(card => new FlashcardItem
            {
                Question = card.Question?.Trim() ?? string.Empty,
                Answer = card.Answer?.Trim() ?? string.Empty,
                Category = card.Category?.Trim() ?? string.Empty,
                SetIndex = Math.Max(1, card.SetIndex)
            })
            .ToList() ?? new List<FlashcardItem>();
        document.CreatedAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        document.LastModified = document.LastModified == default ? DateTime.Now : document.LastModified;
        document.AiModelDisplayName = document.AiModelDisplayName?.Trim() ?? string.Empty;
    }

    private void LoadMindMaps()
    {
        MindMaps.Clear();

        var path = GetMindMapsFilePath();
        if (!File.Exists(path))
        {
            NotifyMindMapsChanged();
            return;
        }

        var json = File.ReadAllText(path);
        var maps = JsonSerializer.Deserialize<List<MindMapDocument>>(json) ?? new();

        foreach (var map in maps
                     .Where(map => map != null)
                     .OrderByDescending(map => map.LastModified)
                     .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            NormalizeMindMapDocument(map);
            MindMaps.Add(new MindMapViewModel(map));
        }

        NotifyMindMapsChanged();
    }

    public MindMapViewModel AddOrUpdateMindMap(MindMapDocument document)
    {
        NormalizeMindMapDocument(document);

        var existing = MindMaps.FirstOrDefault(map => map.Document.Id == document.Id);
        if (existing is null)
        {
            existing = new MindMapViewModel(document);
            MindMaps.Add(existing);
        }
        else
        {
            existing.Document.Title = document.Title;
            existing.Document.Tags = document.Tags;
            existing.Document.Root = document.Root;
            existing.Document.CreatedAt = document.CreatedAt;
            existing.Document.LastModified = document.LastModified;
            existing.Document.AiModelDisplayName = document.AiModelDisplayName;
            existing.Document.SourceNoteId = document.SourceNoteId;
            existing.NotifyChanged();
        }

        ReorderMindMaps();
        RefreshAvailableMindMapTags();
        ApplyMindMapFilters();
        SaveMindMaps();
        NotifyMindMapsChanged();
        return existing;
    }

    public void SaveMindMaps()
    {
        var path = GetMindMapsFilePath();
        var documents = MindMaps
            .Select(map => map.Document)
            .OrderByDescending(map => map.LastModified)
            .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var json = JsonSerializer.Serialize(documents, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void ReorderMindMaps()
    {
        var ordered = MindMaps
            .OrderByDescending(map => map.Document.LastModified)
            .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        MindMaps.Clear();
        foreach (var map in ordered)
            MindMaps.Add(map);
    }

    public void DeleteMindMap(MindMapViewModel mindMapVm)
    {
        if (mindMapVm is null)
            return;

        MindMaps.Remove(mindMapVm);
        SaveMindMaps();

        OnPropertyChanged(nameof(HasMindMaps));
        OnPropertyChanged(nameof(MindMapsView));
    }

    private void NotifyMindMapsChanged()
    {
        OnPropertyChanged(nameof(HasMindMaps));
        OnPropertyChanged(nameof(MindMapCount));
        OnPropertyChanged(nameof(MindMapCountText));
    }

    private static void NormalizeMindMapDocument(MindMapDocument document)
    {
        document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
        document.Title = string.IsNullOrWhiteSpace(document.Title)
            ? LocalizationService.GetString("MindMapUntitled")
            : document.Title.Trim();
        document.Tags = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        document.Root ??= new MindMapNode();
        NormalizeMindMapNode(document.Root);
        if (string.IsNullOrWhiteSpace(document.Root.Text))
            document.Root.Text = document.Title;
        document.CreatedAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        document.LastModified = document.LastModified == default ? DateTime.Now : document.LastModified;
        document.AiModelDisplayName = document.AiModelDisplayName?.Trim() ?? string.Empty;
    }

    private static void NormalizeMindMapNode(MindMapNode node)
    {
        node.Text = node.Text?.Trim() ?? string.Empty;
        node.Children = node.Children?
            .Where(child => child != null)
            .ToList() ?? new List<MindMapNode>();

        foreach (var child in node.Children)
            NormalizeMindMapNode(child);
    }


    public ObservableCollection<NoteCardViewModel> Notes { get; }
    public ObservableCollection<NoteGroupViewModel> NoteGroups { get; }
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; }
    public ObservableCollection<TagFilterItemViewModel> FlashcardTagFilters { get; }
    public ObservableCollection<TagFilterItemViewModel> MindMapTagFilters { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> SortOptions { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> FlashcardSortOptions { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> MindMapSortOptions { get; }
    public IEnumerable<NoteSortOptionItemViewModel> ActiveSortOptions => IsMindMapsView
        ? MindMapSortOptions
        : IsFlashcardsView ? FlashcardSortOptions : SortOptions;
    public IEnumerable<TagFilterItemViewModel> ActiveTagFilters => IsMindMapsView
        ? MindMapTagFilters
        : IsFlashcardsView ? FlashcardTagFilters : TagFilters;
    public ObservableCollection<CalendarScheduledItemViewModel> CalendarScheduledNotes { get; }
     public bool HasGroups => NoteGroups.Count > 0;
    public bool HasTagFilters => TagFilters.Count > 0;
    public bool HasFlashcardTagFilters => FlashcardTagFilters.Count > 0;
    public bool HasMindMapTagFilters => MindMapTagFilters.Count > 0;
    public bool ActiveHasTagFilters => IsMindMapsView
        ? HasMindMapTagFilters
        : IsFlashcardsView ? HasFlashcardTagFilters : HasTagFilters;
    public bool HasActiveTagFilters => _selectedTags.Count > 0;
    public bool HasActiveFlashcardTagFilters => _selectedFlashcardTags.Count > 0;
    public bool HasActiveMindMapTagFilters => _selectedMindMapTags.Count > 0;
    public bool ActiveHasActiveTagFilters => IsMindMapsView
        ? HasActiveMindMapTagFilters
        : IsFlashcardsView ? HasActiveFlashcardTagFilters : HasActiveTagFilters;
    public bool IsMassSelectMode
    {
        get => _isMassSelectMode;
        private set
        {
            if (!SetProperty(ref _isMassSelectMode, value))
                return;

            OnPropertyChanged(nameof(IsNotMassSelectMode));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsNotMassSelectMode => !IsMassSelectMode;
    public int SelectedNotesCount => _massSelectedNoteIds.Count;
    public string MassSelectSelectionText => string.Format(LocalizationService.GetString("MassSelectSelectedCount"), SelectedNotesCount);
    public string TagFilterButtonText => HasActiveTagFilters
        ? $"{LocalizationService.GetString("FilterTags")} ({_selectedTags.Count})"
        : LocalizationService.GetString("FilterTags");
    public string ActiveTagFilterButtonText
    {
        get
        {
            if (IsMindMapsView)
            {
                return HasActiveMindMapTagFilters
                    ? $"{LocalizationService.GetString("FilterTags")} ({_selectedMindMapTags.Count})"
                    : LocalizationService.GetString("FilterTags");
            }

            if (IsFlashcardsView)
            {
                return HasActiveFlashcardTagFilters
                ? $"{LocalizationService.GetString("FilterTags")} ({_selectedFlashcardTags.Count})"
                : LocalizationService.GetString("FilterTags");
            }

            return TagFilterButtonText;
        }
    }
    public string SortButtonText => string.Format(
        LocalizationService.GetString("SortButtonFormat"),
        GetSortOptionDisplayName(_selectedSortOptionKey));
    public string ActiveSortButtonText => IsMindMapsView
        ? string.Format(LocalizationService.GetString("SortButtonFormat"), GetMindMapSortOptionDisplayName(_selectedMindMapSortOptionKey))
        : IsFlashcardsView
            ? string.Format(LocalizationService.GetString("SortButtonFormat"), GetFlashcardSortOptionDisplayName(_selectedFlashcardSortOptionKey))
            : SortButtonText;
    public bool HasCalendarScheduledNotes => CalendarScheduledNotes.Count > 0;

    public DateTime CalendarSelectedDate
    {
        get => _calendarSelectedDate;
        set
        {
            var normalized = value.Date;
            if (!SetProperty(ref _calendarSelectedDate, normalized))
                return;

            RefreshCalendarScheduledNotes();
            OnPropertyChanged(nameof(CalendarSelectedDateDisplay));
        }
    }

    public string CalendarSelectedDateDisplay => CalendarSelectedDate.ToString("dddd, dd MMM yyyy", CultureInfo.CurrentCulture);

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
            OnPropertyChanged(nameof(ActiveSortButtonText));
            ApplyFilters();
            SaveAppSettings();
        }
    }

    private readonly ICollectionView _notesView;
    public ICollectionView NotesView => _notesView;
    private readonly ICollectionView _flashcardSetsView;
    public ICollectionView FlashcardSetsView => _flashcardSetsView;
    private readonly ICollectionView _mindMapsView;
    public ICollectionView MindMapsView => _mindMapsView;

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

    public bool IsFlashcardGroupsSectionExpanded
    {
        get => _isFlashcardGroupsSectionExpanded;
        set
        {
            if (SetProperty(ref _isFlashcardGroupsSectionExpanded, value))
                SaveAppSettings();
        }
    }

    public bool IsMindMapGroupsSectionExpanded
    {
        get => _isMindMapGroupsSectionExpanded;
        set
        {
            if (SetProperty(ref _isMindMapGroupsSectionExpanded, value))
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
    public string UserActivitySummaryTitle => LocalizationService.GetString("UserActivitySummaryTitle");
    public string StatsTotalNotes => string.Format(LocalizationService.GetString("StatsTotalNotesFormat"), Notes.Count);
    public string StatsWordsTyped => string.Format(LocalizationService.GetString("StatsWordsTypedFormat"), AppSettingsService.Load().TotalWordsTyped);
    public string StatsCharactersTyped => string.Format(LocalizationService.GetString("StatsCharactersTypedFormat"), AppSettingsService.Load().TotalCharactersTyped);
    public string StatsTimeSpent => string.Format(LocalizationService.GetString("StatsTimeSpentFormat"), GetTotalTimeSpent());
    public string StatsLastActive => string.Format(
        LocalizationService.GetString("StatsLastActiveFormat"),
        AppSettingsService.Load().LastActiveDate?.ToString("yyyy-MM-dd HH:mm") ?? LocalizationService.GetString("NotAvailable"));

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
    private string _flashcardSearchQuery = string.Empty;
    private string _mindMapSearchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery != value)
            {
                _searchQuery = value ?? string.Empty;
                OnPropertyChanged(nameof(SearchQuery));
                OnPropertyChanged(nameof(ActiveSearchQuery));
                ApplyFilters();
            }
        }
    }

    public string FlashcardSearchQuery
    {
        get => _flashcardSearchQuery;
        set
        {
            if (_flashcardSearchQuery == (value ?? string.Empty))
                return;

            _flashcardSearchQuery = value ?? string.Empty;
            OnPropertyChanged(nameof(FlashcardSearchQuery));
            OnPropertyChanged(nameof(ActiveSearchQuery));
            ApplyFlashcardFilters();
        }
    }

    public string MindMapSearchQuery
    {
        get => _mindMapSearchQuery;
        set
        {
            if (_mindMapSearchQuery == (value ?? string.Empty))
                return;

            _mindMapSearchQuery = value ?? string.Empty;
            OnPropertyChanged(nameof(MindMapSearchQuery));
            OnPropertyChanged(nameof(ActiveSearchQuery));
            ApplyMindMapFilters();
        }
    }

    public string ActiveSearchQuery
    {
        get => IsMindMapsView
            ? MindMapSearchQuery
            : IsFlashcardsView ? FlashcardSearchQuery : SearchQuery;
        set
        {
            if (IsMindMapsView)
                MindMapSearchQuery = value;
            else if (IsFlashcardsView)
                FlashcardSearchQuery = value;
            else
                SearchQuery = value;

            OnPropertyChanged(nameof(ActiveSearchQuery));
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
    public ICommand ExitMassSelectCommand { get; }
    public ICommand SelectAllVisibleNotesCommand { get; }
    public ICommand DeleteSelectedNotesCommand { get; }
    public ICommand RemoveSelectedFromGroupsCommand { get; }
    public ICommand GroupSelectedNotesCommand { get; }
    public ICommand PinSelectedNotesCommand { get; }
    public ICommand UnpinSelectedNotesCommand { get; }
    public ICommand DuplicateSelectedNotesCommand { get; }
    public ICommand AddFlashcardCommand { get; }
    public ICommand ShowFlashcardsCommand { get; }
    public ICommand OpenFlashcardCommand { get; }
    public ICommand FlipFlashcardCommand { get; }
    public ICommand CloseFlashcardCommand { get; }
    public ICommand ShowNotesCommand { get; }
    public ICommand ShowMindMapsCommand { get; }
    public ICommand PreviousFlashcardCommand { get; }
    public ICommand NextFlashcardCommand { get; }

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
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        CommandManager.InvalidateRequerySuggested();
        ApplyFilters();
    }

    public NoteCardViewModel AddNoteFromDocument(NoteDocument document)
    {
        EnsureDocumentTypography(document);
        var note = CreateNoteCard(document);
        Notes.Add(note);
        SaveNotes();
        return note;
    }

    private void AddNote()
    {
        var (fontFamily, fontSize) = GetPreferredTypography();

        var document = new NoteDocument
        {
            Title = LocalizationService.GetString("NewNoteTitle"),
            Content = string.Empty,
            FontFamily = fontFamily,
            FontSize = fontSize
        };
        AddNoteFromDocument(document);
    }

    private static void EnsureDocumentTypography(NoteDocument document)
    {
        var (fontFamily, fontSize) = GetPreferredTypography();

        if (string.IsNullOrWhiteSpace(document.FontFamily))
            document.FontFamily = fontFamily;

        if (document.FontSize <= 0)
            document.FontSize = fontSize;
    }

    private static (string FontFamily, double FontSize) GetPreferredTypography()
    {
        var settings = AppSettingsService.Load();

        var fontFamily = string.IsNullOrWhiteSpace(settings.PreferredFontFamily)
            ? "Segoe UI"
            : settings.PreferredFontFamily;

        var fontSize = settings.PreferredFontSize > 0
            ? settings.PreferredFontSize
            : 14;

        return (fontFamily, fontSize);
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

    public void EnterMassSelect(NoteCardViewModel initialNote)
    {
        IsMassSelectMode = true;
        SetNoteSelectedState(initialNote, true);
    }

    public void ToggleMassSelectForNote(NoteCardViewModel note)
    {
        if (!IsMassSelectMode)
            return;

        SetNoteSelectedState(note, !_massSelectedNoteIds.Contains(note.Document.Id));
    }

    public void ExitMassSelect()
    {
        if (!IsMassSelectMode)
            return;

        _massSelectedNoteIds.Clear();
        foreach (var note in Notes)
            note.IsSelectedInMassSelect = false;

        IsMassSelectMode = false;
        NotifyMassSelectionChanged();
    }

    public int AddTagsToSelected(string tagsInput)
    {
        if (!IsMassSelectMode || string.IsNullOrWhiteSpace(tagsInput))
            return 0;

        var tags = tagsInput
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
            return 0;

        var affected = 0;
        foreach (var note in GetMassSelectedNotes())
        {
            var changed = false;
            foreach (var tag in tags)
            {
                if (note.Document.Tags.Any(existing => string.Equals(existing, tag, StringComparison.OrdinalIgnoreCase)))
                    continue;

                note.Document.Tags.Add(tag);
                changed = true;
            }

            if (!changed)
                continue;

            affected++;
            note.Document.LastModified = DateTime.Now;
            note.NotifyContentChanged();
        }

        if (affected == 0)
            return 0;

        RefreshAvailableTags();
        ApplyFilters();
        SaveNotes();
        return affected;
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

    public string SelectedFlashcardSortOptionKey
    {
        get => _selectedFlashcardSortOptionKey;
        set
        {
            var normalized = NormalizeFlashcardSortOptionKey(value);
            if (!SetProperty(ref _selectedFlashcardSortOptionKey, normalized))
                return;

            UpdateFlashcardSortOptionSelection();
            ApplySortToFlashcardSetsView();
            OnPropertyChanged(nameof(ActiveSortButtonText));
            SaveAppSettings();
        }
    }

    public string SelectedMindMapSortOptionKey
    {
        get => _selectedMindMapSortOptionKey;
        set
        {
            var normalized = NormalizeMindMapSortOptionKey(value);
            if (!SetProperty(ref _selectedMindMapSortOptionKey, normalized))
                return;

            UpdateMindMapSortOptionSelection();
            ApplySortToMindMapsView();
            OnPropertyChanged(nameof(ActiveSortButtonText));
            SaveAppSettings();
        }
    }

    public ObservableCollection<FlashcardSetViewModel> FlashcardSets { get; } = new();
    public bool HasFlashcardSets => FlashcardSets.Count > 0;
    public int FlashcardSetCount => FlashcardSets.Count;
    public string FlashcardSetCountText => string.Format(LocalizationService.GetString("FlashcardSetCountFormat"), FlashcardSetCount);
    public ObservableCollection<MindMapViewModel> MindMaps { get; } = new();
    public bool HasMindMaps => MindMaps.Count > 0;
    public int MindMapCount => MindMaps.Count;
    public string MindMapCountText => string.Format(LocalizationService.GetString("MindMapCountFormat"), MindMapCount);
    public ObservableCollection<FlashcardItem> Flashcards { get; set; } = new();

    
    public string NewFlashcardQuestion
    {
        get => _newFlashcardQuestion;
        set
        {
            _newFlashcardQuestion = value;
            OnPropertyChanged();
        }
    }

   
    public string NewFlashcardAnswer
    {
        get => _newFlashcardAnswer;
        set
        {
            _newFlashcardAnswer = value;
            OnPropertyChanged();
        }
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
        var scheduleSearchText = BuildScheduleSearchText(note.Document);
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
            createdAt,
            scheduleSearchText);
    }

    private static string BuildScheduleSearchText(NoteDocument document)
    {
        var schedules = GetEffectiveSchedules(document);
        if (schedules.Count == 0)
            return string.Empty;

        return string.Join(' ', schedules.Select(entry =>
            $"{entry.ScheduledAt:yyyy-MM-dd HH:mm dd MMM yyyy} {entry.Note}"));
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
        OnPropertyChanged(nameof(ActiveHasTagFilters));
        OnPropertyChanged(nameof(HasActiveTagFilters));
        OnPropertyChanged(nameof(TagFilterButtonText));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearActiveTagFilters()
    {
        if (IsMindMapsView)
            ClearMindMapTagFilters();
        else if (IsFlashcardsView)
            ClearFlashcardTagFilters();
        else
            ClearTagFilters();
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
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyFilters();
    }

    public void SetFlashcardTagFilterSelected(string tag, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        if (isSelected)
            _selectedFlashcardTags.Add(tag);
        else
            _selectedFlashcardTags.Remove(tag);

        OnPropertyChanged(nameof(HasActiveFlashcardTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyFlashcardFilters();
    }

    public void SetMindMapTagFilterSelected(string tag, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        if (isSelected)
            _selectedMindMapTags.Add(tag);
        else
            _selectedMindMapTags.Remove(tag);

        OnPropertyChanged(nameof(HasActiveMindMapTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyMindMapFilters();
    }

    private void NotifyActiveDashboardChromeChanged()
    {
        OnPropertyChanged(nameof(ActiveSearchQuery));
        OnPropertyChanged(nameof(ActiveSortButtonText));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        OnPropertyChanged(nameof(ActiveSortOptions));
        OnPropertyChanged(nameof(ActiveTagFilters));
        OnPropertyChanged(nameof(ActiveHasTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearFlashcardTagFilters()
    {
        if (_selectedFlashcardTags.Count == 0)
            return;

        _selectedFlashcardTags.Clear();
        foreach (var tag in FlashcardTagFilters)
            tag.IsSelected = false;

        OnPropertyChanged(nameof(HasActiveFlashcardTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyFlashcardFilters();
    }

    private void ClearMindMapTagFilters()
    {
        if (_selectedMindMapTags.Count == 0)
            return;

        _selectedMindMapTags.Clear();
        foreach (var tag in MindMapTagFilters)
            tag.IsSelected = false;

        OnPropertyChanged(nameof(HasActiveMindMapTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyMindMapFilters();
    }

    private void ApplyFilters()
    {
        _notesView.Refresh();
        RebuildGroups();
        RefreshRecentNotes();
        RefreshCalendarScheduledNotes();
    }

    private void RefreshAvailableFlashcardTags()
    {
        var tags = FlashcardSets
            .SelectMany(set => set.Document.Tags ?? new List<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _selectedFlashcardTags.RemoveWhere(selected => !tags.Any(tag => string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase)));

        FlashcardTagFilters.Clear();
        foreach (var tag in tags)
        {
            var isSelected = _selectedFlashcardTags.Contains(tag);
            FlashcardTagFilters.Add(new TagFilterItemViewModel(tag, isSelected, SetFlashcardTagFilterSelected));
        }

        OnPropertyChanged(nameof(HasFlashcardTagFilters));
        OnPropertyChanged(nameof(ActiveHasTagFilters));
        OnPropertyChanged(nameof(HasActiveFlashcardTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshAvailableMindMapTags()
    {
        var tags = MindMaps
            .SelectMany(map => map.Document.Tags ?? new List<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _selectedMindMapTags.RemoveWhere(selected => !tags.Any(tag => string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase)));

        MindMapTagFilters.Clear();
        foreach (var tag in tags)
        {
            var isSelected = _selectedMindMapTags.Contains(tag);
            MindMapTagFilters.Add(new TagFilterItemViewModel(tag, isSelected, SetMindMapTagFilterSelected));
        }

        OnPropertyChanged(nameof(HasMindMapTagFilters));
        OnPropertyChanged(nameof(ActiveHasTagFilters));
        OnPropertyChanged(nameof(HasActiveMindMapTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyFlashcardFilters()
    {
        _flashcardSetsView.Refresh();
        NotifyFlashcardSetsChanged();
    }

    private void ApplyMindMapFilters()
    {
        _mindMapsView.Refresh();
        NotifyMindMapsChanged();
    }

    private bool FilterFlashcardSet(object obj)
    {
        if (obj is not FlashcardSetViewModel set)
            return false;

        if (!IsFlashcardGroupsSectionVisible)
            return false;

        if (_selectedFlashcardTags.Count > 0)
        {
            var tags = set.Document.Tags ?? new List<string>();
            if (!tags.Any(tag => _selectedFlashcardTags.Contains(tag)))
                return false;
        }

        var query = FlashcardSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return set.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || set.TagsDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
               || set.Document.Cards.Any(card =>
                   (card.Question ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                   || (card.Answer ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                   || (card.Category ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private bool FilterMindMap(object obj)
    {
        if (obj is not MindMapViewModel map)
            return false;

        if (!IsMindMapGroupsSectionVisible)
            return false;

        if (_selectedMindMapTags.Count > 0)
        {
            var tags = map.Document.Tags ?? new List<string>();
            if (!tags.Any(tag => _selectedMindMapTags.Contains(tag)))
                return false;
        }

        var query = MindMapSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return map.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || map.TagsDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
               || MindMapContainsText(map.Document.Root, query);
    }

    private static bool MindMapContainsText(MindMapNode? node, string query)
    {
        if (node is null)
            return false;

        return (node.Text ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
               || node.Children.Any(child => MindMapContainsText(child, query));
    }

    private void SelectAllVisibleNotes()
    {
        if (!IsMassSelectMode)
            return;

        var visibleNotes = Notes.Where(MatchesSearch).ToList();
        if (visibleNotes.Count == 0)
            return;

        var allVisibleSelected = visibleNotes.All(note => _massSelectedNoteIds.Contains(note.Document.Id));
        if (allVisibleSelected)
        {
            foreach (var note in Notes)
                SetNoteSelectedState(note, false, notify: false);
        }
        else
        {
            foreach (var note in visibleNotes)
                SetNoteSelectedState(note, true, notify: false);
        }

        NotifyMassSelectionChanged();
    }

    private void DeleteSelectedNotes()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count == 0)
            return;

        foreach (var note in selected)
            Notes.Remove(note);

        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
        NotifyMassSelectionChanged();
    }

    private void RemoveSelectedFromGroups()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count == 0 || selected.Any(note => !note.Document.GroupId.HasValue))
            return;

        foreach (var note in selected)
        {
            if (!note.Document.GroupId.HasValue)
                continue;

            note.Document.GroupId = null;
            note.Document.LastModified = DateTime.Now;
            note.NotifyGroupChanged();
        }

        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
    }

    private void GroupSelectedNotes()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count < 2)
            return;

        var selectedGroupIds = selected
            .Select(note => note.Document.GroupId)
            .Distinct()
            .ToList();

        if (selectedGroupIds.Count == 1 && selectedGroupIds[0].HasValue)
            return;

        var targetGroupId = selected.FirstOrDefault(note => note.Document.GroupId.HasValue)?.Document.GroupId ?? Guid.NewGuid();
        EnsureGroupMetadata(targetGroupId);

        foreach (var note in selected)
        {
            if (note.Document.GroupId == targetGroupId)
                continue;

            note.Document.GroupId = targetGroupId;
            note.Document.LastModified = DateTime.Now;
            note.NotifyGroupChanged();
        }

        NormalizeGroups();
        RebuildGroups();
        SaveNotes();
    }

    private void PinSelectedNotes()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count == 0)
            return;

        var changed = false;
        foreach (var note in selected.Where(note => !note.Document.IsPinned))
        {
            note.Document.IsPinned = true;
            note.Document.LastModified = DateTime.Now;
            changed = true;
        }

        if (!changed)
            return;

        RebuildGroups();
        ApplyFilters();
        SaveNotes();
    }

    private void UnpinSelectedNotes()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count == 0)
            return;

        var changed = false;
        foreach (var note in selected.Where(note => note.Document.IsPinned))
        {
            note.Document.IsPinned = false;
            note.Document.LastModified = DateTime.Now;
            changed = true;
        }

        if (!changed)
            return;

        RebuildGroups();
        ApplyFilters();
        SaveNotes();
    }

    private void DuplicateSelectedNotes()
    {
        var selected = GetMassSelectedNotes();
        if (selected.Count == 0)
            return;

        var duplicates = selected
            .Select(note => new NoteDocument
            {
                Title = $"{note.Document.Title} (Copy)",
                Content = note.Document.Content,
                Tags = note.Document.Tags?.ToList() ?? new List<string>(),
                FontFamily = note.Document.FontFamily,
                FontSize = note.Document.FontSize,
                CreatedAt = DateTime.Now,
                LastModified = DateTime.Now,
                GroupId = null,
                IsPinned = note.Document.IsPinned
            })
            .ToList();

        foreach (var document in duplicates)
            Notes.Add(CreateNoteCard(document));

        RebuildGroups();
        ApplyFilters();
        SaveNotes();
    }

    private bool CanUngroupSelectedNotes()
    {
        if (!IsMassSelectMode || SelectedNotesCount == 0)
            return false;

        return GetMassSelectedNotes().All(note => note.Document.GroupId.HasValue);
    }

    private bool CanGroupSelectedNotes()
    {
        if (!IsMassSelectMode || SelectedNotesCount < 2)
            return false;

        var selectedGroupIds = GetMassSelectedNotes()
            .Select(note => note.Document.GroupId)
            .Distinct()
            .ToList();

        return selectedGroupIds.Count > 1 || !selectedGroupIds[0].HasValue;
    }

    private bool CanPinSelectedNotes()
    {
        if (!IsMassSelectMode || SelectedNotesCount == 0)
            return false;

        return GetMassSelectedNotes().Any(note => !note.Document.IsPinned);
    }

    private bool CanUnpinSelectedNotes()
    {
        if (!IsMassSelectMode || SelectedNotesCount == 0)
            return false;

        return GetMassSelectedNotes().Any(note => note.Document.IsPinned);
    }

    private bool CanDuplicateSelectedNotes()
    {
        return IsMassSelectMode && SelectedNotesCount > 0;
    }

    private List<NoteCardViewModel> GetMassSelectedNotes()
    {
        if (_massSelectedNoteIds.Count == 0)
            return new List<NoteCardViewModel>();

        return Notes
            .Where(note => _massSelectedNoteIds.Contains(note.Document.Id))
            .ToList();
    }

    private void SetNoteSelectedState(NoteCardViewModel note, bool isSelected, bool notify = true)
    {
        if (isSelected)
        {
            if (_massSelectedNoteIds.Add(note.Document.Id))
                note.IsSelectedInMassSelect = true;
        }
        else
        {
            if (_massSelectedNoteIds.Remove(note.Document.Id))
                note.IsSelectedInMassSelect = false;
        }

        if (notify)
            NotifyMassSelectionChanged();
    }

    private void EnsureMassSelectionConsistency()
    {
        var existingIds = Notes.Select(note => note.Document.Id).ToHashSet();
        if (_massSelectedNoteIds.Count > 0)
            _massSelectedNoteIds.RemoveWhere(id => !existingIds.Contains(id));

        foreach (var note in Notes)
            note.IsSelectedInMassSelect = _massSelectedNoteIds.Contains(note.Document.Id);

        NotifyMassSelectionChanged();
    }

    private void NotifyMassSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedNotesCount));
        OnPropertyChanged(nameof(MassSelectSelectionText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SetSortOptionSelected(string key, bool isSelected)
    {
        if (!isSelected)
            return;

        if (IsMindMapsView)
            SelectedMindMapSortOptionKey = key;
        else if (IsFlashcardsView)
            SelectedFlashcardSortOptionKey = key;
        else
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

    private void RefreshFlashcardSortOptions()
    {
        var selectedKey = NormalizeFlashcardSortOptionKey(_selectedFlashcardSortOptionKey);
        _selectedFlashcardSortOptionKey = selectedKey;

        FlashcardSortOptions.Clear();
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedDesc, LocalizationService.GetString("SortByLastModifiedDesc"), selectedKey == SortLastModifiedDesc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedAsc, LocalizationService.GetString("SortByLastModifiedAsc"), selectedKey == SortLastModifiedAsc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtDesc, LocalizationService.GetString("SortByCreatedAtDesc"), selectedKey == SortCreatedAtDesc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtAsc, LocalizationService.GetString("SortByCreatedAtAsc"), selectedKey == SortCreatedAtAsc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleAsc, LocalizationService.GetString("SortByTitleAsc"), selectedKey == SortTitleAsc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleDesc, LocalizationService.GetString("SortByTitleDesc"), selectedKey == SortTitleDesc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortFlashcardCardsDesc, LocalizationService.GetString("SortByFlashcardCardsDesc"), selectedKey == SortFlashcardCardsDesc, SetSortOptionSelected));
        FlashcardSortOptions.Add(new NoteSortOptionItemViewModel(SortFlashcardCardsAsc, LocalizationService.GetString("SortByFlashcardCardsAsc"), selectedKey == SortFlashcardCardsAsc, SetSortOptionSelected));
    }

    private void RefreshMindMapSortOptions()
    {
        var selectedKey = NormalizeMindMapSortOptionKey(_selectedMindMapSortOptionKey);
        _selectedMindMapSortOptionKey = selectedKey;

        MindMapSortOptions.Clear();
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedDesc, LocalizationService.GetString("SortByLastModifiedDesc"), selectedKey == SortLastModifiedDesc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedAsc, LocalizationService.GetString("SortByLastModifiedAsc"), selectedKey == SortLastModifiedAsc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtDesc, LocalizationService.GetString("SortByCreatedAtDesc"), selectedKey == SortCreatedAtDesc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtAsc, LocalizationService.GetString("SortByCreatedAtAsc"), selectedKey == SortCreatedAtAsc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleAsc, LocalizationService.GetString("SortByTitleAsc"), selectedKey == SortTitleAsc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleDesc, LocalizationService.GetString("SortByTitleDesc"), selectedKey == SortTitleDesc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortMindMapNodesDesc, LocalizationService.GetString("SortByMindMapNodesDesc"), selectedKey == SortMindMapNodesDesc, SetSortOptionSelected));
        MindMapSortOptions.Add(new NoteSortOptionItemViewModel(SortMindMapNodesAsc, LocalizationService.GetString("SortByMindMapNodesAsc"), selectedKey == SortMindMapNodesAsc, SetSortOptionSelected));
    }

    private void UpdateSortOptionSelection()
    {
        foreach (var option in SortOptions)
            option.IsSelected = string.Equals(option.Key, _selectedSortOptionKey, StringComparison.Ordinal);
    }

    private void UpdateFlashcardSortOptionSelection()
    {
        foreach (var option in FlashcardSortOptions)
            option.IsSelected = string.Equals(option.Key, _selectedFlashcardSortOptionKey, StringComparison.Ordinal);
    }

    private void UpdateMindMapSortOptionSelection()
    {
        foreach (var option in MindMapSortOptions)
            option.IsSelected = string.Equals(option.Key, _selectedMindMapSortOptionKey, StringComparison.Ordinal);
    }

    private void ApplySortToFlashcardSetsView()
    {
        _flashcardSetsView.SortDescriptions.Clear();

        switch (_selectedFlashcardSortOptionKey)
        {
            case SortLastModifiedAsc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Ascending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortCreatedAtDesc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortCreatedAtAsc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Ascending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortTitleAsc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortTitleDesc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Descending));
                break;
            case SortFlashcardCardsAsc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("CardCount", ListSortDirection.Ascending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortFlashcardCardsDesc:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("CardCount", ListSortDirection.Descending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            default:
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                break;
        }
    }

    private void ApplySortToMindMapsView()
    {
        _mindMapsView.SortDescriptions.Clear();

        switch (_selectedMindMapSortOptionKey)
        {
            case SortLastModifiedAsc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Ascending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortCreatedAtDesc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortCreatedAtAsc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Ascending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortTitleAsc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortTitleDesc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Descending));
                break;
            case SortMindMapNodesAsc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("NodeCount", ListSortDirection.Ascending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortMindMapNodesDesc:
                _mindMapsView.SortDescriptions.Add(new SortDescription("NodeCount", ListSortDirection.Descending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            default:
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                _mindMapsView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                break;
        }
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

    private static string NormalizeFlashcardSortOptionKey(string? value)
    {
        if (string.Equals(value, SortFlashcardCardsDesc, StringComparison.OrdinalIgnoreCase))
            return SortFlashcardCardsDesc;
        if (string.Equals(value, SortFlashcardCardsAsc, StringComparison.OrdinalIgnoreCase))
            return SortFlashcardCardsAsc;

        return NormalizeSortOptionKey(value);
    }

    private static string NormalizeMindMapSortOptionKey(string? value)
    {
        if (string.Equals(value, SortMindMapNodesDesc, StringComparison.OrdinalIgnoreCase))
            return SortMindMapNodesDesc;
        if (string.Equals(value, SortMindMapNodesAsc, StringComparison.OrdinalIgnoreCase))
            return SortMindMapNodesAsc;

        return NormalizeSortOptionKey(value);
    }

    private static string NormalizeDashboard(string? dashboard)
    {
        if (string.Equals(dashboard, DashboardFlashcards, StringComparison.OrdinalIgnoreCase))
            return DashboardFlashcards;
        if (string.Equals(dashboard, DashboardMindMaps, StringComparison.OrdinalIgnoreCase))
            return DashboardMindMaps;

        return DashboardNotes;
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

    private static string GetFlashcardSortOptionDisplayName(string sortKey)
    {
        return NormalizeFlashcardSortOptionKey(sortKey) switch
        {
            SortFlashcardCardsDesc => LocalizationService.GetString("SortByFlashcardCardsDesc"),
            SortFlashcardCardsAsc => LocalizationService.GetString("SortByFlashcardCardsAsc"),
            var normalized => GetSortOptionDisplayName(normalized)
        };
    }

    private static string GetMindMapSortOptionDisplayName(string sortKey)
    {
        return NormalizeMindMapSortOptionKey(sortKey) switch
        {
            SortMindMapNodesDesc => LocalizationService.GetString("SortByMindMapNodesDesc"),
            SortMindMapNodesAsc => LocalizationService.GetString("SortByMindMapNodesAsc"),
            var normalized => GetSortOptionDisplayName(normalized)
        };
    }

    public void RefreshTagFiltersAfterNoteEdit()
    {
        RefreshAvailableTags();
        ApplyFilters();
    }

    public void SetNoteSchedules(NoteCardViewModel note, IEnumerable<NoteScheduleEntry>? schedules)
    {
        if (note is null)
            return;

        var normalized = NormalizeScheduleEntries(schedules);
        var current = GetEffectiveSchedules(note.Document);
        var changed = !AreSchedulesEqual(current, normalized);

        if (!changed)
            return;

        note.Document.Schedules = normalized;
        SyncLegacyScheduleFields(note.Document);
        note.Document.LastModified = DateTime.Now;
        note.NotifyContentChanged();

        ApplyFilters();
        SaveNotes();
    }

    private void RefreshCalendarScheduledNotes()
    {
        var selected = CalendarSelectedDate.Date;
        var items = Notes
            .Where(MatchesSearch)
            .SelectMany(note => GetEffectiveSchedules(note.Document).Select(entry => new CalendarScheduledItemViewModel
            {
                Note = note,
                ScheduledAt = entry.ScheduledAt,
                ScheduleNote = entry.Note ?? string.Empty
            }))
            .Where(item => item.ScheduledAt.Date == selected)
            .OrderBy(item => item.ScheduledAt)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        CalendarScheduledNotes.Clear();
        foreach (var item in items)
            CalendarScheduledNotes.Add(item);

        OnPropertyChanged(nameof(HasCalendarScheduledNotes));
        OnPropertyChanged(nameof(CalendarSelectedDateDisplay));
    }

    private static bool AreSchedulesEqual(IReadOnlyList<NoteScheduleEntry> left, IReadOnlyList<NoteScheduleEntry> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i].ScheduledAt != right[i].ScheduledAt)
                return false;

            if (!string.Equals(left[i].Note ?? string.Empty, right[i].Note ?? string.Empty, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static List<NoteScheduleEntry> NormalizeScheduleEntries(IEnumerable<NoteScheduleEntry>? schedules)
    {
        if (schedules is null)
            return new List<NoteScheduleEntry>();

        return schedules
            .Where(entry => entry != null)
            .Select(entry => new NoteScheduleEntry
            {
                ScheduledAt = entry.ScheduledAt,
                Note = (entry.Note ?? string.Empty).Trim()
            })
            .OrderBy(entry => entry.ScheduledAt)
            .ThenBy(entry => entry.Note, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<NoteScheduleEntry> GetEffectiveSchedules(NoteDocument document)
    {
        EnsureDocumentSchedules(document);
        return document.Schedules;
    }

    private static void EnsureDocumentSchedules(NoteDocument document)
    {
        document.Schedules ??= new List<NoteScheduleEntry>();

        if (document.Schedules.Count == 0 && document.ScheduledAt.HasValue)
        {
            document.Schedules.Add(new NoteScheduleEntry
            {
                ScheduledAt = document.ScheduledAt.Value,
                Note = document.ScheduleNote ?? string.Empty
            });
        }

        document.Schedules = NormalizeScheduleEntries(document.Schedules);
        SyncLegacyScheduleFields(document);
    }

    private static void SyncLegacyScheduleFields(NoteDocument document)
    {
        if (document.Schedules is null || document.Schedules.Count == 0)
        {
            document.ScheduledAt = null;
            document.ScheduleNote = string.Empty;
            return;
        }

        var primary = document.Schedules[0];
        document.ScheduledAt = primary.ScheduledAt;
        document.ScheduleNote = primary.Note ?? string.Empty;
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
        _activeDashboard = NormalizeDashboard(settings.LastView);
        _selectedSortOptionKey = NormalizeSortOptionKey(settings.NoteSortOptionKey);
        _selectedMindMapSortOptionKey = NormalizeMindMapSortOptionKey(settings.MindMapSortOptionKey);
        _isRecentSectionExpanded = settings.IsRecentSectionExpanded;
        _isGroupsSectionExpanded = settings.IsGroupsSectionExpanded;
        _isUngroupedSectionExpanded = settings.IsUngroupedSectionExpanded;
        _isCalendarSectionExpanded = settings.IsCalendarSectionExpanded;
        _isMindMapGroupsSectionExpanded = settings.IsMindMapGroupsSectionExpanded;
        _isRecentSectionVisible = settings.IsRecentSectionVisible;
        _isGroupsSectionVisible = settings.IsGroupsSectionVisible;
        _isUngroupedSectionVisible = settings.IsUngroupedSectionVisible;
        _isCalendarSectionVisible = settings.IsCalendarSectionVisible;
        _isMindMapGroupsSectionVisible = settings.IsMindMapGroupsSectionVisible;
        _isGroupsFirst = settings.IsGroupsFirst;
        _isCalendarFirst = settings.IsCalendarFirst;
        _viewMode = NormalizeViewMode(settings.DefaultViewMode);

        LocalizationService.SetCulture(_selectedLanguage);
        ThemeManager.SetTheme(_selectedTheme);

        _isLoadingSettings = false;
    }

    private void SaveAppSettings()
    {
        if (_isLoadingSettings)
            return;

        var settings = AppSettingsService.Load();
        settings.Language = _selectedLanguage;
        settings.Theme = _selectedTheme;
        settings.NoteSortOptionKey = _selectedSortOptionKey;
        settings.MindMapSortOptionKey = _selectedMindMapSortOptionKey;
        settings.EnableScrollbar = _enableScrollbar;
        settings.IsRecentSectionExpanded = _isRecentSectionExpanded;
        settings.IsGroupsSectionExpanded = _isGroupsSectionExpanded;
        settings.IsUngroupedSectionExpanded = _isUngroupedSectionExpanded;
        settings.IsCalendarSectionExpanded = _isCalendarSectionExpanded;
        settings.IsMindMapGroupsSectionExpanded = _isMindMapGroupsSectionExpanded;
        settings.IsRecentSectionVisible = _isRecentSectionVisible;
        settings.IsGroupsSectionVisible = _isGroupsSectionVisible;
        settings.IsUngroupedSectionVisible = _isUngroupedSectionVisible;
        settings.IsCalendarSectionVisible = _isCalendarSectionVisible;
        settings.IsMindMapGroupsSectionVisible = _isMindMapGroupsSectionVisible;
        settings.IsGroupsFirst = _isGroupsFirst;
        settings.IsCalendarFirst = _isCalendarFirst;
        settings.DefaultViewMode = _viewMode;
        settings.LastView = _activeDashboard;

        AppSettingsService.Save(settings);
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
            {
                EnsureDocumentSchedules(doc);
                Notes.Add(CreateNoteCard(doc));
            }

            RefreshAvailableTags();
            NormalizeGroups();
            RebuildGroups();
            RefreshRecentNotes();
            RefreshCalendarScheduledNotes();

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
    private static string NormalizeViewMode(string? viewMode)
        => string.Equals(viewMode, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";

    private string _viewMode = "Grid";

    public string ViewMode
    {
        get => _viewMode;
        set
        {
            var normalized = NormalizeViewMode(value);
            if (SetProperty(ref _viewMode, normalized))
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
