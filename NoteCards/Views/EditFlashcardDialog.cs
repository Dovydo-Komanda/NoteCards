using NoteCards.Localization;
using System.Windows;

namespace NoteCards.Views
{
    public partial class EditFlashcardDialog : Window
    {
        public string Question
        {
            get => QuestionTextBox.Text;
            set => QuestionTextBox.Text = value;
        }

        public string Answer
        {
            get => AnswerTextBox.Text;
            set => AnswerTextBox.Text = value;
        }

        public string Category
        {
            get => CategoryTextBox.Text;
            set => CategoryTextBox.Text = value;
        }

        public EditFlashcardDialog()
        {
            InitializeComponent();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Question) || string.IsNullOrWhiteSpace(Answer))
            {
                MessageBox.Show(
                    LocalizationService.GetString("FlashcardEditEmptyError"),
                    LocalizationService.GetString("EditFlashcard"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}