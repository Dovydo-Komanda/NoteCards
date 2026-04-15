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
    private readonly List<FlashcardPreviewItem> _allItems;
    private readonly ObservableCollection<FlashcardPreviewItem> _items;
    private readonly ObservableCollection<FlashcardSetOption> _setOptions;
    private bool _isStudyMode;
    private int _studyModeIndex;

    public FlashcardsPreviewWindow(IEnumerable<FlashcardItem> items, string? modelDisplayName = null)
    {
        InitializeComponent();
        _allItems = items
            .Select(i => new FlashcardPreviewItem(i.Question, i.Answer, Math.Max(1, i.SetIndex)))
            .ToList();
        _items = new ObservableCollection<FlashcardPreviewItem>();
        _setOptions = new ObservableCollection<FlashcardSetOption>();

        SetSelectorComboBox.ItemsSource = _setOptions;
        FlashcardsItemsControl.ItemsSource = _items;
        var modelName = string.IsNullOrWhiteSpace(modelDisplayName) ? LocalizationService.GetString("NotAvailable") : modelDisplayName;
        if (FindName("ModelInfoText") is TextBlock modelInfoText)
            modelInfoText.Text = string.Format(LocalizationService.GetString("FlashcardsGeneratedWithModel"), modelName);

        InitializeSetOptions();
        ApplyStudyModeState();
    }

    private void InitializeSetOptions()
    {
        _setOptions.Clear();

        var setIndexes = _allItems
            .Select(item => item.SetIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToList();

        foreach (var setIndex in setIndexes)
        {
            _setOptions.Add(new FlashcardSetOption
            {
                SetIndex = setIndex,
                DisplayName = string.Format(LocalizationService.GetString("FlashcardSetFormat"), setIndex)
            });
        }

        SetSelectorComboBox.IsEnabled = _setOptions.Count > 1;

        if (_setOptions.Count == 0)
        {
            ApplySetFilter(1);
            return;
        }

        SetSelectorComboBox.SelectedIndex = 0;
    }

    private void SetSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SetSelectorComboBox.SelectedItem is not FlashcardSetOption set)
            return;

        ApplySetFilter(set.SetIndex);
    }

    private void ApplySetFilter(int setIndex)
    {
        var normalizedSetIndex = Math.Max(1, setIndex);

        _items.Clear();
        foreach (var item in _allItems.Where(item => item.SetIndex == normalizedSetIndex))
            _items.Add(item);

        _studyModeIndex = 0;
        ApplyStudyModeState();
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

        public FlashcardPreviewItem(string question, string answer, int setIndex)
        {
            Question = question;
            Answer = answer;
            SetIndex = setIndex;
        }

        public string Question { get; }
        public string Answer { get; }
        public int SetIndex { get; }

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

    private sealed class FlashcardSetOption
    {
        public int SetIndex { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
