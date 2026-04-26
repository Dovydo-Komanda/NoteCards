using Microsoft.Win32;
using NoteCards.Animations;
using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.Services;
using NoteCards.ViewModels;
using NoteCards.Views;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NoteCards
{
    public partial class NoteEditorWindow : Window
    {
        private bool? _pendingDialogResult = null;
        private bool _isPlayingCloseAnimation = false;
        private bool _isHostedInTab;
        // Last search/replace state for Find Next / Replace Next functionality
        private string? _lastSearchQuery = null;
        private string? _lastReplacementText = null;

        // Auto-save fields
        public event Action<NoteDocument>? DocumentAutoSaved;
        public event Action<NoteEditorWindow>? CloseRequested;
        public static event EventHandler? AiGenerationStateChanged;
        private static int _activeAiGenerationCount;
        private System.Threading.Timer? _autoSaveTimer;
        private bool _isAutoSaveEnabled = true;
        private const int AutoSaveIntervalMs = 30000; // 30 seconds
        private DateTime _lastAutoSaveTime = DateTime.MinValue;
        private string _lastSavedContent = string.Empty;
        private string _lastSavedSnapshot = string.Empty;
        private NoteDocument? _currentDocument;
        private const int MaxEditHistoryEntries = 100;
        private readonly FlashcardConversionService _flashcardConversionService = new();
        private readonly MindMapConversionService _mindMapConversionService = new();
        private bool _isConvertingToFlashcards;
        private CancellationTokenSource? _flashcardConversionCancellationSource;
        private bool _isConvertingToMindMap;
        private CancellationTokenSource? _mindMapConversionCancellationSource;
        private bool _isSyncingFontSelectors;
        private bool _isLoadingDocument;
        private bool _allowCloseWithoutPrompt;
        private const double StatusIndicatorExpandedHeight = 20;

        public static bool IsAiGenerationInProgress => _activeAiGenerationCount > 0;

        public NoteEditorWindow()
        {
            InitializeComponent();
            InitializeAutoSave();
            UpdateCounter();
            UpdateOnlineSearchAvailability();
            ContentTextBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            // Subscribe to theme changes to update RichTextBox foreground
            ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        }

        private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
        {
            ApplyRichTextBoxTheme();
        }

        public void EnableTabMode()
        {
            _isHostedInTab = true;

            // In standalone mode the window-level Loaded trigger animates RootGrid from Opacity=0.
            // When hosted inside tabs, the window is never shown, so force visible state here.
            if (RootGrid != null)
            {
                RootGrid.BeginAnimation(UIElement.OpacityProperty, null);
                RootGrid.Opacity = 1;

                if (RootGrid.RenderTransform is TranslateTransform translate)
                    translate.Y = 0;
            }
        }

        public UIElement? DetachEditorContentForHosting()
        {
            var root = Content as UIElement;
            Content = null;
            return root;
        }

        public void DisposeHostedEditor()
        {
            StopAutoSaveTimer();
            _flashcardConversionCancellationSource?.Cancel();
            _flashcardConversionCancellationSource?.Dispose();
            _flashcardConversionCancellationSource = null;
            _mindMapConversionCancellationSource?.Cancel();
            _mindMapConversionCancellationSource?.Dispose();
            _mindMapConversionCancellationSource = null;
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
        }

        private Window GetDialogOwnerWindow()
        {
            return Window.GetWindow(ContentTextBox) ?? this;
        }

        private void NoteEditorWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_isPlayingCloseAnimation)
                return;

            if (!_allowCloseWithoutPrompt && !ConfirmCloseIfNeeded())
            {
                e.Cancel = true;
                return;
            }

            StopAutoSaveTimer();
            _flashcardConversionCancellationSource?.Cancel();
            _mindMapConversionCancellationSource?.Cancel();
            ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;

            // For non-modal windows, just close directly
            // No need for animation since the window will close immediately
            e.Cancel = false;
        }

        public bool ConfirmCloseIfNeeded()
        {
            if (!HasUnsavedChanges())
                return true;

            var documentTitle = ResolveEditorTitleForPrompt();
            var dialog = new DeleteConfirmationDialog(
                LocalizationService.GetString("UnsavedChanges"),
                string.Format(LocalizationService.GetString("UnsavedChangesConfirmationFormat"), documentTitle),
                LocalizationService.GetString("LeaveWithoutSaving"),
                LocalizationService.GetString("Cancel"),
                LocalizationService.GetString("SaveAndExit"))
            {
                Owner = GetDialogOwnerWindow()
            };

            if (dialog.ShowDialog() != true)
                return false;

            return dialog.SelectedAction switch
            {
                DeleteConfirmationDialog.ConfirmationAction.Confirm => true,
                DeleteConfirmationDialog.ConfirmationAction.Secondary => SaveCurrentDocument(),
                _ => false
            };
        }

        private string ResolveEditorTitleForPrompt()
        {
            var title = TitleTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(title))
                return title;

            title = _currentDocument?.Title?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(title)
                ? LocalizationService.GetString("NewNoteTitle")
                : title;
        }

        private void AnimateAndClose()
        {
            if (_isPlayingCloseAnimation)
                return;

            _isPlayingCloseAnimation = true;

            var sb = ((Storyboard)Resources["CloseStoryboard"]).Clone();
            sb.Completed += (_, _) =>
            {
                if (_pendingDialogResult.HasValue)
                    this.DialogResult = _pendingDialogResult.Value;
                else
                    this.Close();
            };
            sb.Begin(this);
        }

        private void ClearAllHighlights()
        {
            var doc = ContentTextBox.Document;
            var textRange = new TextRange(doc.ContentStart, doc.ContentEnd);
            textRange.ApplyPropertyValue(TextElement.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
        }

        private void ApplyRichTextBoxTheme()
        {
            try
            {
                // Get the foreground color from the application theme resources
                if (Application.Current.Resources.Contains("RichTextBoxForeground"))
                {
                    var foregroundBrush = (Brush)Application.Current.Resources["RichTextBoxForeground"];
                    var doc = ContentTextBox.Document;

                    // Set default foreground for the entire document
                    doc.Foreground = foregroundBrush;

                    // Apply foreground to entire document text range
                    var textRange = new TextRange(doc.ContentStart, doc.ContentEnd);
                    textRange.ApplyPropertyValue(TextElement.ForegroundProperty, foregroundBrush);
                }
            }
            catch (Exception ex)
            {
                // Log or handle error silently - theme application shouldn't crash the app
                System.Diagnostics.Debug.WriteLine($"Error applying RichTextBox theme: {ex.Message}");
            }
        }

        private void UpdateCounter()
        {
            TextRange textRange = new TextRange(
                ContentTextBox.Document.ContentStart,
                ContentTextBox.Document.ContentEnd);

            var text = textRange.Text;

            // RichTextBox/FlowDocument always includes one trailing paragraph break.
            // Remove only that synthetic ending so empty notes are not counted as 2 chars/2 lines.
            if (text.EndsWith("\r\n", StringComparison.Ordinal))
                text = text[..^2];
            else if (text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal))
                text = text[..^1];

            var normalizedText = text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            int characters = normalizedText.Length;

            int words = string.IsNullOrWhiteSpace(normalizedText)
                ? 0
                : normalizedText.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            int lines = string.IsNullOrEmpty(normalizedText)
                ? 1
                : normalizedText.Split('\n').Length;

            CounterText.Text = string.Format(
                LocalizationService.GetString("EditorCounterFormat"),
                words,
                characters,
                lines);
        }

        private void ContentTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // If selection is empty (deselected), clear highlights
            var sel = ContentTextBox.Selection;
            if (sel == null || sel.IsEmpty)
            {
                ClearAllHighlights();
            }
            else
            {
                // Selection is used directly for online search.
            }

            UpdateOnlineSearchAvailability();
            UpdateCounter();
        }

        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Only update counter, not theme (theme is applied during load and on theme change)
            UpdateCounter();
            UpdateEditedIndicator();
        }

        private void EditorField_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateEditedIndicator();
        }

        private void OnlineSearchButton_Click(object sender, RoutedEventArgs e)
        {
            OpenOnlineSearch();
        }

        private void UpdateOnlineSearchAvailability()
        {
            OnlineSearchButton.IsEnabled = !string.IsNullOrWhiteSpace(ResolveOnlineSearchQuery());
        }

        private string ResolveOnlineSearchQuery()
        {
            var selectedText = ContentTextBox.Selection?.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(selectedText))
                return selectedText;

            return string.Empty;
        }

        private void OpenOnlineSearch()
        {
            var query = ResolveOnlineSearchQuery();
            if (string.IsNullOrWhiteSpace(query))
                return;

            var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("SearchOnlineFailed")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Open combined Find/Replace dialog
            var dlg = new Views.SearchReplaceDialogLocalized(_lastSearchQuery, _lastReplacementText);
            dlg.Owner = GetDialogOwnerWindow();
            var res = dlg.ShowDialog();
            if (res == true)
            {
                // save last used values
                _lastSearchQuery = dlg.SearchText;
                _lastReplacementText = dlg.ReplacementText;
            }
        }

        private async void ConvertToFlashcardsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConvertingToFlashcards || _isConvertingToMindMap)
                return;

            var plainText = GetEditorAiText();
            if (string.IsNullOrWhiteSpace(plainText))
            {
                MessageBox.Show(
                    LocalizationService.GetString("ConvertToFlashcardsEmpty"),
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _isConvertingToFlashcards = true;
            BeginAiGeneration();
            ConvertToFlashcardsButton.IsEnabled = false;
            ConvertToMindMapButton.IsEnabled = false;
            ShowPersistentStatusIndicator(LocalizationService.GetString("ConvertToFlashcardsInProgress"));

            _flashcardConversionCancellationSource?.Dispose();
            _flashcardConversionCancellationSource = new CancellationTokenSource();

            var progress = new Progress<BundledModelHostService.FlashcardProgress>(status =>
            {
                var text = BuildAiProgressText(status, null, null, out var useAnimation);

                if (useAnimation)
                    ShowPersistentStatusIndicator(text);
                else
                    ShowPersistentStatusIndicatorWithoutAnimation(text);
            });

            var restoreAutoSave = false;
            try
            {
                restoreAutoSave = TemporarilyDisableAutoSaveForAi();
                var flashcards = await _flashcardConversionService.ConvertToFlashcardsAsync(
                    plainText,
                    progress,
                    _flashcardConversionCancellationSource.Token);
                RestoreAutoSaveAfterAi(ref restoreAutoSave);

                if (flashcards.Count == 0)
                {
                    HideStatusIndicator();
                    MessageBox.Show(
                        LocalizationService.GetString("ConvertToFlashcardsParseFailed"),
                        LocalizationService.GetString("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var modelDisplayName = BundledModelHostService.Instance.GetSelectedModelDisplayName();
                var preview = new FlashcardsPreviewWindow(
                    flashcards,
                    modelDisplayName,
                    TitleTextBox.Text,
                    ParseTags(TagsTextBox.Text))
                {
                    Owner = GetDialogOwnerWindow()
                };

                if (preview.ShowDialog() == true
                    && Application.Current.MainWindow?.DataContext is MainViewModel mainViewModel)
                {
                    mainViewModel.AddOrUpdateFlashcardSet(preview.ToDocument());
                }

                ShowStatusIndicator(LocalizationService.GetString("ConvertToFlashcardsSuccess"));
            }
            catch (OperationCanceledException ex)
            {
                HideStatusIndicator();
                if (_flashcardConversionCancellationSource?.IsCancellationRequested == true)
                    return;

                MessageBox.Show(
                    $"{LocalizationService.GetString("ConvertToFlashcardsFailed")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (AiInputRejectedException ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    ex.Message,
                    LocalizationService.GetString("AiInputRejectedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (TimeoutException ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    $"{LocalizationService.GetString("AiGenerationTimedOut")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    $"{LocalizationService.GetString("ConvertToFlashcardsFailed")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RestoreAutoSaveAfterAi(ref restoreAutoSave);
                _isConvertingToFlashcards = false;
                EndAiGeneration();
                ConvertToFlashcardsButton.IsEnabled = true;
                ConvertToMindMapButton.IsEnabled = true;
                _flashcardConversionCancellationSource?.Dispose();
                _flashcardConversionCancellationSource = null;
            }
        }

        private async void ConvertToMindMapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConvertingToFlashcards || _isConvertingToMindMap)
                return;

            var plainText = GetEditorAiText();
            if (string.IsNullOrWhiteSpace(plainText))
            {
                MessageBox.Show(
                    LocalizationService.GetString("ConvertToMindMapEmpty"),
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _isConvertingToMindMap = true;
            BeginAiGeneration();
            ConvertToFlashcardsButton.IsEnabled = false;
            ConvertToMindMapButton.IsEnabled = false;
            ShowPersistentStatusIndicator(LocalizationService.GetString("ConvertToMindMapInProgress"));

            _mindMapConversionCancellationSource?.Dispose();
            _mindMapConversionCancellationSource = new CancellationTokenSource();

            var progress = new Progress<BundledModelHostService.FlashcardProgress>(status =>
            {
                var text = BuildAiProgressText(
                    status,
                    processingStatusKey: "ConvertToMindMapStatusProcessing",
                    finalizingStatusKey: "ConvertToMindMapStatusFinalizing",
                    out var useAnimation);

                if (useAnimation)
                    ShowPersistentStatusIndicator(text);
                else
                    ShowPersistentStatusIndicatorWithoutAnimation(text);
            });

            var restoreAutoSave = false;
            try
            {
                restoreAutoSave = TemporarilyDisableAutoSaveForAi();
                var mindMap = await _mindMapConversionService.ConvertToMindMapAsync(
                    TitleTextBox.Text,
                    plainText,
                    progress,
                    _mindMapConversionCancellationSource.Token);
                RestoreAutoSaveAfterAi(ref restoreAutoSave);

                if (mindMap is null || mindMap.Children.Count == 0)
                {
                    HideStatusIndicator();
                    MessageBox.Show(
                        LocalizationService.GetString("ConvertToMindMapParseFailed"),
                        LocalizationService.GetString("Error"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var modelDisplayName = BundledModelHostService.Instance.GetSelectedModelDisplayName();
                var preview = new MindMapPreviewWindow(
                    mindMap,
                    modelDisplayName,
                    TitleTextBox.Text,
                    ParseTags(TagsTextBox.Text))
                {
                    Owner = GetDialogOwnerWindow()
                };

                if (preview.ShowDialog() == true
                    && Application.Current.MainWindow?.DataContext is MainViewModel mainViewModel)
                {
                    var document = preview.ToDocument();
                    document.SourceNoteId = _currentDocument?.Id;
                    mainViewModel.AddOrUpdateMindMap(document);
                }

                ShowStatusIndicator(LocalizationService.GetString("ConvertToMindMapSuccess"));
            }
            catch (OperationCanceledException ex)
            {
                HideStatusIndicator();
                if (_mindMapConversionCancellationSource?.IsCancellationRequested == true)
                    return;

                MessageBox.Show(
                    $"{LocalizationService.GetString("ConvertToMindMapFailed")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (AiInputRejectedException ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    ex.Message,
                    LocalizationService.GetString("AiInputRejectedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (TimeoutException ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    $"{LocalizationService.GetString("AiGenerationTimedOut")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                HideStatusIndicator();
                MessageBox.Show(
                    $"{LocalizationService.GetString("ConvertToMindMapFailed")}\n\n{ex.Message}",
                    LocalizationService.GetString("Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RestoreAutoSaveAfterAi(ref restoreAutoSave);
                _isConvertingToMindMap = false;
                EndAiGeneration();
                ConvertToFlashcardsButton.IsEnabled = true;
                ConvertToMindMapButton.IsEnabled = true;
                _mindMapConversionCancellationSource?.Dispose();
                _mindMapConversionCancellationSource = null;
            }
        }

        private static string BuildAiProgressText(
            BundledModelHostService.FlashcardProgress status,
            string? processingStatusKey,
            string? finalizingStatusKey,
            out bool useAnimation)
        {
            var statusKey = status.StatusKey;
            if (processingStatusKey is not null
                && string.Equals(statusKey, "ConvertToFlashcardsStatusProcessing", StringComparison.Ordinal))
            {
                statusKey = processingStatusKey;
            }
            else if (finalizingStatusKey is not null
                && string.Equals(statusKey, "ConvertToFlashcardsStatusFinalizing", StringComparison.Ordinal))
            {
                statusKey = finalizingStatusKey;
            }

            var baseText = LocalizationService.GetString(statusKey);
            useAnimation = true;

            if (status.ChunkIndex.HasValue
                && status.ChunkCount.HasValue
                && status.ChunkCount.Value > 0)
            {
                var chunkCount = Math.Max(1, status.ChunkCount.Value);
                var chunkIndex = Math.Clamp(status.ChunkIndex.Value, 1, chunkCount);
                baseText = $"({chunkIndex}/{chunkCount}) {baseText}";
            }

            if (status.GeneratedChars.HasValue)
            {
                return string.Format(
                    LocalizationService.GetString("ConvertToFlashcardsStatusGeneratedCharsFormat"),
                    baseText,
                    status.GeneratedChars.Value);
            }

            if (status.Percent.HasValue)
            {
                useAnimation = false;
                return string.Format(LocalizationService.GetString("ConvertToFlashcardsStatusPercentFormat"), baseText, status.Percent.Value);
            }

            return baseText;
        }

        private void ShowPersistentStatusIndicator(string message)
        {
            AnimateStatusIndicatorRow(expand: true);
            StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, null);
            StatusIndicatorText.Text = message;
            StatusIndicatorText.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, fadeIn);
        }

    private void ShowPersistentStatusIndicatorWithoutAnimation(string message)
    {
        AnimateStatusIndicatorRow(expand: true);
        StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, null);
        StatusIndicatorText.Opacity = 1;
        StatusIndicatorText.Text = message;
        StatusIndicatorText.Visibility = Visibility.Visible;
    }

        private void HideStatusIndicator()
        {
            if (StatusIndicatorText.Visibility != Visibility.Visible)
            {
                AnimateStatusIndicatorRow(expand: false);
                return;
            }

            StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, null);

            var fadeOut = new DoubleAnimation(StatusIndicatorText.Opacity, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (s, e) =>
            {
                StatusIndicatorText.Visibility = Visibility.Collapsed;
                StatusIndicatorText.Text = string.Empty;
                AnimateStatusIndicatorRow(expand: false);
            };
            StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, fadeOut);
        }

        private void AnimateStatusIndicatorRow(bool expand)
        {
            if (StatusIndicatorRow == null)
                return;

            var targetHeight = expand ? StatusIndicatorExpandedHeight : 0;
            var currentHeight = StatusIndicatorRow.ActualHeight;
            if (currentHeight <= 0)
                currentHeight = StatusIndicatorRow.Height.Value;

            if (Math.Abs(currentHeight - targetHeight) < 0.5)
            {
                StatusIndicatorRow.BeginAnimation(RowDefinition.HeightProperty, null);
                StatusIndicatorRow.Height = new GridLength(targetHeight, GridUnitType.Pixel);
                return;
            }

            var animation = new GridLengthAnimation
            {
                From = new GridLength(currentHeight, GridUnitType.Pixel),
                To = new GridLength(targetHeight, GridUnitType.Pixel),
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            StatusIndicatorRow.BeginAnimation(RowDefinition.HeightProperty, animation);
        }

        private void PerformFind(string query)
        {
            if (string.IsNullOrEmpty(query))
                return;

            // Clear previous selection
            var doc = ContentTextBox.Document;
            ClearAllHighlights();

            // Search for the query in the text
            var navigator = doc.ContentStart;
            while (navigator.CompareTo(doc.ContentEnd) < 0)
            {
                var text = navigator.GetTextInRun(LogicalDirection.Forward);
                if (!string.IsNullOrEmpty(text))
                {
                    var idx = text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = navigator.GetPositionAtOffset(idx);
                        var end = start.GetPositionAtOffset(query.Length);
                        if (start != null && end != null)
                        {
                            var foundRange = new TextRange(start, end);
                            foundRange.ApplyPropertyValue(TextElement.BackgroundProperty, System.Windows.Media.Brushes.Yellow);
                            // Scroll to selection
                            ContentTextBox.Selection.Select(start, end);
                            ContentTextBox.Focus();
                            return; // highlight first occurrence
                        }
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        // Find next occurrence after current selection. Wraps to start if needed.
        internal bool PerformFindNext(string query)
        {
            if (string.IsNullOrEmpty(query))
                return false;

            var doc = ContentTextBox.Document;
            ClearAllHighlights();

            TextPointer startPos = null;
            var sel = ContentTextBox.Selection;
            if (sel != null && !sel.IsEmpty)
            {
                startPos = sel.End;
            }
            else
            {
                startPos = doc.ContentStart;
            }

            // Search from startPos to end
            var navigator = startPos;
            while (navigator != null && navigator.CompareTo(doc.ContentEnd) < 0)
            {
                var text = navigator.GetTextInRun(LogicalDirection.Forward);
                if (!string.IsNullOrEmpty(text))
                {
                    var idx = text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = navigator.GetPositionAtOffset(idx);
                        var end = start.GetPositionAtOffset(query.Length);
                        if (start != null && end != null)
                        {
                            var foundRange = new TextRange(start, end);
                            foundRange.ApplyPropertyValue(TextElement.BackgroundProperty, System.Windows.Media.Brushes.Yellow);
                            ContentTextBox.Selection.Select(start, end);
                            ContentTextBox.Focus();
                            return true;
                        }
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }

            // Not found after current position - try from document start (wrap)
            navigator = doc.ContentStart;
            while (navigator != null && navigator.CompareTo(startPos) < 0)
            {
                var text = navigator.GetTextInRun(LogicalDirection.Forward);
                if (!string.IsNullOrEmpty(text))
                {
                    var idx = text.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        var start = navigator.GetPositionAtOffset(idx);
                        var end = start.GetPositionAtOffset(query.Length);
                        if (start != null && end != null)
                        {
                            var foundRange = new TextRange(start, end);
                            foundRange.ApplyPropertyValue(TextElement.BackgroundProperty, System.Windows.Media.Brushes.Yellow);
                            ContentTextBox.Selection.Select(start, end);
                            ContentTextBox.Focus();
                            return true;
                        }
                    }
                }
                navigator = navigator.GetNextContextPosition(LogicalDirection.Forward);
            }

            return false;
        }

        internal void PerformReplaceNext(string query, string replacement)
        {
            if (string.IsNullOrEmpty(query))
                return;

            // If current selection matches the search, replace it
            var sel = ContentTextBox.Selection;
            if (sel != null && !sel.IsEmpty && string.Equals(sel.Text, query, StringComparison.OrdinalIgnoreCase))
            {
                sel.Text = replacement ?? string.Empty;
            }

            // then find next
            PerformFindNext(query);
        }

        // Load data FROM a NoteDocument
        public void LoadFromDocument(NoteDocument document)
        {
            try
            {
                if (document != null)
                {
                    _isLoadingDocument = true;
                    _currentDocument = document; // Set current document
                    TitleTextBox.Text = document.Title;
                    TagsTextBox.Text = string.Join(", ", document.Tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim()));

                    if (!string.IsNullOrEmpty(document.Content))
                    {
                        TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);

                        try
                        {
                            // Try load as Base64 RTF, but verify decoded bytes look like RTF to avoid
                            // misinterpreting plain text that happens to be valid Base64.
                            byte[] bytes = Convert.FromBase64String(document.Content);
                            // Check for RTF header at start of decoded bytes ("{\rtf")
                            if (bytes.Length >= 5)
                            {
                                var hdr = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length));
                                if (!hdr.StartsWith("{\\rtf"))
                                    throw new FormatException();
                            }

                            using (MemoryStream ms = new MemoryStream(bytes))
                            {
                                tr.Load(ms, DataFormats.Rtf);
                            }
                        }
                        catch (FormatException)
                        {
                            // If not Base64, just load as plain text
                            tr.Text = document.Content;
                        }
                    }

                    var settings = AppSettingsService.Load();
                    var preferredFontFamily = string.IsNullOrWhiteSpace(settings.PreferredFontFamily)
                        ? "Segoe UI"
                        : settings.PreferredFontFamily;
                    var preferredFontSize = settings.PreferredFontSize > 0
                        ? settings.PreferredFontSize
                        : 14;

                    var targetFontFamily = string.IsNullOrWhiteSpace(document.FontFamily)
                        ? preferredFontFamily
                        : document.FontFamily;
                    var targetFontSize = document.FontSize > 0
                        ? document.FontSize
                        : preferredFontSize;

                    ContentTextBox.FontFamily = new FontFamily(targetFontFamily);
                    ContentTextBox.FontSize = targetFontSize;

                    document.FontFamily = ContentTextBox.FontFamily.Source;
                    document.FontSize = ContentTextBox.FontSize;

                    SyncFontSelectorsFromEditor();
                    UpdateFontButtonText();

                    // Initialize last saved content
                    MarkCurrentStateSaved();

                    // Apply theme colors to the loaded content
                    ApplyRichTextBoxTheme();

                    // Clear any selection and move caret to start
                    ContentTextBox.CaretPosition = ContentTextBox.Document.ContentStart;
                    MarkCurrentStateSaved();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading document: {ex.Message}");
                MessageBox.Show(
                    $"Error loading document: {ex.Message}",
                    "Load Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingDocument = false;
                UpdateEditedIndicator();
            }
        }

        // Print functionality
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use the FlowDocument from RichTextBox
                FlowDocument documentToPrint = ContentTextBox.Document;

                // Format the document for printing
                documentToPrint.PagePadding = new Thickness(50);

                // Create a PrintDialog
                PrintDialog pd = new PrintDialog();
                if (pd.ShowDialog() == true)
                {
                    documentToPrint.ColumnWidth = pd.PrintableAreaWidth;

                    // Print the document with fonts and formatting
                    pd.PrintDocument(((IDocumentPaginatorSource)documentToPrint).DocumentPaginator, TitleTextBox.Text);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("FailedToPrintNote")}\n\n{ex.Message}",
                    LocalizationService.GetString("PrintError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Save data TO a NoteDocument
        public void SaveToDocument(NoteDocument document)
        {
            if (document != null)
            {
                var previousContent = document.Content ?? string.Empty;
                var previousTitle = document.Title ?? string.Empty;
                var previousTags = document.Tags ?? new List<string>();
                var previousFontFamily = document.FontFamily ?? string.Empty;
                var previousFontSize = document.FontSize;

                var newTitle = TitleTextBox.Text;
                var newTags = ParseTags(TagsTextBox.Text);
                var newFontFamily = ContentTextBox.FontFamily.Source;
                var newFontSize = ContentTextBox.FontSize;

                TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream())
                {
                    tr.Save(ms, DataFormats.Rtf); // save as RTF
                    var newContent = Convert.ToBase64String(ms.ToArray());
                    var contentChanged = !string.Equals(previousContent, newContent, StringComparison.Ordinal);
                    if (contentChanged)
                    {
                        AppendEditHistoryVersion(document, previousContent);
                    }

                    var titleChanged = !string.Equals(previousTitle, newTitle, StringComparison.Ordinal);
                    var tagsChanged = !AreTagListsEqual(previousTags, newTags);
                    var fontChanged = !string.Equals(previousFontFamily, newFontFamily, StringComparison.Ordinal)
                        || !previousFontSize.Equals(newFontSize);

                    if (contentChanged || titleChanged || tagsChanged || fontChanged)
                    {
                        document.LastModified = DateTime.Now;
                    }

                    document.Title = newTitle;
                    document.Tags = newTags;
                    document.Content = newContent;
                }

                document.FontFamily = newFontFamily;
                document.FontSize = newFontSize;
            }
        }

        private static bool AreTagListsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            left ??= Array.Empty<string>();
            right ??= Array.Empty<string>();

            if (left.Count != right.Count)
                return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private static void AppendEditHistoryVersion(NoteDocument document, string previousContent)
        {
            document.EditHistory ??= new List<NoteEditHistoryEntry>();
            document.EditHistory.Add(new NoteEditHistoryEntry
            {
                Timestamp = DateTime.UtcNow,
                Content = previousContent
            });

            if (document.EditHistory.Count > MaxEditHistoryEntries)
            {
                var overflow = document.EditHistory.Count - MaxEditHistoryEntries;
                document.EditHistory.RemoveRange(0, overflow);
            }
        }

        // Initialize auto-save timer
        private void InitializeAutoSave()
        {
            // Load auto-save setting from app settings
            var settings = AppSettingsService.Load();
            _isAutoSaveEnabled = settings.EnableAutoSave;

            // Start the auto-save timer if enabled
            if (_isAutoSaveEnabled)
            {
                StartAutoSaveTimer();
            }

            // Hook into content changes to track modifications
            ContentTextBox.TextChanged += ContentTextBox_TextChanged;
            ContentTextBox.PreviewTextInput += ContentTextBox_PreviewTextInput;
            ContentTextBox.PreviewKeyDown += ContentTextBox_PreviewKeyDown;
        }

        private void ContentTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Text))
            {
                NoteCards.Services.ActivityTracker.RecordTyping(e.Text.Length, 0);
            }
        }

        private void ContentTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter)
            {
                NoteCards.Services.ActivityTracker.RecordTyping(0, 1);
            }
        }

        // Start the auto-save timer
        private void StartAutoSaveTimer()
        {
            if (_isAutoSaveEnabled)
            {
                // Repeating timer: dueTime=30s (first fire), period=30s (repeat interval)
                _autoSaveTimer = new System.Threading.Timer(
                    AutoSaveCallback,
                    null,
                    AutoSaveIntervalMs,  // dueTime: first fire after 30 seconds
                    AutoSaveIntervalMs); // period: repeat every 30 seconds
            }
        }

        // Stop the auto-save timer
        private void StopAutoSaveTimer()
        {
            _autoSaveTimer?.Dispose();
            _autoSaveTimer = null;
        }

        private static void BeginAiGeneration()
        {
            _activeAiGenerationCount++;
            if (_activeAiGenerationCount == 1)
                AiGenerationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private static void EndAiGeneration()
        {
            if (_activeAiGenerationCount <= 0)
                return;

            _activeAiGenerationCount--;
            if (_activeAiGenerationCount == 0)
                AiGenerationStateChanged?.Invoke(null, EventArgs.Empty);
        }

        private bool TemporarilyDisableAutoSaveForAi()
        {
            if (!_isAutoSaveEnabled)
                return false;

            _isAutoSaveEnabled = false;
            StopAutoSaveTimer();
            return true;
        }

        private void RestoreAutoSaveAfterAi(ref bool shouldRestore)
        {
            if (!shouldRestore)
                return;

            shouldRestore = false;
            if (!AppSettingsService.Load().EnableAutoSave)
                return;

            _isAutoSaveEnabled = true;
            StopAutoSaveTimer();
            StartAutoSaveTimer();
        }

        // Content changed event handler - apply theme and update counter
        private void ContentTextBox_TextChanged_Old(object sender, TextChangedEventArgs e)
        {
            UpdateCounter();
        }

        private void AutoSaveCallback(object? state)
        {
            // Debug: Log timer firing
            System.Diagnostics.Debug.WriteLine($"[AutoSave] Timer fired at {DateTime.Now:HH:mm:ss}");

            // Switch to UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_isAutoSaveEnabled && _currentDocument != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoSave] Checking if content changed...");

                    if (HasContentChanged())
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoSave] Content changed, performing save...");
                        PerformAutoSave();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[AutoSave] No changes detected since last save");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[AutoSave] Disabled or no document: Enabled={_isAutoSaveEnabled}, Doc={_currentDocument != null}");
                }
            });
        }

        // Check if content has changed since last save
        private bool HasContentChanged()
        {
            return HasUnsavedChanges();
        }

        private bool HasUnsavedChanges()
        {
            if (_isLoadingDocument)
                return false;

            var currentSnapshot = GetEditorSnapshot();
            return !string.Equals(currentSnapshot, _lastSavedSnapshot, StringComparison.Ordinal);
        }

        private void MarkCurrentStateSaved()
        {
            _lastSavedContent = GetContentAsText();
            _lastSavedSnapshot = GetEditorSnapshot();
            UpdateEditedIndicator();
        }

        private void UpdateEditedIndicator()
        {
            if (_isLoadingDocument || EditedIndicatorText is null)
                return;

            EditedIndicatorText.Visibility = HasUnsavedChanges()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private string GetEditorSnapshot()
        {
            return string.Join(
                '\u001F',
                TitleTextBox.Text,
                string.Join('\u001E', ParseTags(TagsTextBox.Text)),
                ContentTextBox.FontFamily.Source,
                ContentTextBox.FontSize.ToString(CultureInfo.InvariantCulture),
                GetContentAsRtfBase64());
        }

        private string GetContentAsRtfBase64()
        {
            var textRange = new TextRange(
                ContentTextBox.Document.ContentStart,
                ContentTextBox.Document.ContentEnd);

            using var stream = new MemoryStream();
            textRange.Save(stream, DataFormats.Rtf);
            return Convert.ToBase64String(stream.ToArray());
        }

        // Get current content as plain text for comparison
        private string GetContentAsText()
        {
            var textRange = new TextRange(
                ContentTextBox.Document.ContentStart,
                ContentTextBox.Document.ContentEnd);
            return textRange.Text;
        }

        private string GetEditorPlainText()
        {
            var text = GetContentAsText();

            if (text.EndsWith("\r\n", StringComparison.Ordinal))
                text = text[..^2];
            else if (text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal))
                text = text[..^1];

            return text.Trim();
        }

        private string GetEditorAiText()
        {
            var sb = new StringBuilder();
            foreach (Block block in ContentTextBox.Document.Blocks)
                AppendBlockTextForAi(block, sb);

            var text = sb.ToString().Replace('\uFFFC', ' ');

            if (text.EndsWith("\r\n", StringComparison.Ordinal))
                text = text[..^2];
            else if (text.EndsWith("\n", StringComparison.Ordinal) || text.EndsWith("\r", StringComparison.Ordinal))
                text = text[..^1];

            return text.Trim();
        }

        private static void AppendBlockTextForAi(Block block, StringBuilder target)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    AppendInlineTextForAi(paragraph.Inlines, target);
                    target.AppendLine();
                    break;
                case Section section:
                    foreach (Block child in section.Blocks)
                        AppendBlockTextForAi(child, target);
                    target.AppendLine();
                    break;
                case System.Windows.Documents.List list:
                    foreach (ListItem item in list.ListItems)
                    {
                        foreach (Block child in item.Blocks)
                            AppendBlockTextForAi(child, target);
                    }
                    target.AppendLine();
                    break;
                case Table table:
                    foreach (var rowGroup in table.RowGroups)
                    {
                        foreach (var row in rowGroup.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (Block child in cell.Blocks)
                                    AppendBlockTextForAi(child, target);
                            }
                        }
                    }
                    target.AppendLine();
                    break;
                case BlockUIContainer:
                    break;
            }
        }

        private static void AppendInlineTextForAi(InlineCollection inlines, StringBuilder target)
        {
            foreach (Inline inline in inlines)
            {
                switch (inline)
                {
                    case Run run:
                        target.Append(run.Text);
                        break;
                    case LineBreak:
                        target.AppendLine();
                        break;
                    case Span span:
                        AppendInlineTextForAi(span.Inlines, target);
                        break;
                    case AnchoredBlock anchoredBlock:
                        foreach (Block block in anchoredBlock.Blocks)
                            AppendBlockTextForAi(block, target);
                        break;
                    case InlineUIContainer:
                        break;
                }
            }
        }

        // Perform auto-save
        private void PerformAutoSave()
        {
            try
            {
                if (_currentDocument != null)
                {
                    // Save to document
                    SaveToDocument(_currentDocument);

                    // Update last saved content
                    MarkCurrentStateSaved();
                    _lastAutoSaveTime = DateTime.Now;

                    // Show visual indicator
                    ShowAutoSaveIndicator();

                    // Raise event so MainWindow can refresh the note card
                    DocumentAutoSaved?.Invoke(_currentDocument);

                    // Save notes to disk (via MainViewModel)
                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                    {
                        mainVm.RefreshTagFiltersAfterNoteEdit();
                        mainVm.SaveNotes();
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but don't disturb user during auto-save
                System.Diagnostics.Debug.WriteLine($"Auto-save failed: {ex.Message}");
            }
        }

        // Show visual auto-save indicator
        private void ShowAutoSaveIndicator()
        {
            ShowStatusIndicator(LocalizationService.GetString("AutoSaving"));
        }

        // Enable/disable auto-save
        public void SetAutoSaveEnabled(bool enabled)
        {
            _isAutoSaveEnabled = enabled;

            if (enabled)
            {
                StartAutoSaveTimer();
            }
            else
            {
                StopAutoSaveTimer();
            }

            // Save preference to app settings
            var settings = AppSettingsService.Load();
            settings.EnableAutoSave = enabled;
            AppSettingsService.Save(settings);
        }

        // Check if auto-save is enabled
        public bool IsAutoSaveEnabled() => _isAutoSaveEnabled;

        // Set the current document being edited
        public void SetCurrentDocument(NoteDocument document)
        {
            _currentDocument = document;
            MarkCurrentStateSaved();
        }

        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var exported = ExportToPdf(TitleTextBox.Text);
                if (exported)
                {
                    MessageBox.Show(
                        LocalizationService.GetString("PdfExportComplete"),
                        LocalizationService.GetString("Success"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("FailedToExportPdf")}\n\n{ex.Message}",
                    LocalizationService.GetString("ExportError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private bool ExportToPdf(string title)
        {
            var exportDoc = new FlowDocument();
            exportDoc.PageWidth = 816;  // A4 at 96 DPI
            exportDoc.PageHeight = 1056;
            exportDoc.ColumnWidth = 680;
            exportDoc.PagePadding = new Thickness(60);

            // Add title as FIRST paragraph (from TitleTextBox only)
            var titleParagraph = new Paragraph(new Run(title))
            {
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 30)
            };
            exportDoc.Blocks.Add(titleParagraph);

            // Add separator line
            var separator = new Paragraph(new Run("────────────────────────────────────────────────"))
            {
                FontSize = 10,
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 20)
            };
            exportDoc.Blocks.Add(separator);

            var contentClone = CloneFlowDocument(ContentTextBox.Document);
            if (contentClone != null && contentClone.Blocks.FirstBlock != null)
            {
                while (contentClone.Blocks.FirstBlock != null)
                {
                    var block = contentClone.Blocks.FirstBlock;
                    contentClone.Blocks.Remove(block);
                    exportDoc.Blocks.Add(block);
                }
            }
            else
            {
                exportDoc.Blocks.Add(new Paragraph(new Run(LocalizationService.GetString("NoContent")))
                {
                    FontSize = 12,
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
            }

            var printDialog = new PrintDialog();

            var printQueue = new System.Printing.PrintQueue(
                new System.Printing.PrintServer(),
                "Microsoft Print to PDF");

            printDialog.PrintQueue = printQueue;
            var queuedJobsBefore = GetQueuedJobCount(printQueue);

            printDialog.PrintDocument(
                ((IDocumentPaginatorSource)exportDoc).DocumentPaginator,
                title);

            System.Threading.Thread.Sleep(150);
            var queuedJobsAfter = GetQueuedJobCount(printQueue);

            if (queuedJobsBefore < 0 || queuedJobsAfter < 0)
                return true;

            return queuedJobsAfter > queuedJobsBefore;
        }

        private static int GetQueuedJobCount(System.Printing.PrintQueue printQueue)
        {
            try
            {
                printQueue.Refresh();
                return printQueue.GetPrintJobInfoCollection().Count();
            }
            catch
            {
                return -1;
            }
        }

        private static List<string> ParseTags(string? rawTags)
        {
            if (string.IsNullOrWhiteSpace(rawTags))
                return new List<string>();

            return rawTags
                .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static FlowDocument? CloneFlowDocument(FlowDocument source)
        {
            var xaml = XamlWriter.Save(source);
            return XamlReader.Parse(xaml) as FlowDocument;
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentTextBox.CanUndo)
                ContentTextBox.Undo();
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentTextBox.CanRedo)
                ContentTextBox.Redo();
        }

        private void EditHistoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocument == null)
                return;

            if (_currentDocument.EditHistory == null || _currentDocument.EditHistory.Count == 0)
            {
                MessageBox.Show(
                    LocalizationService.GetString("NoEditHistory"),
                    LocalizationService.GetString("EditHistoryTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new EditHistoryWindow(_currentDocument.EditHistory)
            {
                Owner = GetDialogOwnerWindow()
            };

            if (dialog.ShowDialog() != true || dialog.SelectedVersion == null)
                return;

            ApplyContentToEditor(dialog.SelectedVersion.Content);
            SaveToDocument(_currentDocument);
            MarkCurrentStateSaved();

            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
            {
                mainVm.SaveNotes();
            }

            DocumentAutoSaved?.Invoke(_currentDocument);
            ShowStatusIndicator(LocalizationService.GetString("VersionRestored"));
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentDocument();

            // Close the window after saving
            _allowCloseWithoutPrompt = true;
            if (_isHostedInTab)
                CloseRequested?.Invoke(this);
            else
                Close();
        }

        private bool SaveCurrentDocument()
        {
            if (_currentDocument == null)
                return true;

            SaveToDocument(_currentDocument);
            MarkCurrentStateSaved();

            DocumentAutoSaved?.Invoke(_currentDocument);

            if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
            {
                mainVm.RefreshTagFiltersAfterNoteEdit();
                mainVm.SaveNotes();
            }

            ShowStatusIndicator(LocalizationService.GetString("Saved"));
            return true;
        }

        private void ApplyContentToEditor(string? content)
        {
            TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);

            if (string.IsNullOrEmpty(content))
            {
                tr.Text = string.Empty;
                return;
            }

            try
            {
                byte[] bytes = Convert.FromBase64String(content);
                if (bytes.Length >= 5)
                {
                    var hdr = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(5, bytes.Length));
                    if (!hdr.StartsWith("{\\rtf"))
                        throw new FormatException();
                }

                using (MemoryStream ms = new MemoryStream(bytes))
                {
                    tr.Load(ms, DataFormats.Rtf);
                }
            }
            catch (FormatException)
            {
                tr.Text = content;
            }
        }

        private void ShowStatusIndicator(string message)
        {
            ShowPersistentStatusIndicator(message);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800))
            {
                BeginTime = TimeSpan.FromSeconds(1.15),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            fadeOut.Completed += (s, e) =>
            {
                StatusIndicatorText.Visibility = Visibility.Collapsed;
                StatusIndicatorText.Text = string.Empty;
                AnimateStatusIndicatorRow(expand: false);
            };
            StatusIndicatorText.BeginAnimation(TextBlock.OpacityProperty, fadeOut);
        }

        private void FontFamilyBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingFontSelectors)
                return;

            if (FontFamilyBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string? fontName = item.Content.ToString();
                if (!string.IsNullOrEmpty(fontName))
                {
                    if (ContentTextBox.Selection != null && !ContentTextBox.Selection.IsEmpty)
                    {
                        ContentTextBox.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontName));
                    }
                    else
                    {
                        ContentTextBox.FontFamily = new FontFamily(fontName);
                    }

                    SavePreferredTypography(ContentTextBox.FontFamily.Source, ContentTextBox.FontSize);
                    SyncFontSelectorsFromEditor();
                    UpdateFontButtonText();
                    UpdateEditedIndicator();
                }
            }
        }

        private void FontSizeBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingFontSelectors)
                return;

            if (FontSizeBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string? sizeText = item.Content.ToString();
                if (!string.IsNullOrEmpty(sizeText)
                    && double.TryParse(sizeText, NumberStyles.Float, CultureInfo.InvariantCulture, out double size))
                {
                    if (ContentTextBox.Selection != null && !ContentTextBox.Selection.IsEmpty)
                    {
                        ContentTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    }
                    else
                    {
                        ContentTextBox.FontSize = size;
                    }

                    SavePreferredTypography(ContentTextBox.FontFamily.Source, ContentTextBox.FontSize);
                    SyncFontSelectorsFromEditor();
                    UpdateFontButtonText();
                    UpdateEditedIndicator();
                }
            }
        }

        private void FontSettings_Click(object sender, RoutedEventArgs e)
        {
            // Toggle panel visibility
            FontPanel.Visibility = FontPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;

            UpdateFontButtonText();
        }

        private void ToggleBoldButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSelectionTextStyle(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
        }

        private void ToggleItalicButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSelectionTextStyle(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);
        }

        private void ToggleUnderlineButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = ContentTextBox.Selection;
            var currentValue = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            var isUnderlined = currentValue != DependencyProperty.UnsetValue
                && currentValue is TextDecorationCollection decorations
                && decorations == TextDecorations.Underline;

            selection.ApplyPropertyValue(
                Inline.TextDecorationsProperty,
                isUnderlined ? DependencyProperty.UnsetValue : TextDecorations.Underline);

            UpdateEditedIndicator();
            ContentTextBox.Focus();
        }

        private void ToggleSelectionTextStyle(DependencyProperty property, object enabledValue, object disabledValue)
        {
            var selection = ContentTextBox.Selection;
            var currentValue = selection.GetPropertyValue(property);
            var shouldEnable = currentValue == DependencyProperty.UnsetValue || !Equals(currentValue, enabledValue);

            selection.ApplyPropertyValue(property, shouldEnable ? enabledValue : disabledValue);
            UpdateEditedIndicator();
            ContentTextBox.Focus();
        }

        private void UpdateFontButtonText()
        {
            if (FindName("FontButton") is Button fontButton)
            {
                fontButton.ToolTip = string.Format(LocalizationService.GetString("FontButtonFormat"), ContentTextBox.FontFamily.Source, ContentTextBox.FontSize);
            }
        }

        private void SyncFontSelectorsFromEditor()
        {
            _isSyncingFontSelectors = true;

            try
            {
                SelectComboBoxItemByContent(FontFamilyBox, ContentTextBox.FontFamily.Source, "Segoe UI", StringComparison.OrdinalIgnoreCase);

                var fontSizeText = Math.Round(ContentTextBox.FontSize).ToString(CultureInfo.InvariantCulture);
                SelectComboBoxItemByContent(FontSizeBox, fontSizeText, "14", StringComparison.Ordinal);
            }
            finally
            {
                _isSyncingFontSelectors = false;
            }
        }

        private static void SelectComboBoxItemByContent(ComboBox comboBox, string preferredContent, string fallbackContent, StringComparison comparison)
        {
            if (!TrySelectComboBoxItemByContent(comboBox, preferredContent, comparison)
                && !TrySelectComboBoxItemByContent(comboBox, fallbackContent, comparison)
                && comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private static bool TrySelectComboBoxItemByContent(ComboBox comboBox, string content, StringComparison comparison)
        {
            foreach (var option in comboBox.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(option.Content?.ToString(), content, comparison))
                {
                    comboBox.SelectedItem = option;
                    return true;
                }
            }

            return false;
        }

        private static void SavePreferredTypography(string fontFamily, double fontSize)
        {
            var settings = AppSettingsService.Load();
            settings.PreferredFontFamily = string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily;
            settings.PreferredFontSize = fontSize > 0 ? fontSize : 14;
            AppSettingsService.Save(settings);
        }

        private void OpenFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = LocalizationService.GetString("OpenFileDialogFilter");
            if (dlg.ShowDialog() != true)
                return;

            var path = dlg.FileName;
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string content = string.Empty;

                if (ext == ".rtf")
                {
                    var bytes = System.IO.File.ReadAllBytes(path);
                    // Load RTF directly into RichTextBox
                    TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                    using (var ms = new MemoryStream(bytes))
                    {
                        tr.Load(ms, DataFormats.Rtf);
                    }
                }
                else
                {
                    // plain text
                    content = File.ReadAllText(path);
                    TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                    tr.Text = content;
                }
            }
            catch
            {
                MessageBox.Show(LocalizationService.GetString("FailedToOpenFile"), LocalizationService.GetString("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearContentButton_Click(object sender, RoutedEventArgs e)
        {
            // Show confirmation dialog
            var mainWindow = Application.Current.MainWindow;
            var previousEditorOpacity = Opacity;
            var previousMainOpacity = mainWindow is not null ? mainWindow.Opacity : 1.0;

            try
            {
                Opacity = 0.82;
                if (mainWindow is not null && !ReferenceEquals(mainWindow, this))
                    mainWindow.Opacity = 0.82;

                var dlg = new Views.ClearContentConfirmationDialog
                {
                    Owner = GetDialogOwnerWindow()
                };

                if (dlg.ShowDialog() == true)
                {
                    // Clear all content from the RichTextBox
                    TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                    tr.Text = string.Empty;
                }
            }
            finally
            {
                Opacity = previousEditorOpacity;
                if (mainWindow is not null && !ReferenceEquals(mainWindow, this))
                    mainWindow.Opacity = previousMainOpacity;
            }
        }

        private void InsertImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = LocalizationService.GetString("ImageOpenFileDialogFilter");
            dlg.Title = LocalizationService.GetString("SelectImageToInsert");

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                InsertImageFromFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{LocalizationService.GetString("FailedToInsertImage")}\n\n{ex.Message}",
                    LocalizationService.GetString("ImageInsertError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InsertImageFromFile(string imagePath)
        {
            try
            {
                // Create image from file
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                // Set a max width/height for the image to fit in the note
                const double maxWidth = 400;
                const double maxHeight = 300;

                double displayWidth = bitmap.PixelWidth;
                double displayHeight = bitmap.PixelHeight;

                // Scale down if too large
                if (displayWidth > maxWidth || displayHeight > maxHeight)
                {
                    double aspectRatio = displayWidth / displayHeight;
                    if (displayWidth > maxWidth)
                    {
                        displayWidth = maxWidth;
                        displayHeight = maxWidth / aspectRatio;
                    }
                    if (displayHeight > maxHeight)
                    {
                        displayHeight = maxHeight;
                        displayWidth = maxHeight * aspectRatio;
                    }
                }

                // Create Image control
                Image image = new Image
                {
                    Source = bitmap,
                    Width = displayWidth,
                    Height = displayHeight,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(0, 10, 0, 10),
                    ContextMenu = CreateImageContextMenu()
                };

                // Make image draggable and resizable
                MakeImageDraggable(image);

                // Create a container for the image
                InlineUIContainer container = new InlineUIContainer(image)
                {
                    BaselineAlignment = BaselineAlignment.Bottom
                };

                // Get the current paragraph or create a new one
                TextPointer caretPosition = ContentTextBox.CaretPosition;
                Paragraph currentParagraph = caretPosition.Paragraph;

                if (currentParagraph == null)
                {
                    // If no paragraph exists, create one
                    currentParagraph = new Paragraph();
                    ContentTextBox.Document.Blocks.Add(currentParagraph);
                }

                // Insert the image container at the caret position
                var insertionPosition = caretPosition.GetInsertionPosition(LogicalDirection.Forward);
                if (currentParagraph.Inlines.FirstInline != null && insertionPosition != null)
                {
                    currentParagraph.Inlines.InsertBefore(currentParagraph.Inlines.FirstInline, container);
                }
                else
                {
                    currentParagraph.Inlines.Add(container);
                }

                // Add a new line after image for better spacing
                currentParagraph.Inlines.Add(new LineBreak());

                // Move caret after the image
                ContentTextBox.CaretPosition = container.ContentEnd.GetNextInsertionPosition(LogicalDirection.Forward) ?? ContentTextBox.Document.ContentEnd;
            }
            catch (Exception ex)
            {
                throw new Exception($"{LocalizationService.GetString("ErrorLoadingImage")}: {ex.Message}", ex);
            }
        }

        private ContextMenu CreateImageContextMenu()
        {
            var contextMenu = new ContextMenu();

            var removeItem = new MenuItem
            {
                Header = LocalizationService.GetString("RemoveImage")
            };
            removeItem.Click += (s, e) =>
            {
                // Get the sender's parent
                if (s is MenuItem menuItem && menuItem.Parent is ContextMenu cm)
                {
                    if (cm.PlacementTarget is Image img)
                    {
                        // Find and remove the InlineUIContainer
                        var doc = ContentTextBox.Document;
                        var start = doc.ContentStart;
                        var end = doc.ContentEnd;

                        var navigator = start.GetNextInsertionPosition(LogicalDirection.Forward);
                        while (navigator != null && navigator.CompareTo(end) < 0)
                        {
                            if (navigator.Parent is InlineUIContainer container && container.Child == img)
                            {
                                ((Paragraph)container.Parent)?.Inlines.Remove(container);
                                break;
                            }
                            navigator = navigator.GetNextInsertionPosition(LogicalDirection.Forward);
                        }
                    }
                }
            };

            contextMenu.Items.Add(removeItem);
            return contextMenu;
        }

        private void MakeImageDraggable(Image image)
        {
            bool isDragging = false;
            double lastX = 0;
            double lastY = 0;

            image.MouseDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    isDragging = true;
                    lastX = e.GetPosition(ContentTextBox).X;
                    lastY = e.GetPosition(ContentTextBox).Y;
                }
            };

            image.MouseMove += (s, e) =>
            {
                if (isDragging && e.LeftButton == MouseButtonState.Pressed)
                {
                    var currentPos = e.GetPosition(ContentTextBox);
                    double deltaX = currentPos.X - lastX;
                    double deltaY = currentPos.Y - lastY;

                    // Resize on drag (hold Shift key for resize)
                    if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                    {
                        image.Width = Math.Max(50, image.Width + deltaX);
                        image.Height = Math.Max(50, image.Height + deltaY);
                    }

                    lastX = currentPos.X;
                    lastY = currentPos.Y;
                }
            };

            image.MouseUp += (s, e) =>
            {
                isDragging = false;
            };

            // Add tooltip for resize instruction
            image.ToolTip = LocalizationService.GetString("ImageResizeTooltip");
        }

        private double _zoomLevel = 1.0;

        private void ContentTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                {
                    _zoomLevel += 0.1;
                }
                else if (e.Delta < 0)
                {
                    _zoomLevel -= 0.1;
                }

                ApplyZoom();
                e.Handled = true;
            }
        }

        private void ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel += 0.1;
            ApplyZoom();
        }

        private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel -= 0.1;
            ApplyZoom();
        }

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            _zoomLevel = 1.0;
            ApplyZoom();
        }

        private void ApplyZoom()
        {
            if (_zoomLevel < 0.5) _zoomLevel = 0.5;
            if (_zoomLevel > 3.0) _zoomLevel = 3.0;

            ContentTextBox.LayoutTransform = new ScaleTransform(_zoomLevel, _zoomLevel);
        }
        private bool _isWordWrapEnabled = true;

        private void ToggleWordWrap_Click(object sender, RoutedEventArgs e)
        {
            _isWordWrapEnabled = !_isWordWrapEnabled;

            if (_isWordWrapEnabled)
            {
               
                ContentTextBox.Document.PageWidth = double.NaN;
            }
            else
            {
                
                ContentTextBox.Document.PageWidth = 1000;
            }
        }

    }
}
