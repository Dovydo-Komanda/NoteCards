using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NoteCards.Views;

public partial class QuizLibraryWindow : Window, INotifyPropertyChanged
{
    private readonly MainViewModel _mainViewModel;
    private readonly ObservableCollection<global::NoteCards.Views.QuizLibraryItemViewModel> _quizzes = new();
    private readonly ObservableCollection<TagFilterItemViewModel> _categoryFilters = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);
    private string _statusText = string.Empty;
    private string _searchText = string.Empty;

    public string QuizSearchQuery
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value ?? string.Empty;
            UpdateFilteredVisibility();
            UpdateStatusText();
            OnPropertyChanged(nameof(QuizSearchQuery));
        }
    }

    public QuizLibraryWindow(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel;
        InitializeComponent();
        NoteCards.Services.WindowThemeService.Register(this);
        DataContext = this;

        LoadQuizzes();
        LoadCategories();
        UpdateStatusText();
        UpdateFilteredVisibility();
        _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
    }

    public ObservableCollection<global::NoteCards.Views.QuizLibraryItemViewModel> Quizzes => _quizzes;

    public ObservableCollection<TagFilterItemViewModel> CategoryFilters => _categoryFilters;

    public string FilteredCountText => string.Format(CultureInfo.CurrentCulture, "{0} quizzes", _quizzes.Count(q => q.IsVisible));

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;

            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.QuizTagFilters)
            || e.PropertyName == nameof(MainViewModel.ActiveTagFilters)
            || e.PropertyName == nameof(MainViewModel.ActiveTagFilterButtonText))
        {
            LoadCategories();
            UpdateFilteredVisibility();
            UpdateStatusText();
        }
    }

    private void LoadQuizzes()
    {
        _quizzes.Clear();
        foreach (var quiz in _mainViewModel.Quizzes)
        {
            var wrapper = new global::NoteCards.Views.QuizLibraryItemViewModel(quiz);
            wrapper.PropertyChanged += QuizWrapper_PropertyChanged;
            _quizzes.Add(wrapper);
        }
    }

    private void QuizWrapper_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(QuizLibraryItemViewModel.IsVisible)
            or nameof(QuizLibraryItemViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(FilteredCountText));
            UpdateStatusText();
        }
    }

    private void LoadCategories()
    {
        _categoryFilters.Clear();
        foreach (var filter in _mainViewModel.QuizTagFilters)
        {
            var existing = _selectedTags.Contains(filter.Tag);
            var wrapper = new TagFilterItemViewModel(filter.Tag, existing, SetTagSelected);
            _categoryFilters.Add(wrapper);
        }
    }

    private void SetTagSelected(string tag, bool isSelected)
    {
        if (isSelected)
            _selectedTags.Add(tag);
        else
            _selectedTags.Remove(tag);

        UpdateFilteredVisibility();
        UpdateStatusText();
    }

    private void UpdateFilteredVisibility()
    {
        foreach (var quiz in _quizzes)
            quiz.IsVisible = MatchesFilter(quiz);

        OnPropertyChanged(nameof(FilteredCountText));
    }

    private bool MatchesFilter(QuizLibraryItemViewModel quiz)
    {
        if (_selectedTags.Count > 0)
        {
            var tags = quiz.Document.Tags ?? [];
            if (!tags.Any(tag => _selectedTags.Contains(tag)))
                return false;
        }

        if (string.IsNullOrWhiteSpace(_searchText))
            return true;

        return quiz.Title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || quiz.TagsDisplay.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
               || quiz.Document.Questions.Any(question =>
                   (question.Question ?? string.Empty).Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                   || (question.Explanation ?? string.Empty).Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                   || question.Options.Any(option => (option.Text ?? string.Empty).Contains(_searchText, StringComparison.OrdinalIgnoreCase)));
    }

    private void UpdateStatusText()
    {
        var visibleCount = _quizzes.Count(quiz => quiz.IsVisible);
        StatusText = string.Format(CultureInfo.CurrentCulture, "{0} of {1} quizzes shown", visibleCount, _quizzes.Count);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            QuizSearchQuery = tb.Text;
    }

    private void CategoriesButton_Click(object sender, RoutedEventArgs e)
    {
        var popup = new ContextMenu();
        foreach (var filter in _categoryFilters)
        {
            var item = new MenuItem
            {
                Header = filter.Tag,
                IsCheckable = true,
                IsChecked = filter.IsSelected
            };
            item.Click += (_, _) => filter.IsSelected = !filter.IsSelected;
            popup.Items.Add(item);
        }

        if (sender is Button button)
        {
            popup.PlacementTarget = button;
            popup.IsOpen = true;
        }
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedTags.Clear();
        foreach (var filter in _categoryFilters)
            filter.IsSelected = false;

        QuizSearchQuery = string.Empty;
        UpdateFilteredVisibility();
        UpdateStatusText();
    }

    private void QuizItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is global::NoteCards.Views.QuizLibraryItemViewModel quiz)
            OpenQuiz(quiz);
    }

    private void OpenQuizButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: global::NoteCards.Views.QuizLibraryItemViewModel quiz })
            OpenQuiz(quiz);
    }

    private void OpenQuiz(QuizLibraryItemViewModel quiz)
    {
        var editor = new QuizPreviewWindow(
            quiz.Document,
            quiz.Document.AiModelDisplayName,
            quiz.Document.Title)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            _mainViewModel.AddOrUpdateQuiz(editor.ToDocument(quiz.Document));
    }

    private void CreateCombinedQuizButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedQuizzes = _quizzes.Where(quiz => quiz.IsSelected).Select(quiz => quiz.Model).ToList();
        if (selectedQuizzes.Count == 0)
        {
            ModernMessageBox.Show("Select at least one quiz first.", "Quiz library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var combined = BuildCombinedQuiz(selectedQuizzes);
        var editor = new QuizPreviewWindow(
            combined,
            combined.AiModelDisplayName,
            combined.Title)
        {
            Owner = this
        };

        if (editor.ShowDialog() == true)
            _mainViewModel.AddOrUpdateQuiz(editor.ToDocument());
    }

    private static QuizDocument BuildCombinedQuiz(IEnumerable<QuizViewModel> selectedQuizzes)
    {
        var documents = selectedQuizzes.Select(q => q.Document).ToList();
        var combinedTags = documents.SelectMany(d => d.Tags ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var combinedQuestions = new List<QuizQuestion>();

        foreach (var document in documents)
        {
            foreach (var question in document.Questions)
            {
                combinedQuestions.Add(new QuizQuestion
                {
                    Type = question.Type,
                    Question = question.Question,
                    Explanation = question.Explanation,
                    SetIndex = combinedQuestions.Count + 1,
                    Options = question.Options.Select(option => new QuizOption
                    {
                        Text = option.Text,
                        IsCorrect = option.IsCorrect
                    }).ToList()
                });
            }
        }

        return new QuizDocument
        {
            Title = "Combined quiz",
            Tags = combinedTags,
            Questions = combinedQuestions,
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.Now
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    }
