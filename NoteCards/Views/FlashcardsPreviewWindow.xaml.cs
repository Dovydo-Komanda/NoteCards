using NoteCards.Models;
using NoteCards.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class FlashcardsPreviewWindow : Window
{
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
    private bool _isShuffleMode;
    private int _studyModeIndex;
    private int _studyHistoryPosition = -1;
    private int _currentSetIndex = DefaultSetIndex;
    private bool? _statusFilterIsKnown;
    private string _searchText = string.Empty;
    private string _modelDisplayName = string.Empty;

    public FlashcardsPreviewWindow(
        IEnumerable<FlashcardItem> items,
        string? modelDisplayName = null,
        string? title = null,
        IEnumerable<string>? tags = null)
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
        InitializeSetOptionsFromItems();
        ApplyStudyModeState();
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
            StudyModeNextButton_Click(StudyModeNextButton, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            StudyModePreviousButton_Click(StudyModePreviousButton, new RoutedEventArgs());
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
        if (!_isStudyMode || _items.Count == 0 || _studyModeIndex < 0 || _studyModeIndex >= _items.Count)
            return;

        if (StudyModeCard.DataContext is FlashcardPreviewItem item)
            ToggleCardFlipWithAnimation(StudyModeCard, item);
    }

    private void InitializeSetOptionsFromItems()
    {
        _setOptions.Clear();

        var setIndexes = _allItems
            .Select(item => item.SetIndex)
            .Distinct()
            .OrderBy(index => index)
            .DefaultIfEmpty(DefaultSetIndex)
            .ToList();

        foreach (var setIndex in setIndexes)
        {
            AddSetOption(setIndex, selectSet: false);
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

        var orderedItems = _isShuffleMode
            ? filteredItems.OrderBy(_ => _random.Next()).ToList()
            : filteredItems.ToList();

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

    private FlashcardPreviewItem? PickNextStudyItem(FlashcardPreviewItem? currentItem)
    {
        var candidates = _items.Where(item => !item.IsKnown).ToList();
        if (candidates.Count == 0)
            return null;

        if (currentItem is not null && candidates.Count > 1)
            candidates.Remove(currentItem);

        if (candidates.Count == 0)
            return currentItem;

        var totalWeight = 0;
        foreach (var candidate in candidates)
            totalWeight += candidate.IsUnknown ? 4 : 1;

        var roll = _random.Next(totalWeight);
        foreach (var candidate in candidates)
        {
            roll -= candidate.IsUnknown ? 4 : 1;
            if (roll < 0)
                return candidate;
        }

        return candidates[^1];
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

        _studyHistory.Add(item);
        _studyHistoryPosition = _studyHistory.Count - 1;
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

        var current = (_studyModeIndex >= 0 && _studyModeIndex < _items.Count)
            ? _items[_studyModeIndex]
            : null;

        var next = PickNextStudyItem(current);
        if (next is null)
            return false;

        MoveToStudyItem(next, appendToHistory: true);
        return true;
    }

    private void UpdateShuffleButtonState()
    {
        if (ShuffleModeButton is null)
            return;

        ShuffleModeButton.Content = LocalizationService.GetString(_isShuffleMode ? "ShuffleModeOn" : "ShuffleModeOff");
    }

    private void ShuffleModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isShuffleMode = !_isShuffleMode;
        UpdateShuffleButtonState();
        ApplyFilters();
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
    }

    private void MoveToSetMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem moveToSetMenu)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(moveToSetMenu);
        if (flashcard is null)
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

        var dialog = new EditFlashcardDialog
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
    }

    private void StartStudyModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        if (!_isStudyMode)
        {
            _studyModeIndex = 0;
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
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void FlashcardCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FlashcardPreviewItem item && sender is Border card)
            ToggleCardFlipWithAnimation(card, item);
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

    private static void ToggleCardFlipWithAnimation(Border card, FlashcardPreviewItem item)
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
        fadeOut.Completed += (_, _) =>
        {
            item.IsFlipped = !item.IsFlipped;
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
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
        var studiedCount = _studyHistory.Distinct().Count();
        var knownCount = _items.Count(i => i.IsKnown);
        var unknownCount = _items.Count(i => i.IsUnknown);
        var remainingCount = _items.Count - knownCount;

        UpdateStudyProgressLine(studiedCount, knownCount, unknownCount, _items.Count);

        // Show completion if all cards are known
        if (remainingCount == 0)
        {
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

        if (currentItem is null || currentItem.IsKnown)
        {
            if (!TryMoveToNextStudyItem(fromHistory: false))
            {
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

        StudyModeCard.DataContext = currentItem;

        StudyModePreviousButton.IsEnabled = _studyHistoryPosition > 0;
        StudyModeNextButton.IsEnabled = remainingCount > 0;
        StudyModeMarkKnownButton.IsEnabled = true;
        StudyModeMarkUnknownButton.IsEnabled = true;
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
        if (_items.Count == 0 || _studyModeIndex < 0 || _studyModeIndex >= _items.Count)
            return;

        var currentItem = _items[_studyModeIndex];
        currentItem.IsKnown = true;
        currentItem.IsUnknown = false; // Clear unknown status

        TryMoveToNextStudyItem(fromHistory: false);

        ApplyStudyModeState();
    }

    private void StudyModeMarkUnknownButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _studyModeIndex < 0 || _studyModeIndex >= _items.Count)
            return;

        var currentItem = _items[_studyModeIndex];
        currentItem.IsUnknown = !currentItem.IsUnknown; // Toggle
        currentItem.IsKnown = false; // Clear known status if marking unknown

        ApplyStudyModeState();
    }

    private void StudyModePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _studyHistoryPosition <= 0)
            return;

        _studyHistoryPosition--;
        MoveToStudyItem(_studyHistory[_studyHistoryPosition], appendToHistory: false);

        ApplyStudyModeState();
    }

    private void StudyModeNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        TryMoveToNextStudyItem(fromHistory: true);

        ApplyStudyModeState();
    }

    private void EditFlashcardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(menuItem);
        if (flashcard is null)
            return;

        var dialog = new EditFlashcardDialog
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
