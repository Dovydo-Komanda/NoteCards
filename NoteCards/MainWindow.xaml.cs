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

        private void OpenFromFileMenuButton_Click(object sender, RoutedEventArgs e)
        {
            HamburgerPopup.IsOpen = false;

            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Filter = "Text Files (*.txt)|*.txt|Rich Text Format (*.rtf)|*.rtf|All files (*.*)|*.*";
            if (dlg.ShowDialog() != true)
                return;

            var path = dlg.FileName;
            try
            {
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                string content = string.Empty;
                bool isRtf = false;

                if (ext == ".rtf")
                {
                    var bytes = System.IO.File.ReadAllBytes(path);
                    content = Convert.ToBase64String(bytes);
                    isRtf = true;
                }
                else
                {
                    // Try strict UTF8 then fallbacks
                    var rawBytes = System.IO.File.ReadAllBytes(path);
                    try
                    {
                        content = new System.Text.UTF8Encoding(false, true).GetString(rawBytes);
                    }
                    catch
                    {
                        try
                        {
                            using (var ms = new System.IO.MemoryStream(rawBytes))
                            using (var sr = new System.IO.StreamReader(ms, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                            {
                                content = sr.ReadToEnd();
                            }
                        }
                        catch
                        {
                            try { content = System.Text.Encoding.Default.GetString(rawBytes); }
                            catch { try { content = System.Text.Encoding.GetEncoding(1257).GetString(rawBytes); } catch { content = string.Empty; } }
                        }
                    }
                }

                var vm = this.DataContext as MainViewModel;
                if (vm == null) return;

                // Try find existing note with identical content; otherwise create new
                NoteCardViewModel existing = null;
                foreach (var n in vm.Notes)
                {
                    if (n.Document.Content == content)
                    {
                        existing = n; break;
                    }
                }

                NoteCardViewModel target;
                if (existing != null)
                {
                    target = existing;
                }
                else
                {
                    var doc = new NoteCards.Models.NoteDocument
                    {
                        Title = System.IO.Path.GetFileNameWithoutExtension(path),
                        Content = content
                    };
                    target = new NoteCardViewModel(doc, (note) => vm.Notes.Remove(note));
                    vm.Notes.Add(target);
                    vm.SaveNotes();
                }

                OpenNoteEditor(target);
            }
            catch
            {
                MessageBox.Show("Failed to open file.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.DataContext is ViewModels.MainViewModel vm)
            {
                vm.SearchQuery = string.Empty;
            }
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
                vm.SaveNotes();
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