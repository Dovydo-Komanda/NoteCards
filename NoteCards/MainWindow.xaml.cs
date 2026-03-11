using System.Windows;
using NoteCards.ViewModels;
using System.Windows.Controls;

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
            // Set DataContext so EnableScrollbar binding works
            editor.DataContext = this.DataContext;

            editor.LoadFromDocument(noteViewModel.Document);

            if (editor.ShowDialog() == true)
            {
                editor.SaveToDocument(noteViewModel.Document);

                var vm = this.DataContext as MainViewModel;
#pragma warning disable CS8602
                var notesList = vm.Notes.ToList();
#pragma warning restore CS8602

                vm.Notes.Clear();

                foreach (var note in notesList)
                    vm.Notes.Add(note);
                    vm.RefreshRecentNotes();
            }
        }

        // Settings menu button click handler
        private void SettingsMenuButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;
            var settingsPanel = this.FindName("SettingsPanelControl") as FrameworkElement;
            if (settingsPanel != null)
            {
                settingsPanel.DataContext = this.DataContext;
                settingsPanel.Visibility = Visibility.Visible;
            }
        }
        private void RecentNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is NoteCardViewModel noteVm)
                OpenNoteEditor(noteVm);
        }
    }
}