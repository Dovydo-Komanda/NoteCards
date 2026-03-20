using System.Windows;

namespace NoteCards.Views
{
    public partial class SearchReplaceDialogLocalized : Window
    {
        public SearchReplaceDialogLocalized(string? initialSearch = null, string? initialReplace = null)
        {
            InitializeComponent();
            SearchBox.Text = initialSearch ?? string.Empty;
            ReplaceBox.Text = initialReplace ?? string.Empty;
            SearchBox.SelectAll();
            SearchBox.Focus();
        }

        public string SearchText => SearchBox.Text ?? string.Empty;
        public string ReplacementText => ReplaceBox.Text ?? string.Empty;

        private void FindNextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (this.Owner is NoteCards.NoteEditorWindow owner)
            {
                owner.PerformFindNext(SearchBox.Text);
            }
        }

        private void ReplaceNextBtn_Click(object sender, RoutedEventArgs e)
        {
            if (this.Owner is NoteCards.NoteEditorWindow owner)
            {
                owner.PerformReplaceNext(SearchBox.Text, ReplaceBox.Text);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true;
            this.Close();
        }
    }
}
