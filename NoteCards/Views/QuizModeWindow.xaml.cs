using NoteCards.Localization;
using NoteCards.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NoteCards.Views;

public partial class QuizModeWindow : Window
{
    private static readonly Color CorrectAnswerBackgroundColor = Color.FromRgb(240, 253, 244);
    private static readonly Color CorrectAnswerBorderColor = Color.FromRgb(16, 185, 129);
    private static readonly Color IncorrectAnswerBackgroundColor = Color.FromRgb(254, 242, 242);
    private static readonly Color IncorrectAnswerBorderColor = Color.FromRgb(220, 38, 38);

    private readonly QuizDocument _quiz;
    private int _currentQuestionIndex;
    private readonly Dictionary<int, List<QuizOption>> _userAnswers;
    private DispatcherTimer? _timer;
    private TimeSpan _elapsedTime;
    private bool _isResultsView;
    private readonly int _timeLimitSeconds; // 0 = be limito
    private bool _isCountdown;
    private TimeSpan _remainingTime;
    private readonly HashSet<int> _hintUsedQuestions = new();

    public QuizModeWindow(QuizDocument quiz, int timeLimitSeconds = 0)
    {
        InitializeComponent();

        _quiz = quiz ?? throw new System.ArgumentNullException(nameof(quiz));
        _timeLimitSeconds = timeLimitSeconds;
        _currentQuestionIndex = 0;
        _userAnswers = new Dictionary<int, List<QuizOption>>();
        _elapsedTime = TimeSpan.Zero;
        _isResultsView = false;

        Owner = Application.Current.MainWindow;
        Title = LocalizationService.GetString("QuizModeTitle");

        StartTimer();
        LoadQuestion();
    }

    private void StartTimer()
    {
        _isCountdown = _timeLimitSeconds > 0;
        if (_isCountdown)
            _remainingTime = TimeSpan.FromSeconds(_timeLimitSeconds);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (s, e) =>
        {
            if (_isCountdown)
            {
                _remainingTime -= TimeSpan.FromSeconds(1);
                UpdateTimerDisplay(_remainingTime);

                // ⚠️ Įspėjimas kai lieka mažai laiko (pvz. 30 sek)
                if (_remainingTime.TotalSeconds <= 30)
                    HighlightTimerWarning();

                // ⏱️ Kai laikas baigiasi — automatiškai pateikti
                if (_remainingTime <= TimeSpan.Zero)
                {
                    _timer.Stop();
                    ShowResults(); // arba pereiti prie kito klausimo
                }
            }
            else
            {
                _elapsedTime += TimeSpan.FromSeconds(1);
                UpdateTimerDisplay(_elapsedTime);
            }
        };
        _timer.Start();
    }

    private void HighlightTimerWarning()
    {
        if (FindName("TimerText") is TextBlock timerText)
            timerText.Foreground = new SolidColorBrush(Colors.OrangeRed);
    }
    private void UpdateTimerDisplay(TimeSpan time)
    {
        if (FindName("TimerText") is TextBlock timerText)
            timerText.Text = time.ToString(@"hh\:mm\:ss");
    }

    private void LoadQuestion()
    {
        if (_quiz.Questions == null || _quiz.Questions.Count == 0 || _currentQuestionIndex >= _quiz.Questions.Count)
        {
            ShowResults();
            return;
        }

        var question = _quiz.Questions[_currentQuestionIndex];

        // Show question view, hide results view
        if (FindName("QuestionView") is Border questionView) questionView.Visibility = Visibility.Visible;
        if (FindName("ResultsView") is Border resultsView) resultsView.Visibility = Visibility.Collapsed;

        // Update progress
        if (FindName("ProgressText") is TextBlock progressText)
            progressText.Text = string.Format(LocalizationService.GetString("QuizQuestionProgressFormat"), _currentQuestionIndex + 1, _quiz.Questions.Count);

        // Set question text
        if (FindName("QuestionTextBlock") is TextBlock questionTextBlock)
            questionTextBlock.Text = question.Question ?? string.Empty;

        // ✅ Show question type indicator for multiple choice
        if (FindName("QuestionTypeIndicator") is StackPanel typeIndicator)
        {
            if (question.Type == QuizQuestionType.MultipleChoice)
            {
                typeIndicator.Visibility = Visibility.Visible;
                if (FindName("QuestionTypeText") is TextBlock typeText)
                    typeText.Text = LocalizationService.GetString("QuizMultipleChoiceInstruction");
            }
            else
            {
                typeIndicator.Visibility = Visibility.Collapsed;
            }
        }

        // Clear and populate options
        if (FindName("OptionsPanel") is StackPanel optionsPanel)
        {
            optionsPanel.Children.Clear();

            if (question.Options != null)
            {
                foreach (var option in question.Options)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.Text)) continue;

                    var button = new Button
                    {
                        Content = option.Text,
                        Margin = new Thickness(0, 0, 0, 10),
                        Padding = new Thickness(16, 14, 16, 14),
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Background = (Brush)FindResource("CardBackground"),
                        BorderBrush = (Brush)FindResource("BorderColor"),
                        Foreground = (Brush)FindResource("TextColor"),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand,
                        Tag = option,
                        FontSize = 14,
                        FontWeight = FontWeights.Normal,
                        MinHeight = 44,

                        // ✅ ADD THESE:
                        Focusable = false,                    // Prevents focus rectangle interference
                        OverridesDefaultStyle = true,         // Ignores default hover/pressed triggers
                        Template = CreateSimpleButtonTemplate() // Optional: use minimal template below
                    };

                    // ✅ Wire up click event BEFORE adding to panel
                    button.Click += OptionButton_Click;
                    optionsPanel.Children.Add(button);
                }
            }
        }

        // Restore previously selected answer(s)
        RestoreSelectedAnswer();
        LoadHintSection();
        UpdateNavigationButtons();
    }

    private ControlTemplate CreateSimpleButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));

        // ✅ Set CornerRadius directly on the Border (not via binding)
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));

        // ✅ Bind Background/Border properties from Button to Border using RelativeSource.TemplatedParent
        border.SetBinding(Border.BackgroundProperty,
            new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Button.BackgroundProperty) });
        border.SetBinding(Border.BorderBrushProperty,
            new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Button.BorderBrushProperty) });
        border.SetBinding(Border.BorderThicknessProperty,
            new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Button.BorderThicknessProperty) });

        var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
        contentPresenter.SetValue(ContentPresenter.MarginProperty, new Thickness(16, 14, 16, 14));
        contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentPresenter.SetBinding(ContentPresenter.ContentProperty,
            new Binding { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Path = new PropertyPath(Button.ContentProperty) });

        border.AppendChild(contentPresenter);
        template.VisualTree = border;
        return template;
    }

    private void RestoreSelectedAnswer()
    {
        if (FindName("OptionsPanel") is StackPanel optionsPanel &&
            _userAnswers.TryGetValue(_currentQuestionIndex, out var selectedOptions))
        {
            foreach (var child in optionsPanel.Children)
            {
                if (child is Button btn && btn.Tag is QuizOption opt)
                {
                    bool isSelected = selectedOptions.Contains(opt);
                    btn.Background = isSelected
                        ? (Brush)FindResource("NoteCardSelection")
                        : (Brush)FindResource("CardBackground");
                    btn.BorderBrush = isSelected
                        ? (Brush)FindResource("NoteCardSelectionBorder")
                        : (Brush)FindResource("BorderColor");
                    btn.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    btn.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                    btn.IsEnabled = true;
                }
            }
        }
    }

    private void UpdateNavigationButtons()
    {
        if (FindName("PreviousButton") is Button prevButton)
            prevButton.IsEnabled = _currentQuestionIndex > 0;

        if (FindName("NextButton") is Button nextButton)
        {
            if (_currentQuestionIndex == _quiz.Questions.Count - 1)
            {
                nextButton.Visibility = Visibility.Collapsed;
                if (FindName("SubmitButton") is Button submitButton)
                    submitButton.Visibility = Visibility.Visible;
            }
            else
            {
                nextButton.Visibility = Visibility.Visible;
                if (FindName("SubmitButton") is Button sb2) sb2.Visibility = Visibility.Collapsed;
            }
        }

        if (FindName("DoneButton") is Button doneButton)
            doneButton.Visibility = Visibility.Collapsed;
    }

    private void OptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button clickedButton || clickedButton.Tag is not QuizOption clickedOption)
            return;

        var question = _quiz.Questions[_currentQuestionIndex];
        if (question == null) return;

        bool isMultipleChoice = question.Type == QuizQuestionType.MultipleChoice;

        if (!_userAnswers.ContainsKey(_currentQuestionIndex))
            _userAnswers[_currentQuestionIndex] = new List<QuizOption>();

        var selectedOptions = _userAnswers[_currentQuestionIndex];

        if (isMultipleChoice)
        {
            if (selectedOptions.Contains(clickedOption))
                selectedOptions.Remove(clickedOption);
            else
                selectedOptions.Add(clickedOption);
        }
        else
        {
            selectedOptions.Clear();
            selectedOptions.Add(clickedOption);
        }

        // ✅ Update visuals - now works immediately since we removed interfering triggers
        UpdateOptionVisuals(selectedOptions);
    }

    private void UpdateOptionVisuals(List<QuizOption> selectedOptions)
    {
        if (FindName("OptionsPanel") is StackPanel optionsPanel)
        {
            foreach (var child in optionsPanel.Children)
            {
                if (child is Button optButton && optButton.Tag is QuizOption opt)
                {
                    bool isSelected = selectedOptions.Contains(opt);
                    optButton.Background = isSelected
                        ? (Brush)FindResource("NoteCardSelection")
                        : (Brush)FindResource("CardBackground");
                    optButton.BorderBrush = isSelected
                        ? (Brush)FindResource("NoteCardSelectionBorder")
                        : (Brush)FindResource("BorderColor");
                    optButton.BorderThickness = isSelected ? new Thickness(2) : new Thickness(1);
                    optButton.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
                }
            }
        }
    }
    private void LoadHintSection()
    {
        var question = _quiz.Questions[_currentQuestionIndex];
        var hasHint = !string.IsNullOrWhiteSpace(question.Hint);

        if (FindName("HintSection") is StackPanel hintSection)
            hintSection.Visibility = hasHint ? Visibility.Visible : Visibility.Collapsed;

        if (FindName("HintTextBorder") is Border hintBorder)
            hintBorder.Visibility = Visibility.Collapsed;

        if (FindName("ShowHintButton") is Button showHintBtn)
            showHintBtn.Visibility = Visibility.Visible;

        if (FindName("HintTextBlock") is TextBlock hintText)
            hintText.Text = question.Hint ?? string.Empty;
    }

    private void ShowHintButton_Click(object sender, RoutedEventArgs e)
    {
        _hintUsedQuestions.Add(_currentQuestionIndex);

        if (FindName("HintTextBorder") is Border hintBorder)
            hintBorder.Visibility = Visibility.Visible;

        if (FindName("ShowHintButton") is Button showHintBtn)
            showHintBtn.Visibility = Visibility.Collapsed;
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isResultsView)
        {
            _isResultsView = false;
            _timer?.Start();
            if (FindName("ResultsView") is Border rv) rv.Visibility = Visibility.Collapsed;
            if (FindName("QuestionView") is Border qv) qv.Visibility = Visibility.Visible;
            if (FindName("DoneButton") is Button db) db.Visibility = Visibility.Collapsed;
            if (FindName("SubmitButton") is Button sb) sb.Visibility = Visibility.Collapsed;
            if (FindName("NextButton") is Button nb) nb.Visibility = Visibility.Visible;
            LoadQuestion();
            return;
        }

        if (_currentQuestionIndex > 0)
        {
            _currentQuestionIndex--;
            LoadQuestion();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuestionIndex < _quiz.Questions.Count - 1)
        {
            _currentQuestionIndex++;
            LoadQuestion();
        }
    }

    private void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new QuizSubmitConfirmationDialog(
            title: LocalizationService.GetString("QuizSubmitDialogTitle"),
            message: LocalizationService.GetString("QuizSubmitDialogMessage"),
            confirmText: LocalizationService.GetString("QuizSubmitDialogConfirm"),
            cancelText: LocalizationService.GetString("QuizSubmitDialogCancel"));
        dialog.Owner = this;

        if (dialog.ShowDialog() == true)
            ShowResults();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowResults()
    {
        _timer?.Stop();
        _isResultsView = true;

        var correctCount = 0;
        var totalQuestions = _quiz.Questions?.Count ?? 0;

        for (int i = 0; i < totalQuestions; i++)
        {
            var question = _quiz.Questions[i];
            if (!_userAnswers.TryGetValue(i, out var userSelected)) continue;

            var correctOptions = question.Options?.Where(o => o.IsCorrect).ToList() ?? new List<QuizOption>();
            bool isCorrect = userSelected.Count == correctOptions.Count &&
                            userSelected.All(opt => correctOptions.Contains(opt));
            if (isCorrect) correctCount++;
        }

        var percentage = totalQuestions == 0 ? 0 : (double)correctCount / totalQuestions * 100;

        if (FindName("QuestionView") is Border qv) qv.Visibility = Visibility.Collapsed;
        if (FindName("ResultsView") is Border rv) rv.Visibility = Visibility.Visible;
        if (FindName("ProgressText") is TextBlock pt) pt.Text = string.Format(LocalizationService.GetString("QuizResultsLabelFormat"), correctCount, totalQuestions);
        var displayTime = _isCountdown ? TimeSpan.FromSeconds(_timeLimitSeconds) - _remainingTime : _elapsedTime;
        if (FindName("TimerText") is TextBlock tt) tt.Text = displayTime.ToString(@"hh\:mm\:ss");
        if (FindName("ResultsTitleText") is TextBlock rt)
            rt.Text = percentage >= 70
                ? LocalizationService.GetString("QuizResultsTitleGreat")
                : percentage >= 50
                    ? LocalizationService.GetString("QuizResultsTitleGood")
                    : LocalizationService.GetString("QuizResultsTitleKeepLearning");
        if (FindName("ResultsScoreText") is TextBlock rs)
            rs.Text = $"{correctCount} / {totalQuestions} ({percentage:F1}%)";
        if (FindName("ResultsTimeText") is TextBlock rtime)
            rtime.Text = string.Format(LocalizationService.GetString("QuizTimeFormat"), displayTime.ToString(@"hh\:mm\:ss"));
        if (FindName("PreviousButton") is Button pb) pb.Visibility = Visibility.Collapsed;
        if (FindName("NextButton") is Button nb) nb.Visibility = Visibility.Collapsed;
        if (FindName("SubmitButton") is Button sb) sb.Visibility = Visibility.Collapsed;
        if (FindName("DoneButton") is Button db) db.Visibility = Visibility.Visible;

        if (FindName("ResultsQuestionsPanel") is StackPanel rp)
        {
            rp.Children.Clear();
            for (int i = 0; i < _quiz.Questions.Count; i++)
            {
                var q = _quiz.Questions[i];
                var userSel = _userAnswers.TryGetValue(i, out var ans) ? ans : new List<QuizOption>();
                var correctOpts = q.Options?.Where(o => o.IsCorrect).ToList() ?? new List<QuizOption>();
                var isCorrect = userSel.Count == correctOpts.Count && userSel.All(opt => correctOpts.Contains(opt));

                var card = new Border
                {
                    Background = (Brush)FindResource("CardBackground"),
                    BorderBrush = isCorrect
                        ? new SolidColorBrush(CorrectAnswerBorderColor)
                        : new SolidColorBrush(IncorrectAnswerBorderColor),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16),
                    Margin = new Thickness(0, 0, 0, 12)
                };

                var content = new StackPanel();

                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var qText = new TextBlock
                {
                    Text = string.Format(LocalizationService.GetString("QuizQuestionPrefixFormat"), i + 1, q.Question),
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (Brush)FindResource("TextColor"),
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(qText, 0);
                header.Children.Add(qText);

                var status = new TextBlock
                {
                    Text = isCorrect ? "✓" : "✗",
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = isCorrect
                        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                        : new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                Grid.SetColumn(status, 1);
                header.Children.Add(status);

                content.Children.Add(header);

                var userAns = new TextBlock
                {
                    Text = string.Format(
                        LocalizationService.GetString("QuizYourAnswerFormat"),
                        userSel.Count > 0 ? string.Join(", ", userSel.Select(o => o.Text)) : LocalizationService.GetString("QuizNoAnswer")),
                    FontSize = 13,
                    Foreground = (Brush)FindResource("TextColor"),
                    Margin = new Thickness(0, 8, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                };
                content.Children.Add(userAns);

                if (!isCorrect && correctOpts.Count > 0)
                {
                    var correctAns = new TextBlock
                    {
                        Text = string.Format(LocalizationService.GetString("QuizCorrectAnswerFormat"), string.Join(", ", correctOpts.Select(o => o.Text))),
                        FontSize = 13,
                        Foreground = new SolidColorBrush(CorrectAnswerBorderColor),
                        Margin = new Thickness(0, 0, 0, 4),
                        TextWrapping = TextWrapping.Wrap
                    };
                    content.Children.Add(correctAns);
                }

                if (!string.IsNullOrWhiteSpace(q.Explanation))
                {
                    var expl = new TextBlock
                    {
                        Text = string.Format(LocalizationService.GetString("QuizExplanationFormat"), q.Explanation),
                        FontSize = 12,
                        Foreground = (Brush)FindResource("TextColorSecondary"),
                        Margin = new Thickness(0, 8, 0, 0),
                        TextWrapping = TextWrapping.Wrap,
                        FontStyle = FontStyles.Italic
                    };
                    content.Children.Add(expl);
                }

                card.Child = content;
                rp.Children.Add(card);
            }
        }
    }
}