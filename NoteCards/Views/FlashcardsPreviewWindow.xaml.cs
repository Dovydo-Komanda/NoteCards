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

    public FlashcardsPreviewWindow(IEnumerable<FlashcardItem> items, string? modelDisplayName = null)
    {
        InitializeComponent();
        _items = new ObservableCollection<FlashcardPreviewItem>(
            items.Select(i => new FlashcardPreviewItem(i.Question, i.Answer)));

        FlashcardsItemsControl.ItemsSource = _items;
        var modelName = string.IsNullOrWhiteSpace(modelDisplayName) ? LocalizationService.GetString("NotAvailable") : modelDisplayName;
        if (FindName("ModelInfoText") is TextBlock modelInfoText)
            modelInfoText.Text = string.Format(LocalizationService.GetString("FlashcardsGeneratedWithModel"), modelName);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FlashcardCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FlashcardPreviewItem item)
        {
            if (sender is Border card)
            {
                // Fade out
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150));
                fadeOut.Completed += (_, _) =>
                {
                    // Toggle the flip in the middle of the animation
                    item.IsFlipped = !item.IsFlipped;

                    // Fade back in
                    var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
                    card.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                };
                card.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
            else
            {
                item.IsFlipped = !item.IsFlipped;
            }
        }
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
