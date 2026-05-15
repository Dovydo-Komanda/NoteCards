using NoteCards.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace NoteCards.Views;

public partial class QuizAttemptHistoryWindow : Window
{
    public class AttemptDisplayItem
    {
        public QuizAttempt Attempt { get; set; } = null!;
        public string QuizTitle { get; set; } = string.Empty;
        public string DateDisplay { get; set; } = string.Empty;
        public bool Passed { get; set; }
    }

    private readonly List<QuizAttempt> _allAttempts;

    public QuizAttemptHistoryWindow()
    {
        InitializeComponent();
        NoteCards.Services.WindowThemeService.Register(this);
        _allAttempts = QuizDocument.SavedAttempts;
        RefreshList();
    }

    private void RefreshList()
    {
        var items = _allAttempts
            .OrderByDescending(a => a.Date)
            .Select(a => new AttemptDisplayItem
            {
                Attempt = a,
                QuizTitle = string.IsNullOrWhiteSpace(a.QuizTitle) ? "Untitled Quiz" : a.QuizTitle,
                DateDisplay = a.Date.ToString("yyyy-MM-dd  HH:mm") +
                              $"   —   {a.CorrectCount}/{a.TotalQuestions}  ({a.Percentage:F1}%)" +
                              $"   ⏱ {a.TimeTaken:hh\\:mm\\:ss}",
                Passed = a.Percentage >= a.PassingScorePercent
            })
            .ToList();

        AttemptsItemsControl.ItemsSource = items;
    }

    private void AttemptCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Reserved for future detail view
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear all saved attempt history?",
            "Clear History",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        _allAttempts.Clear();
        RefreshList();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
