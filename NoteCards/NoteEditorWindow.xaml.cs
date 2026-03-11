using NoteCards.Models;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards
{
    public partial class NoteEditorWindow : Window
    {
        private bool? _pendingDialogResult = null;
        private bool _isPlayingCloseAnimation = false;

        public NoteEditorWindow()
        {
            InitializeComponent();
        }

        private void NoteEditorWindow_Closing(object sender, CancelEventArgs e)
        {
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
            // Open a lightweight input dialog to search inside the note
            var input = new Views.SimpleInputDialog("Find in note", "Enter text to find:");
            input.Owner = this;
            if (input.ShowDialog() == true)
            {
                var query = input.InputText;
                PerformFind(query);
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

        // Load data FROM a NoteDocument
        public void LoadFromDocument(NoteDocument document)
        {
            if (document != null)
            {
                TitleTextBox.Text = document.Title;

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
                    $"Failed to print note:\n\n{ex.Message}",
                    "Print Error",
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
            if (Font != null)
            {
                Font.Content = $"Font: {ContentTextBox.FontFamily.Source}, {ContentTextBox.FontSize}";
            }
        }

        private void OpenFromFileButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Text Files (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|All files (*.*)|*.*";
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
                MessageBox.Show("Failed to open file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
