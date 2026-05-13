using NoteCards.Localization;
using NoteCards.Models;
using System.Collections.Specialized;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Xps;
using System.Windows.Xps.Packaging;

namespace NoteCards.Views;

public partial class QuizPreviewWindow : Window, INotifyPropertyChanged
{
    private enum UnsavedCloseDecision
    {
        Cancel,
        LeaveWithoutSaving,
        SaveAndClose
    }

    public static readonly DependencyProperty IsEditModeProperty = DependencyProperty.Register(
        nameof(IsEditMode),
        typeof(bool),
        typeof(QuizPreviewWindow),
        new PropertyMetadata(false, OnIsEditModeChanged));

    private static readonly Brush CorrectBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
    private static readonly Brush IncorrectBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
    private static readonly Brush UnansweredBrush = new SolidColorBrush(Color.FromRgb(107, 114, 128));

    public sealed class QuizNoteLinkOption
    {
        public QuizNoteLinkOption(Guid id, string title)
        {
            Id = id;
            Title = string.IsNullOrWhiteSpace(title) ? LocalizationService.GetString("QuizUntitled") : title.Trim();
        }

        public Guid Id { get; }
        public string Title { get; }
    }

    private readonly ObservableCollection<QuizPreviewQuestion> _questions;
    private readonly QuizDocument _sourceDocument;
    private readonly ObservableCollection<QuizNoteLinkOption> _noteLinkOptions = new();
    private Action<Guid>? _openNoteAction;
    private string _modelDisplayName = string.Empty;
    private bool _isSubmitted;
    private QuizNoteLinkOption? _selectedLinkedNote;
    private string _lastSavedSnapshot = string.Empty;
    private bool _isInitializing = true;
    private bool _allowCloseWithoutPrompt;

    public QuizPreviewWindow(
        QuizDocument document,
        IEnumerable<QuizNoteLinkOption>? noteOptions = null,
        string? modelDisplayName = null,
        string? title = null,
        Action<Guid>? openNoteAction = null)
        : this(document, noteOptions, modelDisplayName, title, openNoteAction, initializeOnly: false)
    {
    }

    public QuizPreviewWindow(
        QuizDocument document,
        string? modelDisplayName = null,
        string? title = null)
        : this(document, null, modelDisplayName, title, null, initializeOnly: false)
    {
    }

    private QuizPreviewWindow(
        QuizDocument document,
        IEnumerable<QuizNoteLinkOption>? noteOptions,
        string? modelDisplayName,
        string? title,
        Action<Guid>? openNoteAction,
        bool initializeOnly)
    {
        _sourceDocument = document;
        _questions = new ObservableCollection<QuizPreviewQuestion>(
            (document.Questions ?? [])
            .Select((question, index) => new QuizPreviewQuestion(index + 1, question)));
        if (_questions.Count == 0)
            _questions.Add(new QuizPreviewQuestion(1, CreateDefaultQuestion()));

        InitializeComponent();

        _openNoteAction = openNoteAction;
        if (noteOptions != null)
        {
            foreach (var option in noteOptions)
                _noteLinkOptions.Add(option);
        }

        _selectedLinkedNote = _noteLinkOptions.FirstOrDefault(option => option.Id == document.SourceNoteId);

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

        OnPropertyChanged(nameof(LinkedNoteVisibility));
        OnPropertyChanged(nameof(LinkedNoteEditorVisibility));
        OnPropertyChanged(nameof(LinkedNoteButtonText));
        OnPropertyChanged(nameof(LinkedNoteButtonToolTip));

        PreviewKeyDown += QuizPreviewWindow_PreviewKeyDown;
        TrackQuestionChanges();
        _isInitializing = false;
        MarkCurrentStateSaved();

    }
    public string EditorTitle => TitleTextBox.Text.Trim();

    public string AiModelDisplayName => _modelDisplayName;

    public string LinkedNoteDisplayText => _selectedLinkedNote?.Title ?? string.Empty;

    public string LinkedNoteButtonText => string.IsNullOrWhiteSpace(LinkedNoteDisplayText)
        ? LocalizationService.GetString("QuizLinkedNoteNone")
        : LinkedNoteDisplayText;

    public string LinkedNoteButtonToolTip => string.IsNullOrWhiteSpace(LinkedNoteDisplayText)
        ? LocalizationService.GetString("QuizLinkedNoteNoneTooltip")
        : string.Format(LocalizationService.GetString("QuizLinkedNoteOpenTooltip"), LinkedNoteDisplayText);

    public Visibility LinkedNoteVisibility => HasLinkedNote() ? Visibility.Visible : Visibility.Collapsed;

    public Visibility LinkedNoteEditorVisibility => IsEditMode ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<QuizNoteLinkOption> NoteLinkOptions => _noteLinkOptions;

    public QuizNoteLinkOption? SelectedLinkedNote
    {
        get => _selectedLinkedNote;
        set
        {
            if (ReferenceEquals(_selectedLinkedNote, value))
                return;

            _selectedLinkedNote = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LinkedNoteDisplayText));
            OnPropertyChanged(nameof(LinkedNoteButtonText));
            OnPropertyChanged(nameof(LinkedNoteButtonToolTip));
            OnPropertyChanged(nameof(LinkedNoteVisibility));
            UpdateEditedIndicator();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsEditMode
    {
        get => (bool)GetValue(IsEditModeProperty);
        set => SetValue(IsEditModeProperty, value);
    }

    private void EditorField_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateEditedIndicator();
    }

    private void TrackQuestionChanges()
    {
        _questions.CollectionChanged += Questions_CollectionChanged;
        foreach (var question in _questions)
            AttachQuestionChangeTracking(question);
    }

    private void Questions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (QuizPreviewQuestion question in e.OldItems)
                DetachQuestionChangeTracking(question);
        }

        if (e.NewItems is not null)
        {
            foreach (QuizPreviewQuestion question in e.NewItems)
                AttachQuestionChangeTracking(question);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var question in _questions)
                AttachQuestionChangeTracking(question);
        }

        UpdateEditedIndicator();
    }

    private void AttachQuestionChangeTracking(QuizPreviewQuestion question)
    {
        question.PropertyChanged -= QuizQuestion_PropertyChanged;
        question.PropertyChanged += QuizQuestion_PropertyChanged;
        question.Options.CollectionChanged -= QuizOptions_CollectionChanged;
        question.Options.CollectionChanged += QuizOptions_CollectionChanged;

        foreach (var option in question.Options)
        {
            option.PropertyChanged -= QuizOption_PropertyChanged;
            option.PropertyChanged += QuizOption_PropertyChanged;
        }
    }

    private void DetachQuestionChangeTracking(QuizPreviewQuestion question)
    {
        question.PropertyChanged -= QuizQuestion_PropertyChanged;
        question.Options.CollectionChanged -= QuizOptions_CollectionChanged;

        foreach (var option in question.Options)
            option.PropertyChanged -= QuizOption_PropertyChanged;
    }

    private void QuizQuestion_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSavedQuestionProperty(e.PropertyName))
            UpdateEditedIndicator();
    }

    private void QuizOptions_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (QuizPreviewOption option in e.OldItems)
                option.PropertyChanged -= QuizOption_PropertyChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (QuizPreviewOption option in e.NewItems)
            {
                option.PropertyChanged -= QuizOption_PropertyChanged;
                option.PropertyChanged += QuizOption_PropertyChanged;
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset && sender is ObservableCollection<QuizPreviewOption> options)
        {
            foreach (var option in options)
            {
                option.PropertyChanged -= QuizOption_PropertyChanged;
                option.PropertyChanged += QuizOption_PropertyChanged;
            }
        }

        UpdateEditedIndicator();
    }

    private void QuizOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSavedOptionProperty(e.PropertyName))
            UpdateEditedIndicator();
    }

    private static bool IsSavedQuestionProperty(string? propertyName)
    {
        return propertyName is null
            || propertyName == nameof(QuizPreviewQuestion.Number)
            || propertyName == nameof(QuizPreviewQuestion.Type)
            || propertyName == nameof(QuizPreviewQuestion.Question)
            || propertyName == nameof(QuizPreviewQuestion.Explanation);
    }

    private static bool IsSavedOptionProperty(string? propertyName)
    {
        return propertyName is null
            || propertyName == nameof(QuizPreviewOption.Text)
            || propertyName == nameof(QuizPreviewOption.IsCorrect);
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
        var questionsSnapshot = string.Join('\u001E', _questions.Select(question =>
        {
            var optionsSnapshot = string.Join('\u001C', question.Options.Select(option =>
                string.Join('\u001B', option.Text, option.IsCorrect.ToString())));

            return string.Join(
                '\u001D',
                question.Number.ToString(CultureInfo.InvariantCulture),
                question.Type.ToString(),
                question.Question,
                question.Explanation,
                optionsSnapshot);
        }));

        return string.Join(
            '\u001F',
            TitleTextBox.Text,
            GetSourceNoteIdForSnapshot(),
            questionsSnapshot);
    }

    private string GetSourceNoteIdForSnapshot()
        => (SelectedLinkedNote?.Id ?? _sourceDocument.SourceNoteId)?.ToString() ?? string.Empty;

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
            ? LocalizationService.GetString("QuizUntitled")
            : title;
    }

    private void SaveAndClose()
    {
        MarkCurrentStateSaved();
        _allowCloseWithoutPrompt = true;
        DialogResult = true;
        Close();
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
            SourceNoteId = SelectedLinkedNote?.Id ?? existingDocument?.SourceNoteId ?? _sourceDocument.SourceNoteId,
            GroupId = existingDocument?.GroupId ?? _sourceDocument.GroupId
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

    private void StartQuizButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceDocument?.Questions == null || _sourceDocument.Questions.Count == 0)
        {
            MessageBox.Show(
                LocalizationService.GetString("QuizNoQuestions"),
                LocalizationService.GetString("QuizMode"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            var selectedItem = TimeLimitComboBox.SelectedItem as ComboBoxItem;
            int timeLimitSeconds = selectedItem?.Tag is string tag ? int.Parse(tag) : 0;
            var quizModeWindow = new QuizModeWindow(_sourceDocument, timeLimitSeconds);
            quizModeWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error starting quiz: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SaveAndClose();
    }

    private void QuizPreviewWindow_Closing(object? sender, CancelEventArgs e)
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

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_questions.Count == 0)
            return;

        var saveDialog = new SaveFileDialog
        {
            Title = LocalizationService.GetString("QuizExportDialogTitle"),
            Filter = LocalizationService.GetString("QuizExportDialogFilter"),
            FileName = GetExportFileName(),
            OverwritePrompt = true
        };

        if (saveDialog.ShowDialog(this) != true)
            return;

        try
        {
            var extension = Path.GetExtension(saveDialog.FileName).ToLowerInvariant();
            switch (extension)
            {
                case ".json":
                    ExportToJson(saveDialog.FileName);
                    break;
                case ".csv":
                    ExportToCsv(saveDialog.FileName);
                    break;
                case ".xps":
                case ".pdf":
                    ExportPrintableDocument(saveDialog.FileName);
                    break;
                default:
                    throw new InvalidOperationException(LocalizationService.GetString("QuizExportUnsupportedFormat"));
            }

            ShowExportDialog(
                LocalizationService.GetString("Success"),
                LocalizationService.GetString("QuizExportComplete"));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                string.Format(CultureInfo.CurrentCulture, LocalizationService.GetString("QuizExportFailedFormat"), ex.Message),
                LocalizationService.GetString("ExportError"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowExportDialog(string title, string message)
    {
        var dialog = new ModernInfoDialog(title, message)
        {
            Owner = this
        };

        dialog.ShowDialog();
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

    private void LinkedNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedLinkedNote is null)
            return;

        _openNoteAction?.Invoke(SelectedLinkedNote.Id);
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
        OnPropertyChanged(nameof(LinkedNoteVisibility));
        OnPropertyChanged(nameof(LinkedNoteEditorVisibility));
        OnPropertyChanged(nameof(LinkedNoteButtonText));
        OnPropertyChanged(nameof(LinkedNoteButtonToolTip));
    }

    private bool HasLinkedNote()
    {
        return SelectedLinkedNote is not null;
    }

    private string GetExportFileName()
    {
        var baseName = string.IsNullOrWhiteSpace(EditorTitle)
            ? LocalizationService.GetString("QuizUntitled")
            : EditorTitle;

        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');

        return baseName;
    }

    private void ExportToJson(string path)
    {
        var exportDocument = ToDocument();
        var json = JsonSerializer.Serialize(exportDocument, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private void ExportToCsv(string path)
    {
        var exportDocument = ToDocument();
        var sb = new StringBuilder();
        sb.AppendLine("QuestionNumber,Type,Question,CorrectAnswers,Explanation");

        foreach (var question in exportDocument.Questions)
        {
            var correctAnswers = string.Join(" | ", question.Options.Where(option => option.IsCorrect).Select(option => option.Text));
            sb.AppendLine(string.Join(",",
                EscapeCsv(question.SetIndex.ToString(CultureInfo.InvariantCulture)),
                EscapeCsv(question.Type.ToString()),
                EscapeCsv(question.Question),
                EscapeCsv(correctAnswers),
                EscapeCsv(question.Explanation)));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private void ExportPrintableDocument(string path)
    {
        var document = BuildPrintableDocument();
        using var xpsDocument = new XpsDocument(path, FileAccess.Write);
        var writer = XpsDocument.CreateXpsDocumentWriter(xpsDocument);
        writer.Write(((IDocumentPaginatorSource)document).DocumentPaginator);
    }

    private FlowDocument BuildPrintableDocument()
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(48),
            ColumnWidth = 700,
            Background = Brushes.White,
            Foreground = Brushes.Black
        };

        document.Blocks.Add(new Paragraph(new Run(EditorTitle))
        {
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 16)
        });

        foreach (var question in _questions)
        {
            document.Blocks.Add(new Paragraph(new Run(string.Format(CultureInfo.CurrentCulture, LocalizationService.GetString("QuizQuestionHeaderFormat"), question.Number, question.Type switch
            {
                QuizQuestionType.TrueFalse => LocalizationService.GetString("QuizTypeTrueFalse"),
                QuizQuestionType.MultipleChoice => LocalizationService.GetString("QuizTypeMultipleChoice"),
                _ => LocalizationService.GetString("QuizTypeSingleChoice")
            })))
            {
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            });

            document.Blocks.Add(new Paragraph(new Run(question.Question))
            {
                Margin = new Thickness(0, 0, 0, 6)
            });

            foreach (var option in question.Options.Where(option => !string.IsNullOrWhiteSpace(option.Text)))
            {
                var marker = option.IsCorrect ? "✓" : "•";
                document.Blocks.Add(new Paragraph(new Run($"{marker} {option.Text}"))
                {
                    Margin = new Thickness(12, 0, 0, 2)
                });
            }

            if (!string.IsNullOrWhiteSpace(question.Explanation))
            {
                document.Blocks.Add(new Paragraph(new Run(string.Format(CultureInfo.CurrentCulture, LocalizationService.GetString("QuizExplanationFormat"), question.Explanation)))
                {
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
        }

        return document;
    }

    private static string EscapeCsv(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
