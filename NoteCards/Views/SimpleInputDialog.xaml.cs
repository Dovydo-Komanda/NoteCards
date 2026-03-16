using System.Windows;

namespace NoteCards.Views
{
    public partial class SimpleInputDialog : Window
    {
        public SimpleInputDialog(string title, string prompt)
        {
            InitializeComponent();
            this.Title = title;
            PromptText.Text = prompt;
        }

        public SimpleInputDialog(string title, string prompt, string initialText)
            : this(title, prompt)
        {
            InputBox.Text = initialText ?? string.Empty;
            InputBox.SelectAll();
            InputBox.Focus();
        }

        public string InputText => InputBox.Text ?? string.Empty;

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}
