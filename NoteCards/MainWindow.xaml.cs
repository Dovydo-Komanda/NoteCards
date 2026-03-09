using System.Windows;
using NoteCards.ViewModels;

namespace NoteCards
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void HamburgerButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = !HamburgerPopup.IsOpen;
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;
            var about = new Views.AboutWindow { Owner = this };
            about.ShowDialog();
        }

        // Open editor for a specific note card
        public void OpenNoteEditor(NoteCardViewModel noteViewModel)
        {
            var editor = new NoteEditorWindow();

            // Load existing data from the NoteDocument
            editor.LoadFromDocument(noteViewModel.Document);

            // Show the editor as a dialog
            if (editor.ShowDialog() == true)
            {
                // Save changes back to the Document
                editor.SaveToDocument(noteViewModel.Document);

                // Notify UI that properties changed (so the card updates)
                // Since Title/Content are read-only wrappers, we need to trigger refresh
                noteViewModel.Document.Title = noteViewModel.Document.Title; // This won't work...

                // Force UI refresh by reassigning the collection
                var vm = this.DataContext as MainViewModel;
                var notesList = vm.Notes.ToList(); // Copy list
                vm.Notes.Clear();
                foreach (var note in notesList)
                    vm.Notes.Add(note);
            }
        }
    }
}