using NoteCards.Models;
using NoteCards.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class FlashcardsPreviewWindow : Window
{
    private enum UnsavedCloseDecision
    {
        Cancel,
        LeaveWithoutSaving,
        SaveAndClose
    }

    private const int DefaultSetIndex = 1;
    private const double FlashcardSearchExpandedWidth = 260;
    private const int FlashcardSearchAnimationMs = 240;
    private readonly List<FlashcardPreviewItem> _allItems;
    private readonly ObservableCollection<FlashcardPreviewItem> _items;
    private readonly ObservableCollection<FlashcardSetOption> _setOptions;
    private readonly ObservableCollection<FlashcardStatusFilterOption> _statusFilterOptions;
    private readonly List<FlashcardPreviewItem> _studyHistory = new();
    private readonly Random _random = new();
    private bool _isStudyMode;
    private bool _isStudyModeCardAnimating;
    private int _studyModeIndex;
    private int _studyHistoryPosition = -1;
    private int _currentSetIndex = DefaultSetIndex;
    private bool? _statusFilterIsKnown;
    private string _searchText = string.Empty;
    private string _modelDisplayName = string.Empty;
    private string _lastSavedSnapshot = string.Empty;
    private bool _isInitializing = true;
    private bool _allowCloseWithoutPrompt;

    public FlashcardsPreviewWindow(
        IEnumerable<FlashcardItem> items,
        string? modelDisplayName = null,
        string? title = null,
        IEnumerable<string>? tags = null,
        IReadOnlyDictionary<int, string>? setNames = null)
    {
        InitializeComponent();
        _allItems = items
            .Select(i => new FlashcardPreviewItem(i.Question, i.Answer, Math.Max(DefaultSetIndex, i.SetIndex), i.Category))
            .ToList();
        _items = new ObservableCollection<FlashcardPreviewItem>();
        _setOptions = new ObservableCollection<FlashcardSetOption>();
        _statusFilterOptions = new ObservableCollection<FlashcardStatusFilterOption>();

        SetSelectorComboBox.ItemsSource = _setOptions;
        StatusFilterComboBox.ItemsSource = _statusFilterOptions;
        FlashcardsItemsControl.ItemsSource = _items;
        TitleTextBox.Text = string.IsNullOrWhiteSpace(title)
            ? LocalizationService.GetString("FlashcardsEditorTitle")
            : title.Trim();
        TagsTextBox.Text = tags is null
            ? string.Empty
            : string.Join(", ", tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()));
        ConfigureAiGeneratedIndicator(modelDisplayName);

        UpdateShuffleButtonState();
        InitializeStatusFilterOptions();
        InitializeSetOptionsFromItems(setNames);
        ApplyStudyModeState();
        _isInitializing = false;
        MarkCurrentStateSaved();
        PreviewKeyDown += FlashcardsPreviewWindow_PreviewKeyDown;
    }

    public string EditorTitle => TitleTextBox.Text.Trim();

    public IReadOnlyList<string> Tags => ParseTags(TagsTextBox.Text);

    public string AiModelDisplayName => _modelDisplayName;

    public FlashcardSetDocument ToDocument(FlashcardSetDocument? existingDocument = null)
    {
        return new FlashcardSetDocument
        {
            Id = existingDocument?.Id ?? Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(EditorTitle)
                ? LocalizationService.GetString("FlashcardSetUntitled")
                : EditorTitle,
            Tags = Tags.ToList(),
            SetNames = _setOptions
                .Where(option => !string.IsNullOrWhiteSpace(option.DisplayName))
                .ToDictionary(option => option.SetIndex, option => option.DisplayName.Trim()),
            Cards = GetFlashcardItems().ToList(),
            CreatedAt = existingDocument?.CreatedAt ?? DateTime.UtcNow,
            LastModified = DateTime.Now,
            AiModelDisplayName = string.IsNullOrWhiteSpace(_modelDisplayName)
                ? existingDocument?.AiModelDisplayName ?? string.Empty
                : _modelDisplayName
        };
    }

    public IReadOnlyList<FlashcardItem> GetFlashcardItems()
    {
        return _allItems
            .Select(item => new FlashcardItem
            {
                Question = item.Question,
                Answer = item.Answer,
                Category = item.Category,
                SetIndex = Math.Max(DefaultSetIndex, item.SetIndex)
            })
            .ToList();
    }

    private void EditorField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditedIndicator();
    }

    private void MarkCurrentStateSaved()
    {
        _lastSavedSnapshot = GetEditorSnapshot();
        UpdateEditedIndicator();
    }

    private bool HasUnsavedChanges()
    {
        if (_isInitializing)
            return false;

        return !string.Equals(GetEditorSnapshot(), _lastSavedSnapshot, StringComparison.Ordinal);
    }

    private void UpdateEditedIndicator()
    {
        if (_isInitializing || EditedIndicatorText is null)
            return;

        EditedIndicatorText.Visibility = HasUnsavedChanges()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private string GetEditorSnapshot()
    {
        var setSnapshot = string.Join(
            '\u001E',
            _setOptions
                .OrderBy(option => option.SetIndex)
                .Select(option => $"{option.SetIndex}\u001D{option.DisplayName.Trim()}"));

        var cardSnapshot = string.Join(
            '\u001E',
            _allItems.Select(item =>
                $"{item.SetIndex}\u001D{item.Question}\u001D{item.Answer}\u001D{item.Category}"));

        return string.Join(
            '\u001F',
            TitleTextBox.Text,
            string.Join('\u001E', Tags),
            setSnapshot,
            cardSnapshot);
    }

    private UnsavedCloseDecision GetCloseDecision()
    {
        if (!HasUnsavedChanges())
            return UnsavedCloseDecision.LeaveWithoutSaving;

        var documentTitle = ResolveEditorTitleForPrompt();
        var dialog = new DeleteConfirmationDialog(
            LocalizationService.GetString("UnsavedChanges"),
            string.Format(LocalizationService.GetString("UnsavedChangesConfirmationFormat"), documentTitle),
            LocalizationService.GetString("LeaveWithoutSaving"),
            LocalizationService.GetString("Cancel"),
            LocalizationService.GetString("SaveAndExit"))
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return UnsavedCloseDecision.Cancel;

        return dialog.SelectedAction switch
        {
            DeleteConfirmationDialog.ConfirmationAction.Confirm => UnsavedCloseDecision.LeaveWithoutSaving,
            DeleteConfirmationDialog.ConfirmationAction.Secondary => UnsavedCloseDecision.SaveAndClose,
            _ => UnsavedCloseDecision.Cancel
        };
    }

    private string ResolveEditorTitleForPrompt()
    {
        var title = EditorTitle;
        return string.IsNullOrWhiteSpace(title)
            ? LocalizationService.GetString("FlashcardSetUntitled")
            : title;
    }

    private void SaveAndClose()
    {
        MarkCurrentStateSaved();
        _allowCloseWithoutPrompt = true;
        DialogResult = true;
        Close();
    }

    private void ConfigureAiGeneratedIndicator(string? modelDisplayName)
    {
        _modelDisplayName = modelDisplayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(modelDisplayName))
        {
            AiGeneratedInfoBadge.Visibility = Visibility.Collapsed;
            AiGeneratedInfoBadge.ToolTip = null;
            return;
        }

        AiGeneratedInfoBadge.Visibility = Visibility.Visible;
        AiGeneratedInfoBadge.ToolTip = string.Format(
            LocalizationService.GetString("FlashcardsGeneratedWithModel"),
            modelDisplayName.Trim());
    }

    private static IReadOnlyList<string> ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return Array.Empty<string>();

        return rawTags
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void InitializeStatusFilterOptions()
    {
        _statusFilterOptions.Clear();
        _statusFilterOptions.Add(new FlashcardStatusFilterOption
        {
            IsKnown = null,
            DisplayName = LocalizationService.GetString("FlashcardStatusAll")
        });
        _statusFilterOptions.Add(new FlashcardStatusFilterOption
        {
            IsKnown = true,
            DisplayName = LocalizationService.GetString("Known")
        });
        _statusFilterOptions.Add(new FlashcardStatusFilterOption
        {
            IsKnown = false,
            DisplayName = LocalizationService.GetString("Unknown")
        });

        StatusFilterComboBox.SelectedIndex = 0;
    }

    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusFilterComboBox.SelectedItem is not FlashcardStatusFilterOption option)
            return;

        _statusFilterIsKnown = option.IsKnown;
        ApplyFilters();
    }

    private void FlashcardsPreviewWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_isStudyMode || _items.Count == 0)
            return;

        if (e.OriginalSource is TextBox)
            return;

        if (e.Key == Key.Right || e.Key == Key.N)
        {
            MoveToNextStudyCardWithAnimation();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            MoveToPreviousStudyCardWithAnimation();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space || e.Key == Key.F)
        {
            FlipCurrentStudyCard();
            e.Handled = true;
        }
    }

    private void FlipCurrentStudyCard()
    {
        if (!_isStudyMode
            || _isStudyModeCardAnimating
            || _items.Count == 0
            || _items.All(item => item.IsKnown)
            || _studyModeIndex < 0
            || _studyModeIndex >= _items.Count
            || StudyModeCard.Visibility != Visibility.Visible
            || !StudyModeCard.IsHitTestVisible)
        {
            return;
        }

        if (StudyModeCard.DataContext is FlashcardPreviewItem item)
            ToggleCardFlipWithAnimation(StudyModeCard, item);
    }

    private void InitializeSetOptionsFromItems(IReadOnlyDictionary<int, string>? setNames)
    {
        _setOptions.Clear();

        var setIndexes = _allItems
            .Select(item => item.SetIndex)
            .Concat(setNames?.Keys ?? Enumerable.Empty<int>())
            .Where(index => index >= DefaultSetIndex)
            .Distinct()
            .OrderBy(index => index)
            .DefaultIfEmpty(DefaultSetIndex)
            .ToList();

        foreach (var setIndex in setIndexes)
        {
            string? displayName = null;
            setNames?.TryGetValue(setIndex, out displayName);
            AddSetOption(setIndex, displayName, selectSet: false);
        }

        SetSelectorComboBox.SelectedItem = _setOptions
            .OrderBy(option => option.SetIndex)
            .FirstOrDefault();
    }

    private void SetSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SetSelectorComboBox.SelectedItem is not FlashcardSetOption set)
            return;

        ApplySetFilter(set.SetIndex);
    }

    private void ApplySetFilter(int setIndex)
    {
        _currentSetIndex = Math.Max(DefaultSetIndex, setIndex);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var normalizedQuery = (_searchText ?? string.Empty).Trim();

        var filteredItems = _allItems.Where(item => item.SetIndex == _currentSetIndex);
        if (_statusFilterIsKnown.HasValue)
        {
            filteredItems = _statusFilterIsKnown.Value
                ? filteredItems.Where(item => item.IsKnown)
                : filteredItems.Where(item => item.IsUnknown);
        }

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filteredItems = filteredItems.Where(item =>
                item.Question.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.Answer.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        }

        var orderedItems = filteredItems.ToList();

        _items.Clear();
        foreach (var item in orderedItems)
            _items.Add(item);

        _studyModeIndex = 0;
        ResetStudyHistory();
        ApplyStudyModeState();
    }

    private void ResetStudyHistory()
    {
        _studyHistory.Clear();
        _studyHistoryPosition = -1;
    }

    private void MoveToStudyItem(FlashcardPreviewItem item, bool appendToHistory)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
            return;

        _studyModeIndex = index;

        if (!appendToHistory)
            return;

        if (_studyHistoryPosition < _studyHistory.Count - 1)
            _studyHistory.RemoveRange(_studyHistoryPosition + 1, _studyHistory.Count - _studyHistoryPosition - 1);

        if (_studyHistory.Count == 0 || !ReferenceEquals(_studyHistory[^1], item))
            _studyHistory.Add(item);

        _studyHistoryPosition = _studyHistory.Count - 1;
    }

    private void EnsureCurrentStudyItemInHistory(FlashcardPreviewItem item)
    {
        if (_studyHistoryPosition >= 0
            && _studyHistoryPosition < _studyHistory.Count
            && ReferenceEquals(_studyHistory[_studyHistoryPosition], item))
        {
            return;
        }

        if (_studyHistoryPosition < _studyHistory.Count - 1)
            _studyHistory.RemoveRange(_studyHistoryPosition + 1, _studyHistory.Count - _studyHistoryPosition - 1);

        if (_studyHistory.Count == 0 || !ReferenceEquals(_studyHistory[^1], item))
            _studyHistory.Add(item);

        _studyHistoryPosition = _studyHistory.Count - 1;
    }

    private bool TryFindNextStudyIndex(int currentIndex, out int nextIndex)
    {
        nextIndex = -1;

        if (_items.Count == 0)
            return false;

        var normalizedCurrentIndex = currentIndex >= 0 && currentIndex < _items.Count
            ? currentIndex
            : -1;

        for (var offset = 1; offset <= _items.Count; offset++)
        {
            var candidateIndex = (normalizedCurrentIndex + offset) % _items.Count;
            if (candidateIndex == normalizedCurrentIndex)
                break;

            if (_items[candidateIndex].IsKnown)
                continue;

            nextIndex = candidateIndex;
            return true;
        }

        return false;
    }

    private bool CanMoveToNextStudyItem(bool fromHistory)
    {
        if (_items.Count == 0)
            return false;

        if (fromHistory && _studyHistoryPosition < _studyHistory.Count - 1)
            return true;

        return TryFindNextStudyIndex(_studyModeIndex, out _);
    }

    private bool TryMoveToNextStudyItem(bool fromHistory)
    {
        if (_items.Count == 0)
            return false;

        if (fromHistory && _studyHistoryPosition < _studyHistory.Count - 1)
        {
            _studyHistoryPosition++;
            MoveToStudyItem(_studyHistory[_studyHistoryPosition], appendToHistory: false);
            return true;
        }

        if (!TryFindNextStudyIndex(_studyModeIndex, out var nextIndex))
            return false;

        MoveToStudyItem(_items[nextIndex], appendToHistory: true);
        return true;
    }

    private void UpdateShuffleButtonState()
    {
        if (ShuffleModeButton is null)
            return;

        ShuffleModeButton.Content = LocalizationService.GetString("ShuffleCards");
    }

    private void ShuffleModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count <= 1)
            return;

        var shuffled = _items.OrderBy(_ => _random.Next()).ToList();
        _items.Clear();
        foreach (var item in shuffled)
            _items.Add(item);

        _studyModeIndex = 0;
        ResetStudyHistory();
        ApplyStudyModeState();
    }

    private void FlashcardSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = FlashcardSearchTextBox.Text ?? string.Empty;
        ApplyFilters();
    }

    private void FlashcardSearchToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (FlashcardSearchPanel.Visibility == Visibility.Visible)
        {
            CollapseFlashcardSearchPanel();
            return;
        }

        ExpandFlashcardSearchPanel();
    }

    private void FlashcardSearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        CollapseFlashcardSearchPanel();
        e.Handled = true;
    }

    private void ExpandFlashcardSearchPanel()
    {
        FlashcardSearchPanel.Visibility = Visibility.Visible;
        FlashcardSearchPanel.IsHitTestVisible = true;
        FlashcardSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
        FlashcardSearchPanel.BeginAnimation(OpacityProperty, null);

        FlashcardSearchPanel.Width = 0;
        FlashcardSearchPanel.Opacity = 0;

        var duration = TimeSpan.FromMilliseconds(FlashcardSearchAnimationMs);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        FlashcardSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(0, FlashcardSearchExpandedWidth, duration)
        {
            EasingFunction = easeOut
        });
        FlashcardSearchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easeOut
        });

        Dispatcher.BeginInvoke(() =>
        {
            FlashcardSearchTextBox.Focus();
            FlashcardSearchTextBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CollapseFlashcardSearchPanel()
    {
        if (FlashcardSearchPanel.Visibility != Visibility.Visible)
            return;

        var startWidth = FlashcardSearchPanel.ActualWidth > 0
            ? FlashcardSearchPanel.ActualWidth
            : Math.Max(FlashcardSearchPanel.Width, 1);
        var startOpacity = FlashcardSearchPanel.Opacity;

        if (startOpacity <= 0)
            startOpacity = 1;

        var duration = TimeSpan.FromMilliseconds(FlashcardSearchAnimationMs);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var widthAnimation = new DoubleAnimation(startWidth, 0, duration)
        {
            EasingFunction = easeIn
        };
        widthAnimation.Completed += (_, _) =>
        {
            FlashcardSearchPanel.Visibility = Visibility.Collapsed;
            FlashcardSearchPanel.IsHitTestVisible = false;
            FlashcardSearchPanel.Width = 0;
            FlashcardSearchPanel.Opacity = 0;
        };

        FlashcardSearchPanel.BeginAnimation(FrameworkElement.WidthProperty, widthAnimation);
        FlashcardSearchPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(startOpacity, 0, duration)
        {
            EasingFunction = easeIn
        });
    }

    private int? GetSelectedSetIndex()
    {
        return SetSelectorComboBox.SelectedItem is FlashcardSetOption set
            ? set.SetIndex
            : null;
    }

    private int GetNextSetIndex()
    {
        return _setOptions.Count == 0
            ? DefaultSetIndex
            : _setOptions.Max(option => option.SetIndex) + 1;
    }

    private void UpdateSetSelectorState()
    {
        SetSelectorComboBox.IsEnabled = _setOptions.Count > 0;
    }

    private void RenameSetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SetSelectorComboBox.SelectedItem is not FlashcardSetOption selectedSet)
            return;

        var dialog = new SimpleInputDialog(
            LocalizationService.GetString("RenameFlashcardSet"),
            LocalizationService.GetString("RenameFlashcardSetPrompt"),
            selectedSet.DisplayName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        var newName = dialog.InputText.Trim();
        if (string.IsNullOrWhiteSpace(newName))
            return;

        selectedSet.DisplayName = newName;
        ReorderSetOptions();
        UpdateEditedIndicator();
    }

    private void DeleteSetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SetSelectorComboBox.SelectedItem is not FlashcardSetOption selectedSet)
            return;

        var dialog = new DeleteConfirmationDialog(
            LocalizationService.GetString("DeleteFlashcardSet"),
            string.Format(LocalizationService.GetString("DeleteFlashcardSetConfirmationFormat"), selectedSet.DisplayName))
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
            return;

        for (var i = _allItems.Count - 1; i >= 0; i--)
        {
            if (_allItems[i].SetIndex == selectedSet.SetIndex)
                _allItems.RemoveAt(i);
        }

        _setOptions.Remove(selectedSet);
        if (_setOptions.Count > 0)
            SetSelectorComboBox.SelectedItem = _setOptions.OrderBy(option => option.SetIndex).First();
        else
            _currentSetIndex = DefaultSetIndex;

        UpdateSetSelectorState();
        ApplyFilters();
        UpdateEditedIndicator();
    }

    private FlashcardSetOption AddSetOption(int setIndex, string? displayName = null, bool selectSet = false)
    {
        var normalizedSetIndex = Math.Max(DefaultSetIndex, setIndex);
        var existing = _setOptions.FirstOrDefault(option => option.SetIndex == normalizedSetIndex);
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(displayName))
                existing.DisplayName = displayName;

            if (selectSet)
                SetSelectorComboBox.SelectedItem = existing;

            return existing;
        }

        var option = new FlashcardSetOption
        {
            SetIndex = normalizedSetIndex,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? string.Format(LocalizationService.GetString("FlashcardSetFormat"), normalizedSetIndex)
                : displayName.Trim()
        };

        _setOptions.Add(option);
        ReorderSetOptions();

        if (selectSet)
            SetSelectorComboBox.SelectedItem = _setOptions.FirstOrDefault(o => o.SetIndex == option.SetIndex);

        UpdateSetSelectorState();
        return option;
    }

    private void ReorderSetOptions()
    {
        var selectedSetIndex = GetSelectedSetIndex();
        var ordered = _setOptions.OrderBy(option => option.SetIndex).ToList();
        _setOptions.Clear();

        foreach (var option in ordered)
            _setOptions.Add(option);

        if (selectedSetIndex.HasValue)
            SetSelectorComboBox.SelectedItem = _setOptions.FirstOrDefault(option => option.SetIndex == selectedSetIndex.Value);

        if (SetSelectorComboBox.SelectedItem is null && _setOptions.Count > 0)
            SetSelectorComboBox.SelectedIndex = 0;
    }

    private bool TryCreateSet(out FlashcardSetOption createdSet, bool selectSet)
    {
        var nextSetIndex = GetNextSetIndex();
        var defaultName = string.Format(LocalizationService.GetString("FlashcardSetFormat"), nextSetIndex);

        var dialog = new SimpleInputDialog(
            LocalizationService.GetString("CreateFlashcardSetTitle"),
            LocalizationService.GetString("CreateFlashcardSetPrompt"),
            defaultName)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            createdSet = null!;
            return false;
        }

        var setName = string.IsNullOrWhiteSpace(dialog.InputText)
            ? defaultName
            : dialog.InputText.Trim();

        createdSet = AddSetOption(nextSetIndex, setName, selectSet);
        UpdateEditedIndicator();
        return true;
    }

    private void CreateSetButton_Click(object sender, RoutedEventArgs e)
    {
        TryCreateSet(out _, selectSet: true);
    }

    private void AddFlashcardButton_Click(object sender, RoutedEventArgs e)
    {
        CreateFlashcardInSet(GetSelectedSetIndex() ?? _currentSetIndex);
    }

    private static FlashcardPreviewItem? ResolveFlashcardFromMenuItem(MenuItem menuItem)
    {
        if (menuItem.DataContext is FlashcardPreviewItem flashcard)
            return flashcard;

        if (menuItem.Parent is MenuItem parentMenuItem)
            return ResolveFlashcardFromMenuItem(parentMenuItem);

        if (menuItem.Parent is ContextMenu { PlacementTarget: FrameworkElement placementTarget }
            && placementTarget.DataContext is FlashcardPreviewItem placementFlashcard)
        {
            return placementFlashcard;
        }

        return null;
    }

    private void MoveFlashcardToSet(FlashcardPreviewItem flashcard, int targetSetIndex)
    {
        var normalizedSetIndex = Math.Max(DefaultSetIndex, targetSetIndex);
        if (flashcard.SetIndex == normalizedSetIndex)
            return;

        var selectedSetIndex = GetSelectedSetIndex();
        flashcard.SetIndex = normalizedSetIndex;

        if (selectedSetIndex.HasValue)
            ApplySetFilter(selectedSetIndex.Value);

        UpdateEditedIndicator();
    }

    private void ShowMoveToSetPicker(FlashcardPreviewItem flashcard)
    {
        var pickerMenu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        foreach (var setOption in _setOptions.OrderBy(option => option.SetIndex))
        {
            var targetSetIndex = setOption.SetIndex;
            var setItem = new MenuItem
            {
                Header = setOption.DisplayName,
                IsEnabled = flashcard.SetIndex != targetSetIndex
            };

            setItem.Click += (_, _) => MoveFlashcardToSet(flashcard, targetSetIndex);
            pickerMenu.Items.Add(setItem);
        }

        pickerMenu.Items.Add(new Separator());

        var createAndMoveItem = new MenuItem
        {
            Header = LocalizationService.GetString("CreateFlashcardSetAndMove")
        };
        createAndMoveItem.Click += (_, _) =>
        {
            if (!TryCreateSet(out var createdSet, selectSet: false))
                return;

            MoveFlashcardToSet(flashcard, createdSet.SetIndex);
        };

        pickerMenu.Items.Add(createAndMoveItem);
        pickerMenu.IsOpen = true;
    }

    private void MoveToSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem moveToSetMenu)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(moveToSetMenu);
        if (flashcard is null)
            return;

        ShowMoveToSetPicker(flashcard);
        e.Handled = true;
    }

    private void AddFlashcardToSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem addToSetMenu)
            return;

        var pickerMenu = new ContextMenu
        {
            Placement = PlacementMode.MousePoint
        };

        foreach (var setOption in _setOptions.OrderBy(option => option.SetIndex))
        {
            var targetSetIndex = setOption.SetIndex;
            var setItem = new MenuItem
            {
                Header = setOption.DisplayName
            };

            setItem.Click += (_, _) => CreateFlashcardInSet(targetSetIndex);
            pickerMenu.Items.Add(setItem);
        }

        pickerMenu.Items.Add(new Separator());

        var createSetAndAddItem = new MenuItem
        {
            Header = LocalizationService.GetString("CreateFlashcardSetAndAdd")
        };
        createSetAndAddItem.Click += (_, _) =>
        {
            if (!TryCreateSet(out var createdSet, selectSet: false))
                return;

            CreateFlashcardInSet(createdSet.SetIndex);
        };

        pickerMenu.Items.Add(createSetAndAddItem);
        pickerMenu.IsOpen = true;
        e.Handled = true;
    }

    private void CreateFlashcardInSet(int targetSetIndex)
    {
        var normalizedSetIndex = Math.Max(DefaultSetIndex, targetSetIndex);
        AddSetOption(normalizedSetIndex, selectSet: false);

        var dialog = new EditFlashcardDialog(isNew: true)
        {
            Owner = this,
            Question = string.Empty,
            Answer = string.Empty,
            Category = string.Empty
        };

        if (dialog.ShowDialog() != true)
            return;

        var flashcard = new FlashcardPreviewItem(dialog.Question, dialog.Answer, normalizedSetIndex, dialog.Category);
        _allItems.Add(flashcard);

        var selectedSetIndex = GetSelectedSetIndex() ?? _currentSetIndex;
        ApplySetFilter(selectedSetIndex);
        UpdateEditedIndicator();
    }

    private void StartStudyModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        if (!_isStudyMode)
        {
            _studyModeIndex = Math.Max(0, _items.ToList().FindIndex(item => !item.IsKnown));
            ResetStudyHistory();
        }

        _isStudyMode = !_isStudyMode;
        ApplyStudyModeState();
    }

    private void StudyModeCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        FlipCurrentStudyCard();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        switch (GetCloseDecision())
        {
            case UnsavedCloseDecision.Cancel:
                return;
            case UnsavedCloseDecision.SaveAndClose:
                SaveAndClose();
                return;
            case UnsavedCloseDecision.LeaveWithoutSaving:
                _allowCloseWithoutPrompt = true;
                Close();
                return;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndClose();
    }

    private void FlashcardsPreviewWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowCloseWithoutPrompt)
            return;

        switch (GetCloseDecision())
        {
            case UnsavedCloseDecision.Cancel:
                e.Cancel = true;
                return;
            case UnsavedCloseDecision.SaveAndClose:
                _allowCloseWithoutPrompt = true;
                MarkCurrentStateSaved();
                DialogResult = true;
                e.Cancel = false;
                return;
            case UnsavedCloseDecision.LeaveWithoutSaving:
                _allowCloseWithoutPrompt = true;
                return;
        }
    }

    private void FlashcardCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FlashcardPreviewItem item && sender is Border card)
            ToggleCardFlipWithAnimation(card, item);
    }

    private void MoveFlashcardButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FlashcardPreviewItem flashcard)
            ShowMoveToSetPicker(flashcard);

        e.Handled = true;
    }

    private void MoveStudyModeFlashcardButton_Click(object sender, RoutedEventArgs e)
    {
        if (StudyModeCard.DataContext is FlashcardPreviewItem flashcard)
            ShowMoveToSetPicker(flashcard);

        e.Handled = true;
    }

    private void EditFlashcardButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FlashcardPreviewItem flashcard)
            EditFlashcard(flashcard);

        e.Handled = true;
    }

    private void ShowQuestionsButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsFlipped = false;
    }

    private void ShowAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsFlipped = true;
    }

    private int GetFlipAnimationHalfDurationMs()
    {
        if (FlipSpeedSlider is null)
            return 150;

        return Math.Max(50, (int)Math.Round(FlipSpeedSlider.Value / 2));
    }

    private void ToggleCardFlipWithAnimation(Border card, FlashcardPreviewItem item)
    {
        var halfDuration = TimeSpan.FromMilliseconds(GetFlipAnimationHalfDurationMs());
        var fadeOut = new DoubleAnimation(1, 0, halfDuration);
        fadeOut.Completed += (_, _) =>
        {
            item.IsFlipped = !item.IsFlipped;
            var fadeIn = new DoubleAnimation(0, 1, halfDuration);
            card.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        card.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void ApplyStudyModeState()
    {
        if (_items.Count == 0)
        {
            _isStudyMode = false;
            _studyModeIndex = 0;
            ResetStudyHistory();
        }

        // Ensure index is within bounds
        if (_studyModeIndex < 0)
            _studyModeIndex = 0;
        if (_studyModeIndex >= _items.Count)
            _studyModeIndex = Math.Max(0, _items.Count - 1);

        FlashcardsGridViewBorder.Visibility = _isStudyMode ? Visibility.Collapsed : Visibility.Visible;
        StudyModeViewBorder.Visibility = _isStudyMode ? Visibility.Visible : Visibility.Collapsed;
        StartStudyModeButton.IsEnabled = _items.Count > 0;
        StartStudyModeButton.Content = LocalizationService.GetString(_isStudyMode ? "StudyModeExit" : "StudyModeStart");

        if (!_isStudyMode || _items.Count == 0)
            return;

        // Count progress and status breakdown for the current study session
        var knownCount = _items.Count(i => i.IsKnown);
        var unknownCount = _items.Count(i => i.IsUnknown);
        var remainingCount = _items.Count - knownCount;

        SetStudyModeCardInteractive(true);

        // Show completion if all cards are known
        if (remainingCount == 0)
        {
            UpdateStudyProgressLine(_studyHistory.Distinct().Count(), knownCount, unknownCount, _items.Count);
            SetStudyModeCardInteractive(false);
            StudyModeProgressText.Text = string.Format(
                LocalizationService.GetString("StudyModeProgress"),
                _items.Count,
                _items.Count);
            StudyModeCompletionText.Text = LocalizationService.GetString("StudyModeComplete");
            StudyModeCompletionText.Visibility = Visibility.Visible;
            StudyModePreviousButton.IsEnabled = false;
            StudyModeNextButton.IsEnabled = false;
            StudyModeMarkKnownButton.IsEnabled = false;
            StudyModeMarkUnknownButton.IsEnabled = false;
            return;
        }
        else
        {
            StudyModeCompletionText.Visibility = Visibility.Collapsed;
        }

        var currentItem = (_studyModeIndex >= 0 && _studyModeIndex < _items.Count)
            ? _items[_studyModeIndex]
            : null;

        if (currentItem is null)
        {
            if (!TryMoveToNextStudyItem(fromHistory: false))
            {
                UpdateStudyProgressLine(_studyHistory.Distinct().Count(), knownCount, unknownCount, _items.Count);
                SetStudyModeCardInteractive(false);
                StudyModeProgressText.Text = string.Format(
                    LocalizationService.GetString("StudyModeProgress"),
                    _items.Count,
                    _items.Count);
                StudyModeCompletionText.Text = LocalizationService.GetString("StudyModeComplete");
                StudyModeCompletionText.Visibility = Visibility.Visible;
                StudyModePreviousButton.IsEnabled = _studyHistoryPosition > 0;
                StudyModeNextButton.IsEnabled = false;
                StudyModeMarkKnownButton.IsEnabled = false;
                StudyModeMarkUnknownButton.IsEnabled = false;
                return;
            }

            currentItem = _items[_studyModeIndex];
        }

        EnsureCurrentStudyItemInHistory(currentItem);
        UpdateStudyProgressLine(_studyHistory.Distinct().Count(), knownCount, unknownCount, _items.Count);

        StudyModeCard.DataContext = currentItem;
        StudyModeProgressText.Text = string.Format(
            LocalizationService.GetString("StudyModeProgress"),
            _studyModeIndex + 1,
            _items.Count);

        StudyModePreviousButton.IsEnabled = !_isStudyModeCardAnimating && _studyHistoryPosition > 0;
        StudyModeNextButton.IsEnabled = !_isStudyModeCardAnimating && CanMoveToNextStudyItem(fromHistory: true);
        StudyModeMarkKnownButton.IsEnabled = !_isStudyModeCardAnimating;
        StudyModeMarkUnknownButton.IsEnabled = !_isStudyModeCardAnimating;
    }

    private void SetStudyModeCardInteractive(bool isInteractive)
    {
        StudyModeCard.Visibility = isInteractive ? Visibility.Visible : Visibility.Hidden;
        StudyModeCard.IsHitTestVisible = isInteractive;
        StudyModeCard.Opacity = isInteractive ? 1 : 0;
        StudyModeCard.Cursor = isInteractive ? Cursors.Hand : Cursors.Arrow;
    }

    private void UpdateStudyProgressLine(int studiedCount, int knownCount, int unknownCount, int totalCount)
    {
        if (StudyModeProgressTrack is null || StudyModeProgressFill is null)
            return;

        var tooltipText = string.Format(
            "Studied: {0} | Known: {1} | Unknown: {2}",
            studiedCount,
            knownCount,
            unknownCount);

        StudyModeProgressTrack.ToolTip = tooltipText;

        if (totalCount <= 0)
        {
            StudyModeProgressFill.Width = 0;
            return;
        }

        if (StudyModeProgressTrack.ActualWidth <= 0)
        {
            Dispatcher.BeginInvoke(() => UpdateStudyProgressLine(studiedCount, knownCount, unknownCount, totalCount), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        StudyModeProgressFill.Width = StudyModeProgressTrack.ActualWidth * Math.Clamp((double)knownCount / totalCount, 0, 1);
    }

    private sealed class FlashcardPreviewItem : INotifyPropertyChanged
    {
        private bool _isFlipped;
        private bool _isKnown;
        private bool _isUnknown;
        private string _question = string.Empty;
        private string _answer = string.Empty;
        private string _category = string.Empty;
        private int _setIndex;

        public FlashcardPreviewItem(string question, string answer, int setIndex, string? category = null)
        {
            Question = question;
            Answer = answer;
            SetIndex = setIndex;
            Category = category ?? string.Empty;
        }

        public string Question
        {
            get => _question;
            set
            {
                if (_question != value)
                {
                    _question = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Answer
        {
            get => _answer;
            set
            {
                if (_answer != value)
                {
                    _answer = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SetIndex
        {
            get => _setIndex;
            set
            {
                if (_setIndex == value)
                    return;

                _setIndex = Math.Max(DefaultSetIndex, value);
                OnPropertyChanged();
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                var normalized = value?.Trim() ?? string.Empty;
                if (_category == normalized)
                    return;

                _category = normalized;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCategory));
            }
        }

        public bool HasCategory => !string.IsNullOrWhiteSpace(_category);

        public bool IsFlipped
        {
            get => _isFlipped;
            set
            {
                if (_isFlipped == value)
                    return;
                _isFlipped = value;
                OnPropertyChanged();
            }
        }

        public bool IsKnown
        {
            get => _isKnown;
            set
            {
                if (_isKnown == value)
                    return;
                _isKnown = value;
                OnPropertyChanged();
            }
        }

        public bool IsUnknown
        {
            get => _isUnknown;
            set
            {
                if (_isUnknown == value)
                    return;
                _isUnknown = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private void StudyModeMarkKnownButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isStudyModeCardAnimating || _items.Count == 0 || _studyModeIndex < 0 || _studyModeIndex >= _items.Count)
            return;

        var currentItem = _items[_studyModeIndex];
        currentItem.IsKnown = true;
        currentItem.IsUnknown = false; // Clear unknown status

        AnimateStudyModeCardChange(1, () => TryMoveToNextStudyItem(fromHistory: false));
    }

    private void StudyModeMarkUnknownButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isStudyModeCardAnimating || _items.Count == 0 || _studyModeIndex < 0 || _studyModeIndex >= _items.Count)
            return;

        var currentItem = _items[_studyModeIndex];
        currentItem.IsUnknown = !currentItem.IsUnknown; // Toggle
        currentItem.IsKnown = false; // Clear known status if marking unknown

        ApplyStudyModeState();
    }

    private void StudyModePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        MoveToPreviousStudyCardWithAnimation();
    }

    private void MoveToPreviousStudyCardWithAnimation()
    {
        if (_items.Count == 0 || _studyHistoryPosition <= 0)
            return;

        AnimateStudyModeCardChange(-1, () =>
        {
            _studyHistoryPosition--;
            MoveToStudyItem(_studyHistory[_studyHistoryPosition], appendToHistory: false);
            return true;
        });
    }

    private void StudyModeNextButton_Click(object sender, RoutedEventArgs e)
    {
        MoveToNextStudyCardWithAnimation();
    }

    private void MoveToNextStudyCardWithAnimation()
    {
        if (_items.Count == 0)
            return;

        AnimateStudyModeCardChange(1, () => TryMoveToNextStudyItem(fromHistory: true));
    }

    private void AnimateStudyModeCardChange(int direction, Func<bool> moveCard)
    {
        if (_isStudyModeCardAnimating)
            return;

        _isStudyModeCardAnimating = true;
        StudyModePreviousButton.IsEnabled = false;
        StudyModeNextButton.IsEnabled = false;
        StudyModeMarkKnownButton.IsEnabled = false;
        StudyModeMarkUnknownButton.IsEnabled = false;

        if (StudyModeCard.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            StudyModeCard.RenderTransform = translate;
        }

        var exitOffset = direction >= 0 ? -26 : 26;
        var enterOffset = direction >= 0 ? 26 : -26;
        var fadeOutDuration = TimeSpan.FromMilliseconds(120);
        var fadeInDuration = TimeSpan.FromMilliseconds(170);
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

        var fadeOut = new DoubleAnimation(StudyModeCard.Opacity, 0, fadeOutDuration) { EasingFunction = easeIn };
        fadeOut.Completed += (_, _) =>
        {
            var moved = moveCard();
            ApplyStudyModeState();

            if (!moved
                || !_isStudyMode
                || StudyModeViewBorder.Visibility != Visibility.Visible
                || StudyModeCard.Visibility != Visibility.Visible)
            {
                StudyModeCard.Opacity = 1;
                translate.X = 0;
                _isStudyModeCardAnimating = false;
                ApplyStudyModeState();
                return;
            }

            translate.X = enterOffset;
            StudyModeCard.Opacity = 0;

            var fadeIn = new DoubleAnimation(0, 1, fadeInDuration) { EasingFunction = easeOut };
            fadeIn.Completed += (_, _) =>
            {
                _isStudyModeCardAnimating = false;
                StudyModeCard.Opacity = 1;
                translate.X = 0;
                ApplyStudyModeState();
            };

            StudyModeCard.BeginAnimation(OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(enterOffset, 0, fadeInDuration) { EasingFunction = easeOut });
        };

        StudyModeCard.BeginAnimation(OpacityProperty, fadeOut);
        translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, exitOffset, fadeOutDuration) { EasingFunction = easeIn });
    }

    private void EditFlashcardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(menuItem);
        if (flashcard is null)
            return;

        EditFlashcard(flashcard);
    }

    private void EditFlashcard(FlashcardPreviewItem flashcard)
    {
        var dialog = new EditFlashcardDialog()
        {
            Owner = this,
            Question = flashcard.Question,
            Answer = flashcard.Answer,
            Category = flashcard.Category
        };

        var result = dialog.ShowDialog();

        if (result == true)
        {
            // Directly update the flashcard
            flashcard.Question = dialog.Question;
            flashcard.Answer = dialog.Answer;
            flashcard.Category = dialog.Category;
            UpdateEditedIndicator();
        }
    }

    // ? Delete flashcard menu item handler
    private void DeleteFlashcardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(menuItem);
        if (flashcard is null)
            return;

        // Show confirmation dialog
        var result = MessageBox.Show(
            LocalizationService.GetString("DeleteFlashcardConfirmation"),
            LocalizationService.GetString("DeleteFlashcard"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            return;

        // Remove from all items
        _items.Remove(flashcard);

        _allItems.Remove(flashcard);

        for (var i = _studyHistory.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_studyHistory[i], flashcard))
                continue;

            _studyHistory.RemoveAt(i);
            if (_studyHistoryPosition >= i)
                _studyHistoryPosition--;
        }

        if (_studyHistoryPosition < -1)
            _studyHistoryPosition = -1;

        // Adjust study mode index if needed
        if (_studyModeIndex >= _items.Count)
            _studyModeIndex = Math.Max(0, _items.Count - 1);

        ApplyStudyModeState();
        UpdateEditedIndicator();
    }

    private sealed class FlashcardSetOption
    {
        public int SetIndex { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class FlashcardStatusFilterOption
    {
        public bool? IsKnown { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
