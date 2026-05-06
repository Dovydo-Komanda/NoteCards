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
    public static readonly DependencyProperty IsEditModeProperty = DependencyProperty.Register(
        nameof(IsEditMode),
        typeof(bool),
        typeof(QuizPreviewWindow),
        new PropertyMetadata(false, OnIsEditModeChanged));

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
        if (_questions.Count == 0)
            _questions.Add(new QuizPreviewQuestion(1, CreateDefaultQuestion()));

        InitializeComponent();

        var displayTitle = string.IsNullOrWhiteSpace(title)
            ? document.Title
            : title;
        TitleTextBox.Text = string.IsNullOrWhiteSpace(displayTitle)
            ? LocalizationService.GetString("QuizUntitled")
            : displayTitle.Trim();
        QuestionsItemsControl.ItemsSource = _questions;
        ConfigureAiGeneratedIndicator(modelDisplayName ?? document.AiModelDisplayName);
        UpdateModeChrome();
        UpdateSummary();

        if (_questions.Any())
        {
            SetQuestionFocus(0);
        }

        PreviewKeyDown += QuizPreviewWindow_PreviewKeyDown;
    }

    public string EditorTitle => TitleTextBox.Text.Trim();

    public string AiModelDisplayName => _modelDisplayName;

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

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
        if (_isSubmitted || IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewOption option)
            return;

        option.Parent.SelectOption(option);
        UpdateSummary();
    }

    private void CheckAnswersButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsEditMode)
            return;

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

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        IsEditMode = !IsEditMode;
    }

    private void AddQuestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            IsEditMode = true;

        _isSubmitted = false;
        var question = new QuizPreviewQuestion(_questions.Count + 1, CreateDefaultQuestion());
        _questions.Add(question);
        RenumberQuestions();
        SetQuestionFocus(_questions.Count - 1);
        UpdateSummary();
    }

    private void AddOptionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewQuestion question)
            return;

        question.AddOption(LocalizationService.GetString("NewQuizWrongAnswer"), isCorrect: false);
        UpdateSummary();
    }

    private void QuizTypeComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox comboBox || comboBox.DataContext is not QuizPreviewQuestion question)
            return;

        SelectQuizTypeComboBoxItem(comboBox, question.Type);
    }

    private void QuizTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox
            || comboBox.DataContext is not QuizPreviewQuestion question
            || comboBox.SelectedItem is not ComboBoxItem item
            || item.Tag is null)
            return;

        if (!Enum.TryParse(item.Tag.ToString(), out QuizQuestionType type))
            return;

        question.Type = type;
        UpdateSummary();
    }

    private static void SelectQuizTypeComboBoxItem(ComboBox comboBox, QuizQuestionType type)
    {
        var targetTag = type.ToString();
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), targetTag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private void MoveQuestionUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewQuestion question)
            return;

        var index = _questions.IndexOf(question);
        if (index <= 0)
            return;

        _questions.Move(index, index - 1);
        RenumberQuestions();
        SetQuestionFocus(index - 1);
    }

    private void MoveQuestionDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewQuestion question)
            return;

        var index = _questions.IndexOf(question);
        if (index < 0 || index >= _questions.Count - 1)
            return;

        _questions.Move(index, index + 1);
        RenumberQuestions();
        SetQuestionFocus(index + 1);
    }

    private void MoveOptionUpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewOption option)
            return;

        var options = option.Parent.Options;
        var index = options.IndexOf(option);
        if (index <= 0)
            return;

        options.Move(index, index - 1);
        option.Parent.RefreshOptionOrderState();
    }

    private void MoveOptionDownButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewOption option)
            return;

        var options = option.Parent.Options;
        var index = options.IndexOf(option);
        if (index < 0 || index >= options.Count - 1)
            return;

        options.Move(index, index + 1);
        option.Parent.RefreshOptionOrderState();
    }

    private void DeleteQuestionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IsEditMode)
            return;

        if ((sender as FrameworkElement)?.DataContext is not QuizPreviewQuestion question)
            return;

        if (_questions.Count <= 1)
        {
            question.Question = LocalizationService.GetString("NewQuizQuestion");
            question.Explanation = string.Empty;
            question.ResetOptions(CreateDefaultQuestion().Options);
            UpdateSummary();
            return;
        }

        var index = _questions.IndexOf(question);
        _questions.Remove(question);
        RenumberQuestions();
        SetQuestionFocus(Math.Min(index, _questions.Count - 1));
        UpdateSummary();
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

    private void RenumberQuestions()
    {
        for (var i = 0; i < _questions.Count; i++)
            _questions[i].Number = i + 1;
    }

    private static void OnIsEditModeChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is QuizPreviewWindow window)
            window.HandleEditModeChanged((bool)e.NewValue);
    }

    private void HandleEditModeChanged(bool isEditMode)
    {
        if (isEditMode && _isSubmitted)
        {
            _isSubmitted = false;
            foreach (var question in _questions)
                question.Reset();
        }

        UpdateModeChrome();
        UpdateSummary();
    }

    private void UpdateModeChrome()
    {
        if (TitleTextBox is null)
            return;

        TitleTextBox.IsReadOnly = !IsEditMode;
        ModeToggleButton.Content = LocalizationService.GetString(IsEditMode ? "ViewQuiz" : "EditQuiz");
        CheckAnswersButton.Visibility = IsEditMode ? Visibility.Collapsed : Visibility.Visible;
        ResetAnswersButton.Visibility = IsEditMode ? Visibility.Collapsed : Visibility.Visible;
        AddQuestionButton.Visibility = IsEditMode ? Visibility.Visible : Visibility.Collapsed;
    }

    private static QuizQuestion CreateDefaultQuestion()
    {
        return new QuizQuestion
        {
            Type = QuizQuestionType.SingleChoice,
            Question = LocalizationService.GetString("NewQuizQuestion"),
            Options = new List<QuizOption>
            {
                new() { Text = LocalizationService.GetString("NewQuizCorrectAnswer"), IsCorrect = true },
                new() { Text = LocalizationService.GetString("NewQuizWrongAnswer"), IsCorrect = false },
                new() { Text = LocalizationService.GetString("NewQuizWrongAnswer"), IsCorrect = false }
            }
        };
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
        else if (IsEditMode)
        {
            return;
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
        private int _number;
        private QuizQuestionType _type;
        private string _question = string.Empty;
        private string _explanation = string.Empty;

        public QuizPreviewQuestion(int number, QuizQuestion source)
        {
            _source = source;
            Number = number;
            _type = source.Type;
            Question = source.Question;
            Explanation = source.Explanation;
            foreach (var option in source.Options)
                Options.Add(new QuizPreviewOption(this, option.Text, option.IsCorrect));
            EnsureValidOptions();
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

        public int Number
        {
            get => _number;
            set
            {
                if (_number == value)
                    return;

                _number = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        public QuizQuestionType Type
        {
            get => _type;
            set
            {
                if (_type == value)
                    return;

                _type = value;
                NormalizeOptionsForType();
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderText));
                OnPropertyChanged(nameof(CanAddOption));
                OnPropertyChanged(nameof(CanReorderOptions));
                foreach (var option in Options)
                    option.RefreshState();
            }
        }

        public string Question
        {
            get => _question;
            set
            {
                if (_question == (value ?? string.Empty))
                    return;

                _question = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public string Explanation
        {
            get => _explanation;
            set
            {
                if (_explanation == (value ?? string.Empty))
                    return;

                _explanation = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasVisibleExplanation));
                OnPropertyChanged(nameof(ExplanationText));
            }
        }

        public ObservableCollection<QuizPreviewOption> Options { get; } = new();

        public bool CanAddOption => Type != QuizQuestionType.TrueFalse;

        public bool CanReorderOptions => Type != QuizQuestionType.TrueFalse && Options.Count > 1;

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

        public void AddOption(string text, bool isCorrect)
        {
            if (!CanAddOption)
                return;

            Options.Add(new QuizPreviewOption(this, text, isCorrect));
            OnPropertyChanged(nameof(CanReorderOptions));
            RefreshState();
        }

        public void RefreshOptionOrderState()
        {
            OnPropertyChanged(nameof(CanReorderOptions));
            foreach (var option in Options)
                option.RefreshState();
        }

        public void ResetOptions(IEnumerable<QuizOption> options)
        {
            Options.Clear();
            foreach (var option in options)
                Options.Add(new QuizPreviewOption(this, option.Text, option.IsCorrect));

            _isSubmitted = false;
            IsCorrect = null;
            EnsureValidOptions();
            OnPropertyChanged(nameof(CanReorderOptions));
            RefreshState();
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
            var options = Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Text))
                .Select(option => new QuizOption
                {
                    Text = option.Text.Trim(),
                    IsCorrect = option.IsCorrect
                })
                .ToList();
            NormalizeModelOptions(Type, options);
            if (options.Count > 0 && options.All(option => !option.IsCorrect))
                options[0].IsCorrect = true;

            return new QuizQuestion
            {
                Type = Type,
                Question = Question.Trim(),
                Explanation = Explanation.Trim(),
                SetIndex = Math.Max(1, _source.SetIndex),
                Options = options
            };
        }

        private void EnsureValidOptions()
        {
            if (Options.Count == 0)
            {
                if (Type == QuizQuestionType.TrueFalse)
                    ResetToTrueFalseOptions();
                else
                {
                    Options.Add(new QuizPreviewOption(this, LocalizationService.GetString("NewQuizCorrectAnswer"), true));
                    Options.Add(new QuizPreviewOption(this, LocalizationService.GetString("NewQuizWrongAnswer"), false));
                    Options.Add(new QuizPreviewOption(this, LocalizationService.GetString("NewQuizWrongAnswer"), false));
                }
            }

            NormalizeOptionsForType();
        }

        private void NormalizeOptionsForType()
        {
            if (Type == QuizQuestionType.TrueFalse)
            {
                ResetToTrueFalseOptions();
                return;
            }

            if (Type == QuizQuestionType.SingleChoice)
            {
                var firstCorrect = Options.FirstOrDefault(option => option.IsCorrect);
                foreach (var option in Options)
                    option.SetCorrectSilently(ReferenceEquals(option, firstCorrect));
            }

            if (Options.Count > 0 && Options.All(option => !option.IsCorrect))
                Options[0].SetCorrectSilently(true);

            OnPropertyChanged(nameof(CanReorderOptions));
        }

        private void ResetToTrueFalseOptions()
        {
            var falseIsCorrect = Options.Any(option =>
                option.IsCorrect
                && ((option.Text ?? string.Empty).Contains("false", StringComparison.OrdinalIgnoreCase)
                    || (option.Text ?? string.Empty).Contains("klaid", StringComparison.OrdinalIgnoreCase)));

            Options.Clear();
            Options.Add(new QuizPreviewOption(this, LocalizationService.GetString("QuizOptionTrue"), !falseIsCorrect));
            Options.Add(new QuizPreviewOption(this, LocalizationService.GetString("QuizOptionFalse"), falseIsCorrect));
            OnPropertyChanged(nameof(CanReorderOptions));
        }

        private static void NormalizeModelOptions(QuizQuestionType type, List<QuizOption> options)
        {
            if (type == QuizQuestionType.TrueFalse)
            {
                var falseIsCorrect = options.Any(option =>
                    option.IsCorrect
                    && ((option.Text ?? string.Empty).Contains("false", StringComparison.OrdinalIgnoreCase)
                        || (option.Text ?? string.Empty).Contains("klaid", StringComparison.OrdinalIgnoreCase)));
                options.Clear();
                options.Add(new QuizOption { Text = LocalizationService.GetString("QuizOptionTrue"), IsCorrect = !falseIsCorrect });
                options.Add(new QuizOption { Text = LocalizationService.GetString("QuizOptionFalse"), IsCorrect = falseIsCorrect });
                return;
            }

            if (type != QuizQuestionType.SingleChoice)
                return;

            var foundCorrect = false;
            foreach (var option in options)
            {
                if (!option.IsCorrect)
                    continue;

                if (!foundCorrect)
                    foundCorrect = true;
                else
                    option.IsCorrect = false;
            }
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
        private string _text = string.Empty;
        private bool _isCorrect;
        private bool _isSelected;

        public QuizPreviewOption(QuizPreviewQuestion parent, string text, bool isCorrect)
        {
            Parent = parent;
            Text = text;
            IsCorrect = isCorrect;
        }

        public QuizPreviewQuestion Parent { get; }

        public string Text
        {
            get => _text;
            set
            {
                if (_text == (value ?? string.Empty))
                    return;

                _text = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public bool IsCorrect
        {
            get => _isCorrect;
            set
            {
                if (_isCorrect == value)
                    return;

                _isCorrect = value;
                if (value && Parent.Type != QuizQuestionType.MultipleChoice)
                {
                    foreach (var option in Parent.Options.Where(option => !ReferenceEquals(option, this)))
                        option.SetCorrectSilently(false);
                }

                OnPropertyChanged();
                RefreshState();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                RefreshState();
            }
        }

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
            OnPropertyChanged(nameof(IsCorrect));
            OnPropertyChanged(nameof(IsCorrectAfterSubmit));
            OnPropertyChanged(nameof(IsIncorrectSelectedAfterSubmit));
            OnPropertyChanged(nameof(MarkerText));
        }

        public void SetCorrectSilently(bool value)
        {
            if (_isCorrect == value)
                return;

            _isCorrect = value;
            RefreshState();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

}
