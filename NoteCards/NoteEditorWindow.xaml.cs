using System.Windows;
using NoteCards.Models;

namespace NoteCards
{
    public partial class NoteEditorWindow : Window
    {
        public NoteEditorWindow()
        {
            InitializeComponent();
        }

        // Load data FROM a NoteDocument
        public void LoadFromDocument(NoteDocument document)
        {
            if (document != null)
            {
                TitleTextBox.Text = document.Title;
                ContentTextBox.Text = document.Content;
            }
        }

        // Print functionality
        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create a print preview window
                var printPreview = new PrintPreviewWindow(
                    TitleTextBox.Text,
                    ContentTextBox.Text
                );

                printPreview.Owner = this;
                printPreview.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open print preview:\n\n{ex.Message}",
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
                document.Content = ContentTextBox.Text;
            }
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentTextBox.CanUndo)
            {
                ContentTextBox.Undo();
            }
        }

        private void RedoButton_Click(object sender, RoutedEventArgs e)
        {
            if (ContentTextBox.CanRedo)
            {
                ContentTextBox.Redo();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
    }
}