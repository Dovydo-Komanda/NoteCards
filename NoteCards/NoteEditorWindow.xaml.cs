using Microsoft.Win32;
using NoteCards.Localization;
using NoteCards.Models;
using NoteCards.Services;
using NoteCards.ViewModels;
using System.ComponentModel;
using System.IO;
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
        // Last search/replace state for Find Next / Replace Next functionality
        private string? _lastSearchQuery = null;
        private string? _lastReplacementText = null;

        // Auto-save fields
        public event Action<NoteDocument>? DocumentAutoSaved;
        private System.Threading.Timer? _autoSaveTimer;
        private bool _isAutoSaveEnabled = true;
        private const int AutoSaveIntervalMs = 30000; // 30 seconds
        private DateTime _lastAutoSaveTime = DateTime.MinValue;
        private string _lastSavedContent = string.Empty;
        private NoteDocument? _currentDocument;

        public NoteEditorWindow()
        {
            InitializeComponent();
            InitializeAutoSave();
        }

        private void NoteEditorWindow_Closing(object sender, CancelEventArgs e)
        {
            StopAutoSaveTimer();

            if (_isPlayingCloseAnimation)
                return;

            e.Cancel = true;
            AnimateAndClose();
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

        private void ContentTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            // If selection is empty (deselected), clear highlights
            var sel = ContentTextBox.Selection;
            if (sel == null || sel.IsEmpty)
            {
                ClearAllHighlights();
            }
        }

        private void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            // Open combined Find/Replace dialog
            var dlg = new Views.SearchReplaceDialogLocalized(_lastSearchQuery, _lastReplacementText);
            dlg.Owner = this;
            var res = dlg.ShowDialog();
            if (res == true)
            {
                // save last used values
                _lastSearchQuery = dlg.SearchText;
                _lastReplacementText = dlg.ReplacementText;
            }
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
            if (document != null)
            {
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

                ContentTextBox.FontFamily = new FontFamily(document.FontFamily);
                ContentTextBox.FontSize = document.FontSize;
                UpdateFontButtonText();

                // Initialize last saved content
                _lastSavedContent = GetContentAsText();
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
                document.Title = TitleTextBox.Text;
                document.Tags = ParseTags(TagsTextBox.Text);
                document.LastModified = DateTime.Now;
                TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream())
                {
                    tr.Save(ms, DataFormats.Rtf); // save as RTF
                    document.Content = Convert.ToBase64String(ms.ToArray());
                }

                document.FontFamily = ContentTextBox.FontFamily.Source;
                document.FontSize = ContentTextBox.FontSize;
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

        // Content changed event handler
        private void ContentTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Reset auto-save timer on content change (optional: only auto-save if user stops typing)
            // For now, we'll auto-save every 30 seconds regardless
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
            var currentContent = GetContentAsText();
            return currentContent != _lastSavedContent ||
                   TitleTextBox.Text != (_currentDocument?.Title ?? string.Empty);
        }

        // Get current content as plain text for comparison
        private string GetContentAsText()
        {
            var textRange = new TextRange(
                ContentTextBox.Document.ContentStart,
                ContentTextBox.Document.ContentEnd);
            return textRange.Text;
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
                    _lastSavedContent = GetContentAsText();
                    _lastAutoSaveTime = DateTime.Now;

                    // Show visual indicator
                    ShowAutoSaveIndicator();

                    // Raise event so MainWindow can refresh the note card
                    DocumentAutoSaved?.Invoke(_currentDocument);

                    // Save notes to disk (via MainViewModel)
                    if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                    {
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
            // Find or create the auto-save indicator
            var indicator = FindName("AutoSaveIndicator") as TextBlock;
            if (indicator == null)
            {
                // Create indicator if it doesn't exist
                indicator = new TextBlock
                {
                    Name = "AutoSaveIndicator",
                    FontSize = 11,
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(10, 0, 0, 0),
                    Visibility = Visibility.Collapsed
                };

                // Add to top bar (find the StackPanel with buttons)
                if (FindName("TopBarStackPanel") is StackPanel topBarPanel)
                {
                    topBarPanel.Children.Add(indicator);
                }
            }

            // Show "Auto-saving..." message
            indicator.Text = LocalizationService.GetString("AutoSaving");
            indicator.Visibility = Visibility.Visible;
            indicator.Opacity = 1;

            // Fade out after 2 seconds
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(2))
            {
                BeginTime = TimeSpan.FromSeconds(1)
            };
            fadeOut.Completed += (s, e) => indicator.Visibility = Visibility.Collapsed;
            indicator.BeginAnimation(TextBlock.OpacityProperty, fadeOut);
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
            _lastSavedContent = GetContentAsText();
        }

        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ExportToPdf(TitleTextBox.Text);

                MessageBox.Show("PDF export complete!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to export PDF:\n\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportToPdf(string title)
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
                exportDoc.Blocks.Add(new Paragraph(new Run("(No content)"))
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

            printDialog.PrintDocument(
                ((IDocumentPaginatorSource)exportDoc).DocumentPaginator,
                title);
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

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Perform manual save
            if (_currentDocument != null)
            {
                SaveToDocument(_currentDocument);
                _lastSavedContent = GetContentAsText();

                // Save to disk
                if (Application.Current.MainWindow?.DataContext is MainViewModel mainVm)
                {
                    mainVm.SaveNotes();
                }

                // Show auto-save indicator with "Saved" message
                var indicator = FindName("AutoSaveIndicator") as TextBlock;
                if (indicator != null)
                {
                    indicator.Text = LocalizationService.GetString("Saved");
                    indicator.Visibility = Visibility.Visible;
                    indicator.Opacity = 1;

                    var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromSeconds(1.5))
                    {
                        BeginTime = TimeSpan.FromSeconds(0.5)
                    };
                    fadeOut.Completed += (s, e) => indicator.Visibility = Visibility.Collapsed;
                    indicator.BeginAnimation(TextBlock.OpacityProperty, fadeOut);
                }
            }

            _pendingDialogResult = true;
            AnimateAndClose();
        }

        private void FontFamilyBox_Changed(object sender, SelectionChangedEventArgs e)
        {
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
                    UpdateFontButtonText();
                }
            }
        }

        private void FontSizeBox_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string? sizeText = item.Content.ToString();
                if (!string.IsNullOrEmpty(sizeText) && double.TryParse(sizeText, out double size))
                {
                    if (ContentTextBox.Selection != null && !ContentTextBox.Selection.IsEmpty)
                    {
                        ContentTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                    }
                    else
                    {
                        ContentTextBox.FontSize = size;
                    }
                    UpdateFontButtonText();
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

        private void UpdateFontButtonText()
        {
            if (FindName("FontButton") is Button fontButton)
            {
                fontButton.ToolTip = string.Format(LocalizationService.GetString("FontButtonFormat"), ContentTextBox.FontFamily.Source, ContentTextBox.FontSize);
            }
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
            var dlg = new Views.ClearContentConfirmationDialog();
            dlg.Owner = this;
            if (dlg.ShowDialog() == true)
            {
                // Clear all content from the RichTextBox
                TextRange tr = new TextRange(ContentTextBox.Document.ContentStart, ContentTextBox.Document.ContentEnd);
                tr.Text = string.Empty;
            }
        }

        private void InsertImageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.Filter = "Image files (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All files (*.*)|*.*";
            dlg.Title = "Select an image to insert";

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                InsertImageFromFile(dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to insert image:\n\n{ex.Message}",
                    "Image Insert Error",
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
                throw new Exception($"Error loading image: {ex.Message}", ex);
            }
        }

        private ContextMenu CreateImageContextMenu()
        {
            var contextMenu = new ContextMenu();

            var removeItem = new MenuItem
            {
                Header = "Remove Image"
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
            image.ToolTip = "Shift+Drag to resize image\nRight-click to remove";
        }
    }
}
