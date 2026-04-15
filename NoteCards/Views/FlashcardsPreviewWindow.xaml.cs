using NoteCards.Models;
using NoteCards.Localization;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
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
    private readonly Random _random = new();
    private bool _isStudyMode;
    private bool _isShuffleMode;
    private int _studyModeIndex;
    private int _currentSetIndex = DefaultSetIndex;
    private string _searchText = string.Empty;

    public FlashcardsPreviewWindow(IEnumerable<FlashcardItem> items, string? modelDisplayName = null)
    {
        InitializeComponent();
        _allItems = items
            .Select(i => new FlashcardPreviewItem(i.Question, i.Answer, Math.Max(DefaultSetIndex, i.SetIndex)))
            .ToList();
        _items = new ObservableCollection<FlashcardPreviewItem>();
        _setOptions = new ObservableCollection<FlashcardSetOption>();

        SetSelectorComboBox.ItemsSource = _setOptions;
        FlashcardsItemsControl.ItemsSource = _items;
        var modelName = string.IsNullOrWhiteSpace(modelDisplayName) ? LocalizationService.GetString("NotAvailable") : modelDisplayName;
        if (FindName("ModelInfoText") is TextBlock modelInfoText)
            modelInfoText.Text = string.Format(LocalizationService.GetString("FlashcardsGeneratedWithModel"), modelName);

        UpdateShuffleButtonState();
        InitializeSetOptionsFromItems();
        ApplyStudyModeState();
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
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filteredItems = filteredItems.Where(item =>
                item.Question.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || item.Answer.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        }

        var orderedItems = _isShuffleMode
            ? filteredItems.OrderBy(_ => _random.Next()).ToList()
            : filteredItems.ToList();

        _items.Clear();
        foreach (var item in orderedItems)
            _items.Add(item);

        _studyModeIndex = 0;
        ApplyStudyModeState();
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

    private static FlashcardPreviewItem? ResolveFlashcardFromMenuItem(MenuItem menuItem)
    {
        if (menuItem.DataContext is FlashcardPreviewItem flashcard)
            return flashcard;

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

    private void MoveToSetMenuItem_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem moveToSetMenu)
            return;

        var flashcard = ResolveFlashcardFromMenuItem(moveToSetMenu);
        if (flashcard is null)
            return;

        moveToSetMenu.Items.Clear();

        foreach (var setOption in _setOptions.OrderBy(option => option.SetIndex))
        {
            var targetSetIndex = setOption.SetIndex;
            var setItem = new MenuItem
            {
                Header = setOption.DisplayName,
                IsEnabled = flashcard.SetIndex != targetSetIndex
            };

            setItem.Click += (_, _) => MoveFlashcardToSet(flashcard, targetSetIndex);
            moveToSetMenu.Items.Add(setItem);
        }

        moveToSetMenu.Items.Add(new Separator());

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

        moveToSetMenu.Items.Add(createAndMoveItem);
    }

    private void StartStudyModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0)
            return;

        if (!_isStudyMode)
            _studyModeIndex = 0;

        _isStudyMode = !_isStudyMode;
        ApplyStudyModeState();
    }

    private void StudyModeCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (StudyModeCard.DataContext is FlashcardPreviewItem item)
            ToggleCardFlipWithAnimation(StudyModeCard, item);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
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

        // Get current card
        var currentItem = _items[_studyModeIndex];
        StudyModeCard.DataContext = currentItem;

        // Count unknown and known cards for progress
        var knownCount = _items.Count(i => i.IsKnown);
        var remainingCount = _items.Count - knownCount;

        // Show completion if all cards are known
        if (remainingCount == 0)
        {
            StudyModeProgressText.Text = LocalizationService.GetString("StudyModeComplete");
            StudyModePreviousButton.IsEnabled = false;
            StudyModeNextButton.IsEnabled = false;
            StudyModeMarkKnownButton.IsEnabled = false;
            StudyModeMarkUnknownButton.IsEnabled = false;
            return;
        }

        StudyModeProgressText.Text = string.Format(
            LocalizationService.GetString("StudyModeProgress"),
            _items.Count(i => !i.IsKnown && _items.IndexOf(i) <= _studyModeIndex),
            remainingCount);

        StudyModePreviousButton.IsEnabled = _studyModeIndex > 0;
        StudyModeNextButton.IsEnabled = _studyModeIndex < _items.Count - 1;
        StudyModeMarkKnownButton.IsEnabled = true;
        StudyModeMarkUnknownButton.IsEnabled = true;
    }

    private sealed class FlashcardPreviewItem : INotifyPropertyChanged
    {
        private bool _isFlipped;
        private bool _isKnown;
        private bool _isUnknown;
        private string _question = string.Empty;
        private string _answer = string.Empty;
        private int _setIndex;

        public FlashcardPreviewItem(string question, string answer, int setIndex)
        {
            Question = question;
            Answer = answer;
            SetIndex = setIndex;
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

        // Move to next card
        if (_studyModeIndex < _items.Count - 1)
        {
            _studyModeIndex++;
            // Skip known cards when advancing
            while (_studyModeIndex < _items.Count && _items[_studyModeIndex].IsKnown)
            {
                _studyModeIndex++;
            }
        }

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
        if (_items.Count == 0 || _studyModeIndex <= 0)
            return;

        _studyModeIndex--;

        while (_studyModeIndex >= 0 && _items[_studyModeIndex].IsKnown)
        {
            _studyModeIndex--;
        }

        ApplyStudyModeState();
    }

    private void StudyModeNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _studyModeIndex >= _items.Count - 1)
            return;

        _studyModeIndex++;

        while (_studyModeIndex < _items.Count && _items[_studyModeIndex].IsKnown)
        {
            _studyModeIndex++;
        }

        ApplyStudyModeState();
    }

    private async void EditFlashcardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FlashcardPreviewItem flashcard)
            return;

        var dialog = new EditFlashcardDialog
        {
            Owner = this,
            Question = flashcard.Question,
            Answer = flashcard.Answer
        };

        var result = dialog.ShowDialog();

        if (result == true)
        {
            // Directly update the flashcard
            flashcard.Question = dialog.Question;
            flashcard.Answer = dialog.Answer;
        }
    }

    // ? Delete flashcard menu item handler
    private async void DeleteFlashcardMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.DataContext is not FlashcardPreviewItem flashcard)
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
}
