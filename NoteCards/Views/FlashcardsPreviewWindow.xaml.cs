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
    private readonly ObservableCollection<FlashcardPreviewItem> _items;
    private bool _isStudyMode;
    private int _studyModeIndex;

    public FlashcardsPreviewWindow(IEnumerable<FlashcardItem> items, string? modelDisplayName = null)
    {
        InitializeComponent();
        _items = new ObservableCollection<FlashcardPreviewItem>(
            items.Select(i => new FlashcardPreviewItem(i.Question, i.Answer)));

        FlashcardsItemsControl.ItemsSource = _items;
        var modelName = string.IsNullOrWhiteSpace(modelDisplayName) ? LocalizationService.GetString("NotAvailable") : modelDisplayName;
        if (FindName("ModelInfoText") is TextBlock modelInfoText)
            modelInfoText.Text = string.Format(LocalizationService.GetString("FlashcardsGeneratedWithModel"), modelName);

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

    private void StudyModePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _studyModeIndex <= 0)
            return;

        _studyModeIndex--;
        ApplyStudyModeState();
    }

    private void StudyModeNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _studyModeIndex >= _items.Count - 1)
            return;

        _studyModeIndex++;
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

        if (_studyModeIndex < 0)
            _studyModeIndex = 0;
        if (_studyModeIndex >= _items.Count && _items.Count > 0)
            _studyModeIndex = _items.Count - 1;

        FlashcardsGridViewBorder.Visibility = _isStudyMode ? Visibility.Collapsed : Visibility.Visible;
        StudyModeViewBorder.Visibility = _isStudyMode ? Visibility.Visible : Visibility.Collapsed;
        StartStudyModeButton.Content = LocalizationService.GetString(_isStudyMode ? "StudyModeExit" : "StudyModeStart");

        if (!_isStudyMode || _items.Count == 0)
            return;

        StudyModeCard.DataContext = _items[_studyModeIndex];
        StudyModeProgressText.Text = string.Format(LocalizationService.GetString("StudyModeProgress"), _studyModeIndex + 1, _items.Count);
        StudyModePreviousButton.IsEnabled = _studyModeIndex > 0;
        StudyModeNextButton.IsEnabled = _studyModeIndex < _items.Count - 1;
    }

    private sealed class FlashcardPreviewItem : INotifyPropertyChanged
    {
        private bool _isFlipped;

        public FlashcardPreviewItem(string question, string answer)
        {
            Question = question;
            Answer = answer;
        }

        public string Question { get; }
        public string Answer { get; }

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

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
