using NoteCards.Localization;
using NoteCards.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NoteCards.Views;

public partial class QuizPreviewWindow : Window
{
    private static readonly Brush CorrectBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
    private static readonly Brush IncorrectBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private static readonly Brush UnansweredBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));

    private readonly ObservableCollection<QuizPreviewQuestion> _questions;
    private readonly QuizDocument _sourceDocument;
    private string _modelDisplayName = string.Empty;
    private bool _isSubmitted;

    public QuizPreviewWindow(QuizDocument document, string? modelDisplayName = null, string? title = null)
    {
        _sourceDocument = document;
        _questions = new ObservableCollection<QuizPreviewQuestion>(
            (document.Questions ?? [])
            .Select((question, index) => new QuizPreviewQuestion(index + 1, question)));

        InitializeComponent();

        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? document.Title
            : title;
        TitleTextBox.Text = string.IsNullOrWhiteSpace(displayTitle)
            ? LocalizationService.GetString("QuizUntitled")
            : displayTitle.Trim();
        QuestionsItemsControl.ItemsSource = _questions;
        ConfigureAiGeneratedIndicator(modelDisplayName ?? document.AiModelDisplayName);
        UpdateSummary();

        if (_questions.Any())
        {
            SetQuestionFocus(0);
        }

        PreviewKeyDown += QuizPreviewWindow_PreviewKeyDown;
    }

    public string EditorTitle => TitleTextBox.Text.Trim();

    public string AiModelDisplayName => _modelDisplayName;

    public QuizDocument ToDocument(QuizDocument? existingDocument = null)
    {
        return new QuizDocument
        {
            Id = existingDocument?.Id ?? _sourceDocument.Id,
            Title = string.IsNullOrWhiteSpace(EditorTitle)
                ? LocalizationService.GetString("QuizUntitled")
                : EditorTitle,
            Tags = existingDocument?.Tags?.ToList() ?? _sourceDocument.Tags?.ToList() ?? new List<string>(),
            Questions = _questions.Select(question => question.ToModel()).ToList(),
            CreatedAt = existingDocument?.CreatedAt ?? _sourceDocument.CreatedAt,
            LastModified = DateTime.Now,
            AiModelDisplayName = string.IsNullOrWhiteSpace(_modelDisplayName)
                ? existingDocument?.AiModelDisplayName ?? _sourceDocument.AiModelDisplayName
                : _modelDisplayName,
            SourceNoteId = existingDocument?.SourceNoteId ?? _sourceDocument.SourceNoteId
        };
    }

    private void OptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isSubmitted)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewOption option)
            return;

        option.Parent.SelectOption(option);
        UpdateSummary();
    }

    private void CheckAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        _isSubmitted = true;

        foreach (var question in _questions)
            question.Submit();

        UpdateSummary();
    }

    private void ResetAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        _isSubmitted = false;

        foreach (var question in _questions)
            question.Reset();

        UpdateSummary();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullscreen();
    }

    private void ToggleFullscreen()
    {
        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.CanResize;
        }
        else
        {
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.NoResize;
        }
    }

    private void QuizPreviewWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (WindowStyle == WindowStyle.None)
            {
                ToggleFullscreen();
                e.Handled = true;
            }
            else
            {
                Close();
            }
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (CheckAnswersButton.IsEnabled && CheckAnswersButton.Visibility == Visibility.Visible)
            {
                CheckAnswersButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (ResetAnswersButton.IsEnabled && ResetAnswersButton.Visibility == Visibility.Visible)
            {
                ResetAnswersButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Up)
        {
            int current = GetFocusedQuestionIndex();
            if (current > 0)
            {
                SetQuestionFocus(current - 1);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Down)
        {
            int current = GetFocusedQuestionIndex();
            if (current >= 0 && current < _questions.Count - 1)
            {
                SetQuestionFocus(current + 1);
                e.Handled = true;
            }
        }
        else if ((e.Key >= Key.D1 && e.Key <= Key.D9) || (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9))
        {
            int number = e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1;
            int current = GetFocusedQuestionIndex();
            if (current >= 0 && current < _questions.Count)
            {
                var question = _questions[current];
                if (number < question.Options.Count)
                {
                    question.SelectOption(question.Options[number]);
                    UpdateSummary();
                    e.Handled = true;
                }
            }
        }
    }

    private void ConfigureAiGeneratedIndicator(string? modelDisplayName)
    {
        _modelDisplayName = modelDisplayName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_modelDisplayName))
        {
            AiGeneratedInfoBadge.Visibility = Visibility.Collapsed;
            AiGeneratedInfoBadge.ToolTip = null;
            return;
        }

        AiGeneratedInfoBadge.Visibility = Visibility.Visible;
        AiGeneratedInfoBadge.ToolTip = string.Format(
            LocalizationService.GetString("QuizGeneratedWithModel"),
            _modelDisplayName);
    }

    private void UpdateSummary()
    {
        var questionCount = _questions.Count;
        var answeredCount = _questions.Count(question => question.HasSelection);
        QuestionCountTextBlock.Text = string.Format(LocalizationService.GetString("QuizQuestionCountFormat"), questionCount);
        AnsweredCountTextBlock.Text = string.Format(LocalizationService.GetString("QuizAnsweredCountFormat"), answeredCount, questionCount);

        CheckAnswersButton.IsEnabled = !_isSubmitted && questionCount > 0;
        ResetAnswersButton.IsEnabled = _isSubmitted || answeredCount > 0;

        if (!_isSubmitted)
        {
            ScoreBadge.Visibility = Visibility.Collapsed;
            ScoreTextBlock.Text = string.Empty;
            return;
        }

        var correctCount = _questions.Count(question => question.IsCorrect == true);
        var percent = questionCount == 0
            ? 0
            : (int)Math.Round((double)correctCount / questionCount * 100);

        ScoreBadge.Visibility = Visibility.Visible;
        ScoreTextBlock.Text = string.Format(LocalizationService.GetString("QuizScoreFormat"), correctCount, questionCount, percent);
    }

    private void SetQuestionFocus(int index)
    {
        if (index < 0 || index >= _questions.Count) return;

        for (int i = 0; i < _questions.Count; i++)
        {
            _questions[i].IsFocused = (i == index);
        }

        var container = QuestionsItemsControl.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement;
        container?.BringIntoView();
    }

    private int GetFocusedQuestionIndex()
    {
        for (int i = 0; i < _questions.Count; i++)
        {
            if (_questions[i].IsFocused) return i;
        }
        return -1;
    }

    private sealed class QuizPreviewQuestion : INotifyPropertyChanged
    {
        private readonly QuizQuestion _source;
        private bool _isSubmitted;
        private bool _isFocused;

        public QuizPreviewQuestion(int number, QuizQuestion source)
        {
            _source = source;
            Number = number;
            Type = source.Type;
            Question = source.Question;
            Explanation = source.Explanation;
            Options = new ObservableCollection<QuizPreviewOption>(
                source.Options.Select(option => new QuizPreviewOption(this, option.Text, option.IsCorrect)));
        }

        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Number { get; }

        public QuizQuestionType Type { get; }

        public string Question { get; }

        public string Explanation { get; }

        public ObservableCollection<QuizPreviewOption> Options { get; }

        public bool HasSelection => Options.Any(option => option.IsSelected);

        public bool IsSubmitted => _isSubmitted;

        public bool? IsCorrect { get; private set; }

        public string HeaderText => string.Format(
            LocalizationService.GetString("QuizQuestionHeaderFormat"),
            Number,
            Type switch
            {
                QuizQuestionType.TrueFalse => LocalizationService.GetString("QuizTypeTrueFalse"),
                QuizQuestionType.MultipleChoice => LocalizationService.GetString("QuizTypeMultipleChoice"),
                _ => LocalizationService.GetString("QuizTypeSingleChoice")
            });

        public bool HasResult => _isSubmitted;

        public string ResultText
        {
            get
            {
                if (!_isSubmitted)
                    return string.Empty;

                if (!HasSelection)
                    return LocalizationService.GetString("QuizUnanswered");

                return IsCorrect == true
                    ? LocalizationService.GetString("QuizCorrect")
                    : LocalizationService.GetString("QuizIncorrect");
            }
        }

        public Brush ResultBackground
        {
            get
            {
                if (!_isSubmitted || !HasSelection)
                    return UnansweredBrush;

                return IsCorrect == true ? CorrectBrush : IncorrectBrush;
            }
        }

        public bool HasVisibleExplanation => _isSubmitted && !string.IsNullOrWhiteSpace(Explanation);

        public string ExplanationText => string.Format(LocalizationService.GetString("QuizExplanationFormat"), Explanation);

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SelectOption(QuizPreviewOption option)
        {
            if (_isSubmitted)
                return;

            if (Type == QuizQuestionType.MultipleChoice)
            {
                option.IsSelected = !option.IsSelected;
                option.RefreshState();
            }
            else
            {
                foreach (var item in Options)
                {
                    item.IsSelected = ReferenceEquals(item, option);
                    item.RefreshState();
                }
            }

            OnPropertyChanged(nameof(HasSelection));
        }

        public void Submit()
        {
            _isSubmitted = true;
            var selectedOptions = Options.Where(option => option.IsSelected).ToList();
            var correctCount = Options.Count(option => option.IsCorrect);
            IsCorrect = selectedOptions.Count > 0
                && selectedOptions.Count == correctCount
                && selectedOptions.All(option => option.IsCorrect);
            RefreshState();
        }

        public void Reset()
        {
            _isSubmitted = false;
            IsCorrect = null;

            foreach (var option in Options)
            {
                option.IsSelected = false;
                option.RefreshState();
            }

            RefreshState();
        }

        public QuizQuestion ToModel()
        {
            return new QuizQuestion
            {
                Type = Type,
                Question = Question,
                Explanation = Explanation,
                SetIndex = Math.Max(1, _source.SetIndex),
                Options = Options
                    .Select(option => new QuizOption
                    {
                        Text = option.Text,
                        IsCorrect = option.IsCorrect
                    })
                    .ToList()
            };
        }

        private void RefreshState()
        {
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(IsSubmitted));
            OnPropertyChanged(nameof(IsCorrect));
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ResultText));
            OnPropertyChanged(nameof(ResultBackground));
            OnPropertyChanged(nameof(HasVisibleExplanation));
            OnPropertyChanged(nameof(ExplanationText));

            foreach (var option in Options)
                option.RefreshState();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private sealed class QuizPreviewOption : INotifyPropertyChanged
    {
        public QuizPreviewOption(QuizPreviewQuestion parent, string text, bool isCorrect)
        {
            Parent = parent;
            Text = text;
            IsCorrect = isCorrect;
        }

        public QuizPreviewQuestion Parent { get; }

        public string Text { get; }

        public bool IsCorrect { get; }

        public bool IsSelected { get; set; }

        public bool IsCorrectAfterSubmit => Parent.IsSubmitted && IsCorrect;

        public bool IsIncorrectSelectedAfterSubmit => Parent.IsSubmitted && IsSelected && !IsCorrect;

        public string MarkerText
        {
            get
            {
                if (Parent.IsSubmitted && IsCorrect)
                    return "✓";

                if (Parent.IsSubmitted && IsSelected && !IsCorrect)
                    return "×";

                if (Parent.Type == QuizQuestionType.MultipleChoice)
                    return IsSelected ? "☑" : "☐";

                return IsSelected ? "●" : "○";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void RefreshState()
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsCorrectAfterSubmit));
            OnPropertyChanged(nameof(IsIncorrectSelectedAfterSubmit));
            OnPropertyChanged(nameof(MarkerText));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
