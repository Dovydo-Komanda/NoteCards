using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PrintDialog = System.Windows.Controls.PrintDialog;

namespace NoteCards
{
    public partial class PrintPreviewWindow : Window
    {
        private readonly string _title;
        private readonly string _content;

        public PrintPreviewWindow(string title, string content)
        {
            InitializeComponent();

            _title = title;
            _content = content;

            // Load preview
            PreviewTitle.Text = _title;
            PreviewContent.Text = _content;
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new PrintDialog();

                if (printDialog.ShowDialog() == true)
                {
                    // Create a document for printing
                    FlowDocument document = new FlowDocument();

                    // Add title
                    Paragraph titleParagraph = new Paragraph(
                        new Run(_title)
                    )
                    {
                        FontSize = 24,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.Black
                    };
                    document.Blocks.Add(titleParagraph);

                    // Add spacing
                    document.Blocks.Add(new Paragraph(new Run("\n")));

                    // Add content
                    Paragraph contentParagraph = new Paragraph(
                        new Run(_content)
                    )
                    {
                        FontSize = 12,
                        Foreground = Brushes.Black,
                        FontFamily = new FontFamily("Segoe UI")
                    };
                    document.Blocks.Add(contentParagraph);

                    // Print the document
                    printDialog.PrintDocument(
                        ((IDocumentPaginatorSource)document).DocumentPaginator,
                        _title
                    );

                    MessageBox.Show(
                        "Document sent to printer successfully!",
                        "Print Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    this.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to print:\n\n{ex.Message}",
                    "Print Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}