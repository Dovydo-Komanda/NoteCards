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
    private const int RecentDashboardItemsLimit = 20;
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
    private const string SortQuizQuestionsDesc = "quiz-questions-desc";
    private const string SortQuizQuestionsAsc = "quiz-questions-asc";
    private const string DashboardNotes = "Notes";
    private const string DashboardFlashcards = "Flashcards";
    private const string DashboardMindMaps = "MindMaps";
    private const string DashboardQuizzes = "Quizzes";

    private bool _isLoadingSettings;
    private bool _saveNotesQueued;
    private DispatcherTimer? _autoSaveTimer;
    private bool _enableScrollbar = true;
    private string _selectedLanguage = LocalizationService.English;
    private string _selectedTheme = "Light";
    private string _activeDashboard = DashboardNotes;
    private string _selectedSortOptionKey = SortLastModifiedDesc;
    private string _selectedFlashcardSortOptionKey = SortLastModifiedDesc;
    private string _selectedMindMapSortOptionKey = SortLastModifiedDesc;
    private string _selectedQuizSortOptionKey = SortLastModifiedDesc;
    private readonly Dictionary<Guid, NoteGroupData> _groupMetadata = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedFlashcardTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedMindMapTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedQuizTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _massSelectedNoteIds = new();
    private bool _isMassSelectMode;
    private DateTime _calendarSelectedDate = DateTime.Today;
    private AppSettings _settings;
    private readonly Dictionary<Guid, FlashcardSetGroupData> _flashcardSetGroupMetadata = new();
    private readonly Dictionary<Guid, MindMapGroupData> _mindMapGroupMetadata = new();
    private readonly Dictionary<Guid, QuizGroupData> _quizGroupMetadata = new();
    private readonly ObservableCollection<QuizViewModel> _quizzes = new();
    public ObservableCollection<FlashcardSetGroupViewModel> FlashcardSetGroups { get; } = new();
    public ObservableCollection<MindMapGroupViewModel> MindMapGroups { get; } = new();
    public ObservableCollection<QuizGroupViewModel> QuizGroups { get; } = new();

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

    private string GetQuizzesFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteCards");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "quizzes.json");
    }

    public void DuplicateQuiz(QuizViewModel sourceQuiz)
    {
        if (sourceQuiz is null)
            return;

        // Create a deep copy of the quiz document
        var newDocument = CloneQuizDocument(sourceQuiz.Document);

        // Create new view model
        var newQuiz = new QuizViewModel(newDocument);

        // Add to collection
        Quizzes.Add(newQuiz);

        // Save to disk
        ReorderQuizzes();
        SaveQuizzes();
        NotifyQuizzesChanged();
    }

    private static QuizDocument CloneQuizDocument(QuizDocument source)
    {
        return new QuizDocument
        {
            Id = Guid.NewGuid(), // New ID for the copy
            Title = $"{source.Title} (Copy)",
            Tags = source.Tags.ToList(),
            Questions = source.Questions.Select(CloneQuizQuestion).ToList(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.Now,
            AiModelDisplayName = source.AiModelDisplayName,
            SourceNoteId = source.SourceNoteId,
            GroupId = source.GroupId,
            Schedules = source.Schedules?.Select(schedule => new NoteScheduleEntry
            {
                ScheduledAt = schedule.ScheduledAt,
                Note = schedule.Note
            }).ToList() ?? new List<NoteScheduleEntry>()
        };
    }

    private static QuizQuestion CloneQuizQuestion(QuizQuestion source)
    {
        return new QuizQuestion
        {
            Type = source.Type,
            Question = source.Question,
            Options = source.Options.Select(o => new QuizOption
            {
                Text = o.Text,
                IsCorrect = o.IsCorrect
            }).ToList(),
            Explanation = source.Explanation
        };
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
                RefreshQuizSortOptions();
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
    public bool IsQuizzesView => string.Equals(_activeDashboard, DashboardQuizzes, StringComparison.Ordinal);
    public FlashcardItem? SelectedFlashcard { get; set; }

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
        OnPropertyChanged(nameof(IsQuizzesView));
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
        QuizTagFilters = new ObservableCollection<TagFilterItemViewModel>();
        SortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        FlashcardSortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        MindMapSortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
        QuizSortOptions = new ObservableCollection<NoteSortOptionItemViewModel>();
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
        _quizzesView = CollectionViewSource.GetDefaultView(Quizzes);
        _quizzesView.Filter = FilterQuiz;
        ApplySortToQuizzesView();
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
            RefreshCalendarScheduledNotes();
            RefreshRecentFlashcardSets();
        };
        MindMaps.CollectionChanged += (_, _) =>
        {
            RefreshAvailableMindMapTags();
            ApplyMindMapFilters();
            NotifyMindMapsChanged();
            RefreshCalendarScheduledNotes();
            RefreshRecentMindMaps();
        };
        Quizzes.CollectionChanged += (_, _) =>
        {
            RefreshAvailableQuizTags();
            ApplyQuizFilters();
            NotifyQuizzesChanged();
            RefreshCalendarScheduledNotes();
            RefreshRecentQuizzes();
        };
        
        NoteCards.Services.ActivityTracker.ActivityUpdated += RefreshActivityStats;
        
        RefreshSortOptions();
        RefreshFlashcardSortOptions();
        RefreshMindMapSortOptions();
        RefreshQuizSortOptions();
        RefreshAvailableTags();
        RefreshAvailableFlashcardTags();
        RefreshAvailableMindMapTags();
        RefreshAvailableQuizTags();
        RefreshRecentNotes();
        RefreshRecentFlashcardSets();
        RefreshRecentMindMaps();
        RefreshRecentQuizzes();
        RefreshCalendarScheduledNotes();
        LoadFlashcards();
        LoadMindMaps();
        LoadQuizzes();
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
        ShowQuizzesCommand = new RelayCommand(() =>
        {
            SetActiveDashboard(DashboardQuizzes);
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
        ApplyAutoSaveSettings(AppSettingsService.Load());
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
            List<FlashcardSetDocument> sets;
            try
            {
                var store = JsonSerializer.Deserialize<FlashcardSetStoreData>(json);
                if (store?.Sets != null)
                {
                    sets = store.Sets;
                    _flashcardSetGroupMetadata.Clear();
                    foreach (var meta in store.Groups ?? new List<FlashcardSetGroupData>())
                        _flashcardSetGroupMetadata[meta.GroupId] = meta;
                }
                else
                {
                    sets = JsonSerializer.Deserialize<List<FlashcardSetDocument>>(json) ?? new();
                }
            }
            catch
            {
                sets = JsonSerializer.Deserialize<List<FlashcardSetDocument>>(json) ?? new();
            }

            foreach (var set in sets
                         .Where(set => set != null)
                         .OrderByDescending(set => set.IsPinned)
                         .ThenByDescending(set => set.LastModified)
                         .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase))
            {
                NormalizeFlashcardSetDocument(set);
                FlashcardSets.Add(new FlashcardSetViewModel(set));
            }

            NormalizeFlashcardSetGroups();
            RebuildFlashcardSetGroups();
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
            existing.Document.GroupId = document.GroupId;
            existing.Document.Schedules = document.Schedules;
            existing.Document.IsPinned = document.IsPinned;
            existing.NotifyChanged();
        }

        ReorderFlashcardSets();
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        RefreshAvailableFlashcardTags();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
        NotifyFlashcardSetsChanged();
        RefreshRecentFlashcardSets();
        return existing;
    }

    public void DeleteFlashcardSet(FlashcardSetViewModel set)
    {
        if (set is null)
            return;

        FlashcardSets.Remove(set);
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        SaveFlashcardSets();
        RefreshAvailableFlashcardTags();
        ApplyFlashcardFilters();
        NotifyFlashcardSetsChanged();
        RefreshRecentFlashcardSets();
    }

    public void SaveFlashcardSets()
    {
        var path = GetFlashcardSetsFilePath();
        var documents = FlashcardSets
            .Select(set => set.Document)
            .OrderByDescending(set => set.IsPinned)
            .ThenByDescending(set => set.LastModified)
            .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var store = new FlashcardSetStoreData
        {
            Sets = documents,
            Groups = _flashcardSetGroupMetadata.Values
                .OrderBy(g => g.SortOrder ?? int.MaxValue)
                .ThenBy(g => g.Name)
                .ToList()
        };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void ReorderFlashcardSets()
    {
        var ordered = FlashcardSets
            .OrderByDescending(set => set.Document.IsPinned)
            .ThenByDescending(set => set.Document.LastModified)
            .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FlashcardSets.Clear();
        foreach (var set in ordered)
            FlashcardSets.Add(set);
    }

    private void NotifyFlashcardSetsChanged()
    {
        OnPropertyChanged(nameof(HasFlashcardSets));
        OnPropertyChanged(nameof(HasFlashcardSetGroups));
        OnPropertyChanged(nameof(HasUngroupedFlashcardSets));
        OnPropertyChanged(nameof(FlashcardSetCount));
        OnPropertyChanged(nameof(FlashcardSetCountText));
        OnPropertyChanged(nameof(HasRecentFlashcardSets));
    }

    private void NormalizeFlashcardSetGroups()
    {
        var grouped = FlashcardSets
            .Where(set => set.Document.GroupId.HasValue)
            .GroupBy(set => set.Document.GroupId!.Value)
            .ToList();

        foreach (var group in grouped.Where(group => group.Count() < 2))
        {
            foreach (var set in group)
            {
                set.Document.GroupId = null;
                set.NotifyChanged();
            }

            _flashcardSetGroupMetadata.Remove(group.Key);
        }
    }

    public void RebuildFlashcardSetGroups()
    {
        FlashcardSetGroups.Clear();

        var grouped = FlashcardSets
            .Where(set => set.Document.GroupId.HasValue)
            .GroupBy(set => set.Document.GroupId!.Value)
            .OrderBy(group => EnsureFlashcardSetGroupMetadata(group.Key).SortOrder ?? int.MaxValue)
            .ThenByDescending(group => group.Max(set => set.Document.LastModified))
            .ToList();

        foreach (var group in grouped)
        {
            var visibleSets = group
                .Where(MatchesFlashcardSetFilters)
                .OrderByDescending(set => set.Document.IsPinned)
                .ThenByDescending(set => set.Document.LastModified)
                .ThenBy(set => set.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (visibleSets.Count == 0)
                continue;

            var metadata = EnsureFlashcardSetGroupMetadata(group.Key);
            FlashcardSetGroups.Add(new FlashcardSetGroupViewModel(group.Key, metadata.Name, metadata.BackgroundColor, visibleSets));
        }

        OnPropertyChanged(nameof(HasFlashcardSetGroups));
        OnPropertyChanged(nameof(HasUngroupedFlashcardSets));
    }

    private FlashcardSetGroupData EnsureFlashcardSetGroupMetadata(Guid groupId)
    {
        if (_flashcardSetGroupMetadata.TryGetValue(groupId, out var metadata))
        {
            metadata.SortOrder ??= GetNextFlashcardSetGroupSortOrder();
            return metadata;
        }

        metadata = new FlashcardSetGroupData
        {
            GroupId = groupId,
            Name = string.Format(LocalizationService.GetString("GroupTitleFormat"), groupId.ToString()[..4].ToUpperInvariant()),
            BackgroundColor = DefaultGroupBackground,
            SortOrder = GetNextFlashcardSetGroupSortOrder()
        };
        _flashcardSetGroupMetadata[groupId] = metadata;
        return metadata;
    }

    private int GetNextFlashcardSetGroupSortOrder()
    {
        return _flashcardSetGroupMetadata.Values
            .Select(group => group.SortOrder ?? -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;
    }

    public bool TryGroupFlashcardSets(FlashcardSetViewModel draggedSet, FlashcardSetViewModel targetSet)
    {
        if (draggedSet is null || targetSet is null || ReferenceEquals(draggedSet, targetSet))
            return false;

        var targetGroupId = targetSet.Document.GroupId ?? draggedSet.Document.GroupId ?? Guid.NewGuid();
        EnsureFlashcardSetGroupMetadata(targetGroupId);

        var changed = false;
        foreach (var set in new[] { draggedSet, targetSet })
        {
            if (set.Document.GroupId == targetGroupId)
                continue;

            set.Document.GroupId = targetGroupId;
            set.NotifyChanged();
            changed = true;
        }

        if (!changed)
            return false;

        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
        return true;
    }

    public bool TryReorderFlashcardSetsWithinGroup(FlashcardSetViewModel dragged, FlashcardSetViewModel target, bool placeAfter)
    {
        if (ReferenceEquals(dragged, target)) return false;
        var groupId = dragged.Document.GroupId;
        if (!groupId.HasValue || target.Document.GroupId != groupId) return false;

        var draggedIdx = FlashcardSets.IndexOf(dragged);
        var targetIdx = FlashcardSets.IndexOf(target);
        if (draggedIdx < 0 || targetIdx < 0) return false;

        var newIdx = placeAfter ? targetIdx + 1 : targetIdx;
        if (draggedIdx < newIdx) newIdx--;
        if (newIdx == draggedIdx) return false;

        FlashcardSets.Move(draggedIdx, Math.Clamp(newIdx, 0, FlashcardSets.Count - 1));
        RebuildFlashcardSetGroups();
        SaveFlashcardSets();
        return true;
    }

    public bool TryMoveFlashcardSetToGroup(FlashcardSetViewModel draggedSet, FlashcardSetGroupViewModel targetGroup)
    {
        if (draggedSet.Document.GroupId == targetGroup.GroupId)
            return false;

        draggedSet.Document.GroupId = targetGroup.GroupId;
        EnsureFlashcardSetGroupMetadata(targetGroup.GroupId);
        draggedSet.NotifyChanged();
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
        return true;
    }

    public bool MoveFlashcardSetGroupUp(FlashcardSetGroupViewModel group)
    {
        return TryMoveFlashcardSetGroup(group, moveUp: true);
    }

    public bool MoveFlashcardSetGroupDown(FlashcardSetGroupViewModel group)
    {
        return TryMoveFlashcardSetGroup(group, moveUp: false);
    }

    private bool TryMoveFlashcardSetGroup(FlashcardSetGroupViewModel group, bool moveUp)
    {
        var currentIndex = FlashcardSetGroups.IndexOf(group);
        var targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= FlashcardSetGroups.Count)
            return false;

        var target = FlashcardSetGroups[targetIndex];
        var currentMeta = EnsureFlashcardSetGroupMetadata(group.GroupId);
        var targetMeta = EnsureFlashcardSetGroupMetadata(target.GroupId);
        (currentMeta.SortOrder, targetMeta.SortOrder) = (targetMeta.SortOrder, currentMeta.SortOrder);
        FlashcardSetGroups.Move(currentIndex, targetIndex);
        SaveFlashcardSets();
        return true;
    }

    public bool RenameFlashcardSetGroup(FlashcardSetGroupViewModel group, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var metadata = EnsureFlashcardSetGroupMetadata(group.GroupId);
        metadata.Name = trimmed;
        group.Name = trimmed;
        SaveFlashcardSets();
        return true;
    }

    public bool SetFlashcardSetGroupBackgroundColor(FlashcardSetGroupViewModel group, string backgroundColor)
    {
        if (string.IsNullOrWhiteSpace(backgroundColor))
            return false;

        var metadata = EnsureFlashcardSetGroupMetadata(group.GroupId);
        metadata.BackgroundColor = backgroundColor;
        group.SetBackground(backgroundColor);
        SaveFlashcardSets();
        return true;
    }

    public void DisbandFlashcardSetGroup(FlashcardSetGroupViewModel group, bool deleteSets)
    {
        var setsInGroup = FlashcardSets.Where(set => set.Document.GroupId == group.GroupId).ToList();
        if (deleteSets)
        {
            foreach (var set in setsInGroup)
                FlashcardSets.Remove(set);
        }
        else
        {
            foreach (var set in setsInGroup)
            {
                set.Document.GroupId = null;
                set.NotifyChanged();
            }
        }

        _flashcardSetGroupMetadata.Remove(group.GroupId);
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
    }

    public void RemoveFlashcardSetFromGroup(FlashcardSetViewModel set)
    {
        if (set?.Document.GroupId is null)
            return;

        set.Document.GroupId = null;
        set.NotifyChanged();
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
    }

    public void DuplicateFlashcardSet(FlashcardSetViewModel sourceSet)
    {
        if (sourceSet is null)
            return;

        var source = sourceSet.Document;
        var document = new FlashcardSetDocument
        {
            Id = Guid.NewGuid(),
            Title = $"{source.Title} (Copy)",
            Tags = source.Tags.ToList(),
            SetNames = source.SetNames.ToDictionary(pair => pair.Key, pair => pair.Value),
            Cards = source.Cards.Select(card => new FlashcardItem
            {
                Id = Guid.NewGuid(),
                Question = card.Question,
                Answer = card.Answer,
                Category = card.Category,
                SetIndex = card.SetIndex,
                IsKnown = card.IsKnown,
                IsUnknown = card.IsUnknown
            }).ToList(),
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.Now,
            AiModelDisplayName = source.AiModelDisplayName
        };

        FlashcardSets.Add(new FlashcardSetViewModel(document));
        ReorderFlashcardSets();
        SaveFlashcardSets();
        NotifyFlashcardSetsChanged();
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
                Id = card.Id == Guid.Empty ? Guid.NewGuid() : card.Id,
                Question = card.Question?.Trim() ?? string.Empty,
                Answer = card.Answer?.Trim() ?? string.Empty,
                Category = card.Category?.Trim() ?? string.Empty,
                SetIndex = Math.Max(1, card.SetIndex),
                IsKnown = card.IsKnown && !card.IsUnknown,
                IsUnknown = card.IsUnknown && !card.IsKnown
            })
            .ToList() ?? new List<FlashcardItem>();
        document.CreatedAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        document.LastModified = document.LastModified == default ? DateTime.Now : document.LastModified;
        document.AiModelDisplayName = document.AiModelDisplayName?.Trim() ?? string.Empty;
        document.StudySession ??= new FlashcardStudySession();

        var cardIds = document.Cards.Select(card => card.Id).ToHashSet();
        document.StudySession.CurrentSetIndex = Math.Max(1, document.StudySession.CurrentSetIndex);
        if (document.StudySession.CurrentCardId.HasValue && !cardIds.Contains(document.StudySession.CurrentCardId.Value))
            document.StudySession.CurrentCardId = null;
        document.StudySession.History = document.StudySession.History
            .Where(id => cardIds.Contains(id))
            .ToList();
        if (document.StudySession.HistoryPosition >= document.StudySession.History.Count)
            document.StudySession.HistoryPosition = document.StudySession.History.Count - 1;
        if (document.StudySession.HistoryPosition < -1)
            document.StudySession.HistoryPosition = -1;
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

        // Try new format first, fall back to legacy list format
        List<MindMapDocument> maps;
        try
        {
            var store = JsonSerializer.Deserialize<MindMapStoreData>(json);
            if (store?.Maps != null)
            {
                maps = store.Maps;
                _mindMapGroupMetadata.Clear();
                foreach (var meta in store.Groups ?? new List<MindMapGroupData>())
                    _mindMapGroupMetadata[meta.GroupId] = meta;
            }
            else
            {
                maps = JsonSerializer.Deserialize<List<MindMapDocument>>(json) ?? new();
            }
        }
        catch
        {
            try
            {
                maps = JsonSerializer.Deserialize<List<MindMapDocument>>(json) ?? new();
            }
            catch
            {
                maps = new List<MindMapDocument>();
                _mindMapGroupMetadata.Clear();
            }
        }

        foreach (var map in maps
                     .Where(map => map != null)
                     .OrderByDescending(map => map.IsPinned)
                     .ThenByDescending(map => map.LastModified)
                     .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            NormalizeMindMapDocument(map);
            MindMaps.Add(new MindMapViewModel(map));
        }

        NormalizeMindMapGroups();
        RebuildMindMapGroups();
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
            existing.Document.LayoutMode = document.LayoutMode;
            existing.Document.UseManualPositions = document.UseManualPositions;
            existing.Document.CreatedAt = document.CreatedAt;
            existing.Document.LastModified = document.LastModified;
            existing.Document.AiModelDisplayName = document.AiModelDisplayName;
            existing.Document.SourceNoteId = document.SourceNoteId;
            existing.Document.GroupId = document.GroupId;
            existing.Document.Schedules = document.Schedules;
            existing.Document.IsPinned = document.IsPinned;
            existing.NotifyChanged();
        }

        ReorderMindMaps();
        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        RefreshAvailableMindMapTags();
        ApplyMindMapFilters();
        SaveMindMaps();
        NotifyMindMapsChanged();
        RefreshRecentMindMaps();
        return existing;
    }
    public MindMapGroupViewModel CreateMindMapGroup(string name)
    {
        var groupId = Guid.NewGuid();
        _mindMapGroupMetadata[groupId] = new MindMapGroupData { GroupId = groupId, Name = name, BackgroundColor = DefaultGroupBackground };
        var group = new MindMapGroupViewModel(groupId, name, DefaultGroupBackground, Enumerable.Empty<MindMapViewModel>());
        MindMapGroups.Add(group);
        SaveMindMaps();
        return group;
    }

    public bool RenameMindMapGroup(MindMapGroupViewModel group, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return false;
        group.Name = newName.Trim();
        if (_mindMapGroupMetadata.TryGetValue(group.GroupId, out var meta))
            meta.Name = group.Name;
        SaveMindMaps();
        return true;
    }

    public void DeleteMindMapGroup(MindMapGroupViewModel group)
    {
        // Ungroup all mind maps in this group
        foreach (var map in MindMaps.Where(m => m.Document.GroupId == group.GroupId))
            map.Document.GroupId = null;

        _mindMapGroupMetadata.Remove(group.GroupId);
        MindMapGroups.Remove(group);
        SaveMindMaps();
    }

    private void NormalizeMindMapGroups()
    {
        var grouped = MindMaps
            .Where(map => map.Document.GroupId.HasValue)
            .GroupBy(map => map.Document.GroupId!.Value)
            .ToList();

        foreach (var group in grouped.Where(group => group.Count() < 2))
        {
            foreach (var map in group)
            {
                map.Document.GroupId = null;
                map.NotifyChanged();
            }

            _mindMapGroupMetadata.Remove(group.Key);
        }
    }

    public void AddMindMapToGroup(MindMapViewModel map, MindMapGroupViewModel group)
    {
        map.Document.GroupId = group.GroupId;
        SaveMindMaps();
        RebuildMindMapGroups();
    }

    public bool TryGroupMindMaps(MindMapViewModel draggedMap, MindMapViewModel targetMap)
    {
        if (draggedMap is null || targetMap is null || ReferenceEquals(draggedMap, targetMap))
            return false;

        var targetGroupId = targetMap.Document.GroupId ?? draggedMap.Document.GroupId ?? Guid.NewGuid();
        if (!_mindMapGroupMetadata.ContainsKey(targetGroupId))
            _mindMapGroupMetadata[targetGroupId] = new MindMapGroupData
            {
                GroupId = targetGroupId,
                Name = string.Format(LocalizationService.GetString("GroupTitleFormat"), targetGroupId.ToString()[..4].ToUpperInvariant()),
                BackgroundColor = DefaultGroupBackground
            };

        var changed = false;
        foreach (var map in new[] { draggedMap, targetMap })
        {
            if (map.Document.GroupId == targetGroupId)
                continue;

            map.Document.GroupId = targetGroupId;
            map.NotifyChanged();
            changed = true;
        }

        if (!changed)
            return false;

        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        ApplyMindMapFilters();
        SaveMindMaps();
        return true;
    }

    public bool TryReorderMindMapsWithinGroup(MindMapViewModel dragged, MindMapViewModel target, bool placeAfter)
    {
        if (ReferenceEquals(dragged, target)) return false;
        var groupId = dragged.Document.GroupId;
        if (!groupId.HasValue || target.Document.GroupId != groupId) return false;

        var draggedIdx = MindMaps.IndexOf(dragged);
        var targetIdx = MindMaps.IndexOf(target);
        if (draggedIdx < 0 || targetIdx < 0) return false;

        var newIdx = placeAfter ? targetIdx + 1 : targetIdx;
        if (draggedIdx < newIdx) newIdx--;
        if (newIdx == draggedIdx) return false;

        MindMaps.Move(draggedIdx, Math.Clamp(newIdx, 0, MindMaps.Count - 1));
        RebuildMindMapGroups();
        SaveMindMaps();
        return true;
    }

    public bool TryMoveMindMapToGroup(MindMapViewModel draggedMap, MindMapGroupViewModel targetGroup)
    {
        if (draggedMap.Document.GroupId == targetGroup.GroupId)
            return false;

        draggedMap.Document.GroupId = targetGroup.GroupId;
        if (!_mindMapGroupMetadata.ContainsKey(targetGroup.GroupId))
            _mindMapGroupMetadata[targetGroup.GroupId] = new MindMapGroupData
            {
                GroupId = targetGroup.GroupId,
                Name = targetGroup.Name,
                BackgroundColor = targetGroup.BackgroundColor
            };
        draggedMap.NotifyChanged();
        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        ApplyMindMapFilters();
        SaveMindMaps();
        return true;
    }

    public bool MoveMindMapGroupUp(MindMapGroupViewModel group)
    {
        return TryMoveMindMapGroup(group, moveUp: true);
    }

    public bool MoveMindMapGroupDown(MindMapGroupViewModel group)
    {
        return TryMoveMindMapGroup(group, moveUp: false);
    }

    private bool TryMoveMindMapGroup(MindMapGroupViewModel group, bool moveUp)
    {
        var currentIndex = MindMapGroups.IndexOf(group);
        var targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= MindMapGroups.Count)
            return false;

        var target = MindMapGroups[targetIndex];
        var currentMeta = _mindMapGroupMetadata[group.GroupId];
        var targetMeta = _mindMapGroupMetadata[target.GroupId];
        currentMeta.SortOrder ??= currentIndex;
        targetMeta.SortOrder ??= targetIndex;
        (currentMeta.SortOrder, targetMeta.SortOrder) = (targetMeta.SortOrder, currentMeta.SortOrder);
        MindMapGroups.Move(currentIndex, targetIndex);
        SaveMindMaps();
        return true;
    }

    public bool SetMindMapGroupBackgroundColor(MindMapGroupViewModel group, string backgroundColor)
    {
        if (string.IsNullOrWhiteSpace(backgroundColor))
            return false;

        if (!_mindMapGroupMetadata.TryGetValue(group.GroupId, out var metadata))
            return false;

        metadata.BackgroundColor = backgroundColor;
        group.SetBackground(backgroundColor);
        SaveMindMaps();
        return true;
    }

    public void DisbandMindMapGroup(MindMapGroupViewModel group, bool deleteMaps)
    {
        var mapsInGroup = MindMaps.Where(map => map.Document.GroupId == group.GroupId).ToList();
        if (deleteMaps)
        {
            foreach (var map in mapsInGroup)
                MindMaps.Remove(map);
        }
        else
        {
            foreach (var map in mapsInGroup)
            {
                map.Document.GroupId = null;
                map.NotifyChanged();
            }
        }

        _mindMapGroupMetadata.Remove(group.GroupId);
        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        ApplyMindMapFilters();
        SaveMindMaps();
    }

    public void RemoveMindMapFromGroup(MindMapViewModel map)
    {
        if (map?.Document.GroupId is null)
            return;

        map.Document.GroupId = null;
        map.NotifyChanged();
        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        ApplyMindMapFilters();
        SaveMindMaps();
    }

    public void SaveMindMaps()
    {
        var path = GetMindMapsFilePath();
        var documents = MindMaps
            .Select(map => map.Document)
            .OrderByDescending(map => map.IsPinned)
            .ThenByDescending(map => map.LastModified)
            .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var store = new MindMapStoreData
        {
            Maps = documents,
            Groups = _mindMapGroupMetadata.Values
                .OrderBy(g => g.SortOrder ?? int.MaxValue)
                .ThenBy(g => g.Name)
                .ToList()
        };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private void ReorderMindMaps()
    {
        var ordered = MindMaps
            .OrderByDescending(map => map.Document.IsPinned)
            .ThenByDescending(map => map.Document.LastModified)
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

    public MindMapViewModel? DuplicateMindMap(MindMapViewModel sourceMindMap)
    {
        if (sourceMindMap is null)
            return null;

        // Create a deep copy of the mind map document
        var newDocument = CloneMindMapDocument(sourceMindMap.Document);

        // Create new view model
        var newMindMap = new MindMapViewModel(newDocument);

        // Add to collection
        MindMaps.Add(newMindMap);

        // Save to disk
        SaveMindMaps();

        return newMindMap;
    }

    private static MindMapDocument CloneMindMapDocument(MindMapDocument source)
    {
        return new MindMapDocument
        {
            Id = Guid.NewGuid(), // New ID for the copy
            Title = $"{source.Title} (Copy)",
            Tags = source.Tags.ToList(),
            Root = CloneMindMapNode(source.Root),
            LayoutMode = source.LayoutMode,
            UseManualPositions = source.UseManualPositions,
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.Now,
            AiModelDisplayName = source.AiModelDisplayName,
            SourceNoteId = source.SourceNoteId
        };
    }

    private static MindMapNode CloneMindMapNode(MindMapNode source)
    {
        var newNode = new MindMapNode
        {
            Text = source.Text,
            IsExpanded = source.IsExpanded,
            BackgroundColor = source.BackgroundColor,
            BorderColor = source.BorderColor,
            BorderThickness = source.BorderThickness,
            NodeShape = source.NodeShape,
            Icon = source.Icon,
            IconBadgeColor = source.IconBadgeColor,
            ManualX = source.ManualX,
            ManualY = source.ManualY,
            Children = new List<MindMapNode>()
        };

        // Recursively clone children
        foreach (var child in source.Children)
        {
            newNode.Children.Add(CloneMindMapNode(child));
        }

        return newNode;
    }

    private void NotifyMindMapsChanged()
    {
        OnPropertyChanged(nameof(HasMindMaps));
        OnPropertyChanged(nameof(HasMindMapGroups));
        OnPropertyChanged(nameof(HasUngroupedMindMaps));
        OnPropertyChanged(nameof(MindMapCount));
        OnPropertyChanged(nameof(MindMapCountText));
        OnPropertyChanged(nameof(HasRecentMindMaps));
    }

    private void LoadQuizzes()
    {
        Quizzes.Clear();

        var path = GetQuizzesFilePath();
        if (!File.Exists(path))
        {
            NotifyQuizzesChanged();
            return;
        }

        var json = File.ReadAllText(path);
        List<QuizDocument> documents;
        try
        {
            var store = JsonSerializer.Deserialize<QuizStoreData>(json);
            if (store?.Quizzes != null)
            {
                documents = store.Quizzes;
                _quizGroupMetadata.Clear();
                foreach (var meta in store.Groups ?? new List<QuizGroupData>())
                    _quizGroupMetadata[meta.GroupId] = meta;
            }
            else
            {
                documents = JsonSerializer.Deserialize<List<QuizDocument>>(json) ?? new();
            }
        }
        catch
        {
            documents = JsonSerializer.Deserialize<List<QuizDocument>>(json) ?? new();
        }

        foreach (var quiz in documents
                     .Where(quiz => quiz != null)
                     .OrderByDescending(quiz => quiz.IsPinned)
                     .ThenByDescending(quiz => quiz.LastModified)
                     .ThenBy(quiz => quiz.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            NormalizeQuizDocument(quiz);
            Quizzes.Add(new QuizViewModel(quiz));
        }

        NormalizeQuizGroups();
        RebuildQuizGroups();
        NotifyQuizzesChanged();
    }

    public QuizViewModel AddOrUpdateQuiz(QuizDocument document)
    {
        NormalizeQuizDocument(document);

        var existing = Quizzes.FirstOrDefault(quiz => quiz.Document.Id == document.Id);
        if (existing is null)
        {
            existing = new QuizViewModel(document);
            Quizzes.Add(existing);
        }
        else
        {
            existing.Document.Title = document.Title;
            existing.Document.Tags = document.Tags;
            existing.Document.Questions = document.Questions;
            existing.Document.CreatedAt = document.CreatedAt;
            existing.Document.LastModified = document.LastModified;
            existing.Document.AiModelDisplayName = document.AiModelDisplayName;
            existing.Document.SourceNoteId = document.SourceNoteId;
            existing.Document.GroupId = document.GroupId;
            existing.Document.Schedules = document.Schedules;
            existing.Document.IsPinned = document.IsPinned;
            existing.Document.TimeLimitSeconds = document.TimeLimitSeconds;
            existing.Document.PassingScorePercent = document.PassingScorePercent;
            existing.Document.Attempts = document.Attempts;
            existing.NotifyChanged();
        }

        ReorderQuizzes();
        NormalizeQuizGroups();
        RebuildQuizGroups();
        RefreshAvailableQuizTags();
        ApplyQuizFilters();
        SaveQuizzes();
        NotifyQuizzesChanged();
        RefreshRecentQuizzes();
        return existing;
    }

    public void DeleteQuiz(QuizViewModel quiz)
    {
        if (quiz is null)
            return;

        Quizzes.Remove(quiz);
        NormalizeQuizGroups();
        RebuildQuizGroups();
        SaveQuizzes();
        RefreshAvailableQuizTags();
        ApplyQuizFilters();
        NotifyQuizzesChanged();
        RefreshRecentQuizzes();
    }

    public void SaveQuizzes()
    {
        var path = GetQuizzesFilePath();
        var documents = Quizzes
            .Select(quiz => quiz.Document)
            .OrderByDescending(quiz => quiz.IsPinned)
            .ThenByDescending(quiz => quiz.LastModified)
            .ThenBy(quiz => quiz.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var store = new QuizStoreData
        {
            Quizzes = documents,
            Groups = _quizGroupMetadata.Values
                .OrderBy(g => g.SortOrder ?? int.MaxValue)
                .ThenBy(g => g.Name)
                .ToList()
        };

        var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public NoteCardViewModel? FindNoteById(Guid noteId)
    {
        return Notes.FirstOrDefault(note => note.Document.Id == noteId);
    }

    private void ReorderQuizzes()
    {
        var ordered = Quizzes
            .OrderByDescending(quiz => quiz.Document.IsPinned)
            .ThenByDescending(quiz => quiz.Document.LastModified)
            .ThenBy(quiz => quiz.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Quizzes.Clear();
        foreach (var quiz in ordered)
            Quizzes.Add(quiz);
    }

    private void NotifyQuizzesChanged()
    {
        OnPropertyChanged(nameof(HasQuizzes));
        OnPropertyChanged(nameof(HasQuizGroups));
        OnPropertyChanged(nameof(HasUngroupedQuizzes));
        OnPropertyChanged(nameof(QuizCount));
        OnPropertyChanged(nameof(QuizCountText));
        OnPropertyChanged(nameof(HasRecentQuizzes));
    }

    private void NormalizeQuizGroups()
    {
        var grouped = Quizzes
            .Where(quiz => quiz.Document.GroupId.HasValue)
            .GroupBy(quiz => quiz.Document.GroupId!.Value)
            .ToList();

        foreach (var group in grouped.Where(group => group.Count() < 2))
        {
            foreach (var quiz in group)
            {
                quiz.Document.GroupId = null;
                quiz.NotifyChanged();
            }

            _quizGroupMetadata.Remove(group.Key);
        }
    }

    public void RebuildQuizGroups()
    {
        QuizGroups.Clear();

        var grouped = Quizzes
            .Where(quiz => quiz.Document.GroupId.HasValue)
            .GroupBy(quiz => quiz.Document.GroupId!.Value)
            .OrderBy(group => EnsureQuizGroupMetadata(group.Key).SortOrder ?? int.MaxValue)
            .ThenByDescending(group => group.Max(quiz => quiz.Document.LastModified))
            .ToList();

        foreach (var group in grouped)
        {
            var visibleQuizzes = group
                .Where(MatchesQuizFilters)
                .OrderByDescending(quiz => quiz.Document.IsPinned)
                .ThenByDescending(quiz => quiz.Document.LastModified)
                .ThenBy(quiz => quiz.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (visibleQuizzes.Count == 0)
                continue;

            var metadata = EnsureQuizGroupMetadata(group.Key);
            QuizGroups.Add(new QuizGroupViewModel(group.Key, metadata.Name, metadata.BackgroundColor, visibleQuizzes));
        }

        OnPropertyChanged(nameof(HasQuizGroups));
        OnPropertyChanged(nameof(HasUngroupedQuizzes));
    }

    private QuizGroupData EnsureQuizGroupMetadata(Guid groupId)
    {
        if (_quizGroupMetadata.TryGetValue(groupId, out var metadata))
        {
            metadata.SortOrder ??= GetNextQuizGroupSortOrder();
            return metadata;
        }

        metadata = new QuizGroupData
        {
            GroupId = groupId,
            Name = string.Format(LocalizationService.GetString("GroupTitleFormat"), groupId.ToString()[..4].ToUpperInvariant()),
            BackgroundColor = DefaultGroupBackground,
            SortOrder = GetNextQuizGroupSortOrder()
        };
        _quizGroupMetadata[groupId] = metadata;
        return metadata;
    }

    private int GetNextQuizGroupSortOrder()
    {
        return _quizGroupMetadata.Values
            .Select(group => group.SortOrder ?? -1)
            .DefaultIfEmpty(-1)
            .Max() + 1;
    }

    public bool TryGroupQuizzes(QuizViewModel draggedQuiz, QuizViewModel targetQuiz)
    {
        if (draggedQuiz is null || targetQuiz is null || ReferenceEquals(draggedQuiz, targetQuiz))
            return false;

        var targetGroupId = targetQuiz.Document.GroupId ?? draggedQuiz.Document.GroupId ?? Guid.NewGuid();
        EnsureQuizGroupMetadata(targetGroupId);

        var changed = false;
        foreach (var quiz in new[] { draggedQuiz, targetQuiz })
        {
            if (quiz.Document.GroupId == targetGroupId)
                continue;

            quiz.Document.GroupId = targetGroupId;
            quiz.NotifyChanged();
            changed = true;
        }

        if (!changed)
            return false;

        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
        return true;
    }

    public bool TryReorderQuizzesWithinGroup(QuizViewModel dragged, QuizViewModel target, bool placeAfter)
    {
        if (ReferenceEquals(dragged, target)) return false;
        var groupId = dragged.Document.GroupId;
        if (!groupId.HasValue || target.Document.GroupId != groupId) return false;

        var draggedIdx = Quizzes.IndexOf(dragged);
        var targetIdx = Quizzes.IndexOf(target);
        if (draggedIdx < 0 || targetIdx < 0) return false;

        var newIdx = placeAfter ? targetIdx + 1 : targetIdx;
        if (draggedIdx < newIdx) newIdx--;
        if (newIdx == draggedIdx) return false;

        Quizzes.Move(draggedIdx, Math.Clamp(newIdx, 0, Quizzes.Count - 1));
        RebuildQuizGroups();
        SaveQuizzes();
        return true;
    }

    public bool TryMoveQuizToGroup(QuizViewModel draggedQuiz, QuizGroupViewModel targetGroup)
    {
        if (draggedQuiz.Document.GroupId == targetGroup.GroupId)
            return false;

        draggedQuiz.Document.GroupId = targetGroup.GroupId;
        EnsureQuizGroupMetadata(targetGroup.GroupId);
        draggedQuiz.NotifyChanged();
        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
        return true;
    }

    public bool MoveQuizGroupUp(QuizGroupViewModel group)
    {
        return TryMoveQuizGroup(group, moveUp: true);
    }

    public bool MoveQuizGroupDown(QuizGroupViewModel group)
    {
        return TryMoveQuizGroup(group, moveUp: false);
    }

    private bool TryMoveQuizGroup(QuizGroupViewModel group, bool moveUp)
    {
        var currentIndex = QuizGroups.IndexOf(group);
        var targetIndex = moveUp ? currentIndex - 1 : currentIndex + 1;
        if (currentIndex < 0 || targetIndex < 0 || targetIndex >= QuizGroups.Count)
            return false;

        var target = QuizGroups[targetIndex];
        var currentMeta = EnsureQuizGroupMetadata(group.GroupId);
        var targetMeta = EnsureQuizGroupMetadata(target.GroupId);
        (currentMeta.SortOrder, targetMeta.SortOrder) = (targetMeta.SortOrder, currentMeta.SortOrder);
        QuizGroups.Move(currentIndex, targetIndex);
        SaveQuizzes();
        return true;
    }

    public bool RenameQuizGroup(QuizGroupViewModel group, string newName)
    {
        var trimmed = (newName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var metadata = EnsureQuizGroupMetadata(group.GroupId);
        metadata.Name = trimmed;
        group.Name = trimmed;
        SaveQuizzes();
        return true;
    }

    public bool SetQuizGroupBackgroundColor(QuizGroupViewModel group, string backgroundColor)
    {
        if (string.IsNullOrWhiteSpace(backgroundColor))
            return false;

        var metadata = EnsureQuizGroupMetadata(group.GroupId);
        metadata.BackgroundColor = backgroundColor;
        group.SetBackground(backgroundColor);
        SaveQuizzes();
        return true;
    }

    public Guid CreateQuizGroup(string groupName)
    {
        var trimmed = (groupName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return Guid.Empty;

        var groupId = Guid.NewGuid();
        var metadata = EnsureQuizGroupMetadata(groupId);
        metadata.Name = trimmed;
        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
        return groupId;
    }

    public bool CreateQuizGroupForQuiz(QuizViewModel quiz, string groupName)
    {
        if (quiz is null)
            return false;

        var trimmed = (groupName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var groupId = quiz.Document.GroupId ?? Guid.NewGuid();
        var metadata = EnsureQuizGroupMetadata(groupId);
        metadata.Name = trimmed;
        quiz.Document.GroupId = groupId;
        quiz.NotifyChanged();

        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
        return true;
    }

    public void DisbandQuizGroup(QuizGroupViewModel group, bool deleteQuizzes)
    {
        var quizzesInGroup = Quizzes.Where(quiz => quiz.Document.GroupId == group.GroupId).ToList();
        if (deleteQuizzes)
        {
            foreach (var quiz in quizzesInGroup)
                Quizzes.Remove(quiz);
        }
        else
        {
            foreach (var quiz in quizzesInGroup)
            {
                quiz.Document.GroupId = null;
                quiz.NotifyChanged();
            }
        }

        _quizGroupMetadata.Remove(group.GroupId);
        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
    }

    public void RemoveQuizFromGroup(QuizViewModel quiz)
    {
        if (quiz?.Document.GroupId is null)
            return;

        quiz.Document.GroupId = null;
        quiz.NotifyChanged();
        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
    }

    private static void NormalizeQuizDocument(QuizDocument document)
    {
        document.Id = document.Id == Guid.Empty ? Guid.NewGuid() : document.Id;
        document.Title = string.IsNullOrWhiteSpace(document.Title)
            ? LocalizationService.GetString("QuizUntitled")
            : document.Title.Trim();
        document.Tags = document.Tags?
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        document.Questions = document.Questions?
            .Where(question => question != null)
            .Select(NormalizeQuizQuestion)
            .Where(question => !string.IsNullOrWhiteSpace(question.Question) && question.Options.Count > 0)
            .ToList() ?? new List<QuizQuestion>();
        document.CreatedAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        document.LastModified = document.LastModified == default ? DateTime.Now : document.LastModified;
        document.AiModelDisplayName = document.AiModelDisplayName?.Trim() ?? string.Empty;
    }

    private static QuizQuestion NormalizeQuizQuestion(QuizQuestion question)
    {
        question.Question = question.Question?.Trim() ?? string.Empty;
        question.Explanation = question.Explanation?.Trim() ?? string.Empty;
        question.Options = question.Options?
            .Where(option => option != null && !string.IsNullOrWhiteSpace(option.Text))
            .Select(option => new QuizOption
            {
                Text = option.Text.Trim(),
                IsCorrect = option.IsCorrect
            })
            .ToList() ?? new List<QuizOption>();
        question.SetIndex = Math.Max(1, question.SetIndex);
        return question;
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
        document.LayoutMode = NormalizeMindMapLayoutMode(document.LayoutMode);
        document.UseManualPositions = document.UseManualPositions && HasAnyManualMindMapPosition(document.Root);
        document.CreatedAt = document.CreatedAt == default ? DateTime.UtcNow : document.CreatedAt;
        document.LastModified = document.LastModified == default ? DateTime.Now : document.LastModified;
        document.AiModelDisplayName = document.AiModelDisplayName?.Trim() ?? string.Empty;
    }

    private static string NormalizeMindMapLayoutMode(string? layoutMode)
    {
        return layoutMode?.Trim() switch
        {
            "Radial" => "Radial",
            "RightTree" => "RightTree",
            "LeftTree" => "LeftTree",
            "TopDown" => "TopDown",
            _ => "BalancedTree"
        };
    }

    private static void NormalizeMindMapNode(MindMapNode node)
    {
        node.Text = node.Text?.Trim() ?? string.Empty;
        if (node.ManualX is not { } x || node.ManualY is not { } y || !IsFinite(x) || !IsFinite(y))
        {
            node.ManualX = null;
            node.ManualY = null;
        }

        node.Children = node.Children?
            .Where(child => child != null)
            .ToList() ?? new List<MindMapNode>();

        foreach (var child in node.Children)
            NormalizeMindMapNode(child);
    }

    private static bool HasAnyManualMindMapPosition(MindMapNode node)
    {
        if (node.ManualX.HasValue && node.ManualY.HasValue)
            return true;

        return node.Children.Any(HasAnyManualMindMapPosition);
    }

    private static bool IsFinite(double value)
        => !double.IsNaN(value) && !double.IsInfinity(value);


    public ObservableCollection<NoteCardViewModel> Notes { get; }
    public ObservableCollection<NoteGroupViewModel> NoteGroups { get; }
    public ObservableCollection<TagFilterItemViewModel> TagFilters { get; }
    public ObservableCollection<TagFilterItemViewModel> FlashcardTagFilters { get; }
    public ObservableCollection<TagFilterItemViewModel> MindMapTagFilters { get; }
    public ObservableCollection<TagFilterItemViewModel> QuizTagFilters { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> SortOptions { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> FlashcardSortOptions { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> MindMapSortOptions { get; }
    public ObservableCollection<NoteSortOptionItemViewModel> QuizSortOptions { get; }
    public IEnumerable<NoteSortOptionItemViewModel> ActiveSortOptions => IsMindMapsView
        ? MindMapSortOptions
        : IsQuizzesView ? QuizSortOptions : IsFlashcardsView ? FlashcardSortOptions : SortOptions;
    public IEnumerable<TagFilterItemViewModel> ActiveTagFilters => IsMindMapsView
        ? MindMapTagFilters
        : IsQuizzesView ? QuizTagFilters : IsFlashcardsView ? FlashcardTagFilters : TagFilters;
    public ObservableCollection<CalendarScheduledItemViewModel> CalendarScheduledNotes { get; }
     public bool HasGroups => NoteGroups.Count > 0;
    public bool HasTagFilters => TagFilters.Count > 0;
    public bool HasFlashcardTagFilters => FlashcardTagFilters.Count > 0;
    public bool HasMindMapTagFilters => MindMapTagFilters.Count > 0;
    public bool HasQuizTagFilters => QuizTagFilters.Count > 0;
    public bool ActiveHasTagFilters => IsMindMapsView
        ? HasMindMapTagFilters
        : IsQuizzesView ? HasQuizTagFilters : IsFlashcardsView ? HasFlashcardTagFilters : HasTagFilters;
    public bool HasActiveTagFilters => _selectedTags.Count > 0;
    public bool HasActiveFlashcardTagFilters => _selectedFlashcardTags.Count > 0;
    public bool HasActiveMindMapTagFilters => _selectedMindMapTags.Count > 0;
    public bool HasActiveQuizTagFilters => _selectedQuizTags.Count > 0;
    public bool ActiveHasActiveTagFilters => IsMindMapsView
        ? HasActiveMindMapTagFilters
        : IsQuizzesView ? HasActiveQuizTagFilters : IsFlashcardsView ? HasActiveFlashcardTagFilters : HasActiveTagFilters;
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

            if (IsQuizzesView)
            {
                return HasActiveQuizTagFilters
                    ? $"{LocalizationService.GetString("FilterTags")} ({_selectedQuizTags.Count})"
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
        : IsQuizzesView
            ? string.Format(LocalizationService.GetString("SortButtonFormat"), GetQuizSortOptionDisplayName(_selectedQuizSortOptionKey))
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
    private readonly ICollectionView _quizzesView;
    public ICollectionView QuizzesView => _quizzesView;

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
    private string _quizSearchQuery = string.Empty;
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

    public string QuizSearchQuery
    {
        get => _quizSearchQuery;
        set
        {
            if (_quizSearchQuery == (value ?? string.Empty))
                return;

            _quizSearchQuery = value ?? string.Empty;
            OnPropertyChanged(nameof(QuizSearchQuery));
            OnPropertyChanged(nameof(ActiveSearchQuery));
            ApplyQuizFilters();
        }
    }

    public string ActiveSearchQuery
    {
        get => IsMindMapsView
            ? MindMapSearchQuery
            : IsQuizzesView ? QuizSearchQuery
            : IsFlashcardsView ? FlashcardSearchQuery : SearchQuery;
        set
        {
            if (IsMindMapsView)
                MindMapSearchQuery = value;
            else if (IsQuizzesView)
                QuizSearchQuery = value;
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
    public ICommand ShowQuizzesCommand { get; }
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
            Images = noteCard.Document.Images?
                .Select(image => new NoteImageAttachment
                {
                    Id = image.Id,
                    Data = image.Data,
                    Layout = image.Layout,
                    Width = image.Width,
                    Height = image.Height,
                    Left = image.Left,
                    Top = image.Top,
                    PreserveAspectRatio = image.PreserveAspectRatio
                })
                .ToList() ?? new List<NoteImageAttachment>(),
            Tags = noteCard.Document.Tags?.ToList() ?? new List<string>(), // Copy tags if they exist
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

    public void ToggleFlashcardSetPin(FlashcardSetViewModel set)
    {
        if (set is null)
            return;

        set.Document.IsPinned = !set.Document.IsPinned;
        set.NotifyChanged();
        ReorderFlashcardSets();
        NormalizeFlashcardSetGroups();
        RebuildFlashcardSetGroups();
        ApplyFlashcardFilters();
        SaveFlashcardSets();
        NotifyFlashcardSetsChanged();
    }

    public void ToggleMindMapPin(MindMapViewModel mindMap)
    {
        if (mindMap is null)
            return;

        mindMap.Document.IsPinned = !mindMap.Document.IsPinned;
        mindMap.NotifyChanged();
        ReorderMindMaps();
        NormalizeMindMapGroups();
        RebuildMindMapGroups();
        ApplyMindMapFilters();
        SaveMindMaps();
        NotifyMindMapsChanged();
    }

    public void ToggleQuizPin(QuizViewModel quiz)
    {
        if (quiz is null)
            return;

        quiz.Document.IsPinned = !quiz.Document.IsPinned;
        quiz.NotifyChanged();
        ReorderQuizzes();
        NormalizeQuizGroups();
        RebuildQuizGroups();
        ApplyQuizFilters();
        SaveQuizzes();
        NotifyQuizzesChanged();
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
    public ObservableCollection<FlashcardSetViewModel> RecentFlashcardSets { get; } = new();
    public ObservableCollection<MindMapViewModel> RecentMindMaps { get; } = new();
    public ObservableCollection<QuizViewModel> RecentQuizzes { get; } = new();
    public bool HasRecentFlashcardSets => RecentFlashcardSets.Count > 0;
    public bool HasRecentMindMaps => RecentMindMaps.Count > 0;
    public bool HasRecentQuizzes => RecentQuizzes.Count > 0;

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

    public void RefreshRecentFlashcardSets()
    {
        var recent = FlashcardSets
            .Where(MatchesFlashcardSetFilters)
            .OrderByDescending(set => set.Document.LastModified)
            .Take(RecentDashboardItemsLimit)
            .ToList();

        RecentFlashcardSets.Clear();
        foreach (var set in recent)
            RecentFlashcardSets.Add(set);

        OnPropertyChanged(nameof(HasRecentFlashcardSets));
    }

    public void RefreshRecentMindMaps()
    {
        var recent = MindMaps
            .Where(MatchesMindMapFilters)
            .OrderByDescending(map => map.Document.LastModified)
            .Take(RecentDashboardItemsLimit)
            .ToList();

        RecentMindMaps.Clear();
        foreach (var map in recent)
            RecentMindMaps.Add(map);

        OnPropertyChanged(nameof(HasRecentMindMaps));
    }

    public void RefreshRecentQuizzes()
    {
        var recent = Quizzes
            .Where(MatchesQuizFilters)
            .OrderByDescending(quiz => quiz.Document.LastModified)
            .Take(RecentDashboardItemsLimit)
            .ToList();

        RecentQuizzes.Clear();
        foreach (var quiz in recent)
            RecentQuizzes.Add(quiz);

        OnPropertyChanged(nameof(HasRecentQuizzes));
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

    public string SelectedQuizSortOptionKey
    {
        get => _selectedQuizSortOptionKey;
        set
        {
            var normalized = NormalizeQuizSortOptionKey(value);
            if (!SetProperty(ref _selectedQuizSortOptionKey, normalized))
                return;

            UpdateQuizSortOptionSelection();
            ApplySortToQuizzesView();
            OnPropertyChanged(nameof(ActiveSortButtonText));
            SaveAppSettings();
        }
    }

    public ObservableCollection<FlashcardSetViewModel> FlashcardSets { get; } = new();
    public bool HasFlashcardSets => FlashcardSets.Count > 0;
    public bool HasFlashcardSetGroups => FlashcardSetGroups.Count > 0;
    public bool HasUngroupedFlashcardSets => FlashcardSets.Any(set => !set.Document.GroupId.HasValue && MatchesFlashcardSetFilters(set));
    public int FlashcardSetCount => FlashcardSets.Count;
    public string FlashcardSetCountText => string.Format(LocalizationService.GetString("FlashcardSetCountFormat"), FlashcardSetCount);
    public ObservableCollection<MindMapViewModel> MindMaps { get; } = new();
    public bool HasMindMaps => MindMaps.Count > 0;
    public bool HasMindMapGroups => MindMapGroups.Count > 0;
    public bool HasUngroupedMindMaps => MindMaps.Any(map => !map.Document.GroupId.HasValue && MatchesMindMapFilters(map));
    public int MindMapCount => MindMaps.Count;
    public string MindMapCountText => string.Format(LocalizationService.GetString("MindMapCountFormat"), MindMapCount);
    public ObservableCollection<QuizViewModel> Quizzes => _quizzes;
    public bool HasQuizzes => Quizzes.Count > 0;
    public bool HasQuizGroups => QuizGroups.Count > 0;
    public bool HasUngroupedQuizzes => Quizzes.Any(quiz => !quiz.Document.GroupId.HasValue && MatchesQuizFilters(quiz));
    public int QuizCount => Quizzes.Count;
    public string QuizCountText => string.Format(LocalizationService.GetString("QuizCountFormat"), QuizCount);
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
        else if (IsQuizzesView)
            ClearQuizTagFilters();
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

    public void SetQuizTagFilterSelected(string tag, bool isSelected)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        if (isSelected)
            _selectedQuizTags.Add(tag);
        else
            _selectedQuizTags.Remove(tag);

        OnPropertyChanged(nameof(HasActiveQuizTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyQuizFilters();
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

    private void ClearQuizTagFilters()
    {
        if (_selectedQuizTags.Count == 0)
            return;

        _selectedQuizTags.Clear();
        foreach (var tag in QuizTagFilters)
            tag.IsSelected = false;

        OnPropertyChanged(nameof(HasActiveQuizTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
        ApplyQuizFilters();
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

    private void RefreshAvailableQuizTags()
    {
        var tags = Quizzes
            .SelectMany(quiz => quiz.Document.Tags ?? new List<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        _selectedQuizTags.RemoveWhere(selected => !tags.Any(tag => string.Equals(tag, selected, StringComparison.OrdinalIgnoreCase)));

        QuizTagFilters.Clear();
        foreach (var tag in tags)
        {
            var isSelected = _selectedQuizTags.Contains(tag);
            QuizTagFilters.Add(new TagFilterItemViewModel(tag, isSelected, SetQuizTagFilterSelected));
        }

        OnPropertyChanged(nameof(HasQuizTagFilters));
        OnPropertyChanged(nameof(ActiveHasTagFilters));
        OnPropertyChanged(nameof(HasActiveQuizTagFilters));
        OnPropertyChanged(nameof(ActiveHasActiveTagFilters));
        OnPropertyChanged(nameof(ActiveTagFilterButtonText));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyFlashcardFilters()
    {
        _flashcardSetsView.Refresh();
        RebuildFlashcardSetGroups();
        RefreshRecentFlashcardSets();
        NotifyFlashcardSetsChanged();
    }

    private void ApplyMindMapFilters()
    {
        _mindMapsView.Refresh();
        RebuildMindMapGroups();
        RefreshRecentMindMaps();
        NotifyMindMapsChanged();
    }

    private void ApplyQuizFilters()
    {
        _quizzesView.Refresh();
        RebuildQuizGroups();
        RefreshRecentQuizzes();
        NotifyQuizzesChanged();
    }

    private bool FilterFlashcardSet(object obj)
    {
        if (obj is not FlashcardSetViewModel set)
            return false;

        if (set.Document.GroupId.HasValue)
            return false;

        return MatchesFlashcardSetFilters(set);
    }

    private bool MatchesFlashcardSetFilters(FlashcardSetViewModel set)
    {
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

        if (map.Document.GroupId.HasValue)
            return false;

        return MatchesMindMapFilters(map);
    }

    private bool MatchesMindMapFilters(MindMapViewModel map)
    {
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

    private bool FilterQuiz(object obj)
    {
        if (obj is not QuizViewModel quiz)
            return false;

        if (quiz.Document.GroupId.HasValue)
            return false;

        return MatchesQuizFilters(quiz);
    }

    private bool MatchesQuizFilters(QuizViewModel quiz)
    {
        if (_selectedQuizTags.Count > 0)
        {
            var tags = quiz.Document.Tags ?? new List<string>();
            if (!tags.Any(tag => _selectedQuizTags.Contains(tag)))
                return false;
        }

        var query = QuizSearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return quiz.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || quiz.TagsDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
               || quiz.Document.Questions.Any(question =>
                   (question.Question ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                   || (question.Explanation ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)
                   || question.Options.Any(option => (option.Text ?? string.Empty).Contains(query, StringComparison.OrdinalIgnoreCase)));
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
        else if (IsQuizzesView)
            SelectedQuizSortOptionKey = key;
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

    private void RefreshQuizSortOptions()
    {
        var selectedKey = NormalizeQuizSortOptionKey(_selectedQuizSortOptionKey);
        _selectedQuizSortOptionKey = selectedKey;

        QuizSortOptions.Clear();
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedDesc, LocalizationService.GetString("SortByLastModifiedDesc"), selectedKey == SortLastModifiedDesc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortLastModifiedAsc, LocalizationService.GetString("SortByLastModifiedAsc"), selectedKey == SortLastModifiedAsc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtDesc, LocalizationService.GetString("SortByCreatedAtDesc"), selectedKey == SortCreatedAtDesc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortCreatedAtAsc, LocalizationService.GetString("SortByCreatedAtAsc"), selectedKey == SortCreatedAtAsc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleAsc, LocalizationService.GetString("SortByTitleAsc"), selectedKey == SortTitleAsc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortTitleDesc, LocalizationService.GetString("SortByTitleDesc"), selectedKey == SortTitleDesc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortQuizQuestionsDesc, LocalizationService.GetString("SortByQuizQuestionsDesc"), selectedKey == SortQuizQuestionsDesc, SetSortOptionSelected));
        QuizSortOptions.Add(new NoteSortOptionItemViewModel(SortQuizQuestionsAsc, LocalizationService.GetString("SortByQuizQuestionsAsc"), selectedKey == SortQuizQuestionsAsc, SetSortOptionSelected));
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

    private void UpdateQuizSortOptionSelection()
    {
        foreach (var option in QuizSortOptions)
            option.IsSelected = string.Equals(option.Key, _selectedQuizSortOptionKey, StringComparison.Ordinal);
    }

    private void ApplySortToFlashcardSetsView()
    {
        _flashcardSetsView.SortDescriptions.Clear();
        _flashcardSetsView.SortDescriptions.Add(new SortDescription("Document.IsPinned", ListSortDirection.Descending));

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
        _mindMapsView.SortDescriptions.Add(new SortDescription("Document.IsPinned", ListSortDirection.Descending));

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

    private void ApplySortToQuizzesView()
    {
        _quizzesView.SortDescriptions.Clear();
        _quizzesView.SortDescriptions.Add(new SortDescription("Document.IsPinned", ListSortDirection.Descending));

        switch (_selectedQuizSortOptionKey)
        {
            case SortLastModifiedAsc:
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Ascending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortCreatedAtDesc:
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortCreatedAtAsc:
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Ascending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                break;
            case SortTitleAsc:
                _quizzesView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortTitleDesc:
                _quizzesView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Descending));
                break;
            case SortQuizQuestionsAsc:
                _quizzesView.SortDescriptions.Add(new SortDescription("QuestionCount", ListSortDirection.Ascending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            case SortQuizQuestionsDesc:
                _quizzesView.SortDescriptions.Add(new SortDescription("QuestionCount", ListSortDirection.Descending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Title", ListSortDirection.Ascending));
                break;
            default:
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.LastModified", ListSortDirection.Descending));
                _quizzesView.SortDescriptions.Add(new SortDescription("Document.CreatedAt", ListSortDirection.Descending));
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

    private static string NormalizeQuizSortOptionKey(string? value)
    {
        if (string.Equals(value, SortQuizQuestionsDesc, StringComparison.OrdinalIgnoreCase))
            return SortQuizQuestionsDesc;
        if (string.Equals(value, SortQuizQuestionsAsc, StringComparison.OrdinalIgnoreCase))
            return SortQuizQuestionsAsc;

        return NormalizeSortOptionKey(value);
    }

    private static string NormalizeDashboard(string? dashboard)
    {
        if (string.Equals(dashboard, DashboardFlashcards, StringComparison.OrdinalIgnoreCase))
            return DashboardFlashcards;
        if (string.Equals(dashboard, DashboardMindMaps, StringComparison.OrdinalIgnoreCase))
            return DashboardMindMaps;
        if (string.Equals(dashboard, DashboardQuizzes, StringComparison.OrdinalIgnoreCase))
            return DashboardQuizzes;

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

    private static string GetQuizSortOptionDisplayName(string sortKey)
    {
        return NormalizeQuizSortOptionKey(sortKey) switch
        {
            SortQuizQuestionsDesc => LocalizationService.GetString("SortByQuizQuestionsDesc"),
            SortQuizQuestionsAsc => LocalizationService.GetString("SortByQuizQuestionsAsc"),
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

    public void SetFlashcardSetSchedules(FlashcardSetViewModel flashcardSet, IEnumerable<NoteScheduleEntry>? schedules)
    {
        if (flashcardSet is null)
            return;

        flashcardSet.Document.Schedules = NormalizeScheduleEntries(schedules);
        flashcardSet.Document.LastModified = DateTime.Now;
        flashcardSet.NotifyChanged();
        RefreshCalendarScheduledNotes();
        RefreshRecentFlashcardSets();
        SaveFlashcardSets();
    }

    public void SetMindMapSchedules(MindMapViewModel mindMap, IEnumerable<NoteScheduleEntry>? schedules)
    {
        if (mindMap is null)
            return;

        mindMap.Document.Schedules = NormalizeScheduleEntries(schedules);
        mindMap.Document.LastModified = DateTime.Now;
        mindMap.NotifyChanged();
        RefreshCalendarScheduledNotes();
        RefreshRecentMindMaps();
        SaveMindMaps();
    }

    public void SetQuizSchedules(QuizViewModel quiz, IEnumerable<NoteScheduleEntry>? schedules)
    {
        if (quiz is null)
            return;

        quiz.Document.Schedules = NormalizeScheduleEntries(schedules);
        quiz.Document.LastModified = DateTime.Now;
        quiz.NotifyChanged();
        RefreshCalendarScheduledNotes();
        RefreshRecentQuizzes();
        SaveQuizzes();
    }

    private void RefreshCalendarScheduledNotes()
    {
        var selected = CalendarSelectedDate.Date;

        var noteItems = Notes
            .Where(MatchesSearch)
            .SelectMany(note => GetEffectiveSchedules(note.Document).Select(entry => new CalendarScheduledItemViewModel
            {
                ItemType = ScheduledItemType.Note,
                Note = note,
                Title = note.Title,
                ScheduledAt = entry.ScheduledAt,
                ScheduleNote = entry.Note ?? string.Empty
            }))
            .Where(item => item.ScheduledAt.Date == selected);

        var flashcardItems = FlashcardSets
            .SelectMany(fs => (fs.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>()).Select(entry => new CalendarScheduledItemViewModel
            {
                ItemType = ScheduledItemType.Flashcard,
                FlashcardSet = fs,
                Title = fs.Title,
                ScheduledAt = entry.ScheduledAt,
                ScheduleNote = entry.Note ?? string.Empty
            }))
            .Where(item => item.ScheduledAt.Date == selected);

        var mindMapItems = MindMaps
            .SelectMany(mm => (mm.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>()).Select(entry => new CalendarScheduledItemViewModel
            {
                ItemType = ScheduledItemType.MindMap,
                MindMap = mm,
                Title = mm.Title,
                ScheduledAt = entry.ScheduledAt,
                ScheduleNote = entry.Note ?? string.Empty
            }))
            .Where(item => item.ScheduledAt.Date == selected);

        var quizItems = Quizzes
            .SelectMany(q => (q.Document.Schedules ?? Enumerable.Empty<NoteScheduleEntry>()).Select(entry => new CalendarScheduledItemViewModel
            {
                ItemType = ScheduledItemType.Quiz,
                Quiz = q,
                Title = q.Title,
                ScheduledAt = entry.ScheduledAt,
                ScheduleNote = entry.Note ?? string.Empty
            }))
            .Where(item => item.ScheduledAt.Date == selected);

        var items = noteItems
            .Concat(flashcardItems)
            .Concat(mindMapItems)
            .Concat(quizItems)
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

    public void RebuildMindMapGroups()
    {
        MindMapGroups.Clear();

        var grouped = MindMaps
            .Where(m => m.Document.GroupId.HasValue)
            .GroupBy(m => m.Document.GroupId!.Value)
            .OrderBy(g =>
            {
                if (_mindMapGroupMetadata.TryGetValue(g.Key, out var meta))
                    return meta.SortOrder ?? int.MaxValue;
                return int.MaxValue;
            })
            .ToList();

        foreach (var group in grouped)
        {
            if (!_mindMapGroupMetadata.TryGetValue(group.Key, out var meta))
            {
                meta = new MindMapGroupData { GroupId = group.Key, Name = string.Format(LocalizationService.GetString("GroupTitleFormat"), group.Key.ToString()[..4].ToUpperInvariant()), BackgroundColor = DefaultGroupBackground };
                _mindMapGroupMetadata[group.Key] = meta;
            }

            var visibleMaps = group
                .Where(MatchesMindMapFilters)
                .OrderByDescending(map => map.Document.IsPinned)
                .ThenByDescending(map => map.Document.LastModified)
                .ThenBy(map => map.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (visibleMaps.Count == 0)
                continue;

            MindMapGroups.Add(new MindMapGroupViewModel(group.Key, meta.Name, meta.BackgroundColor, visibleMaps));
        }

        OnPropertyChanged(nameof(MindMapGroups));
        OnPropertyChanged(nameof(HasMindMapGroups));
        OnPropertyChanged(nameof(HasUngroupedMindMaps));
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
        _selectedFlashcardSortOptionKey = NormalizeFlashcardSortOptionKey(settings.FlashcardSortOptionKey);
        _selectedMindMapSortOptionKey = NormalizeMindMapSortOptionKey(settings.MindMapSortOptionKey);
        _selectedQuizSortOptionKey = NormalizeQuizSortOptionKey(settings.QuizSortOptionKey);
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
        settings.FlashcardSortOptionKey = _selectedFlashcardSortOptionKey;
        settings.MindMapSortOptionKey = _selectedMindMapSortOptionKey;
        settings.QuizSortOptionKey = _selectedQuizSortOptionKey;
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

    public void RefreshAutoSaveSettings()
    {
        ApplyAutoSaveSettings(AppSettingsService.Load());
    }

    private void ApplyAutoSaveSettings(AppSettings settings)
    {
        if (!settings.EnableAutoSave)
        {
            StopAutoSaveTimer();
            return;
        }

        var intervalSeconds = Math.Clamp(settings.AutoSaveIntervalSeconds, 5, 86400);
        _autoSaveTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(intervalSeconds)
        };
        _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
        _autoSaveTimer.Start();
    }

    private void StopAutoSaveTimer()
    {
        if (_autoSaveTimer is null)
            return;

        _autoSaveTimer.Stop();
        _autoSaveTimer.Tick -= AutoSaveTimer_Tick;
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        SaveAllDashboardContent();
    }

    public void SaveAllDashboardContent()
    {
        SaveNotes();
        SaveFlashcardSets();
        SaveMindMaps();
        SaveQuizzes();
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
