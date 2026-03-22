using System.Windows;

namespace NoteCards.Views
{
    public partial class ClearContentConfirmationDialog : Window
    {
        public ClearContentConfirmationDialog()
        {
            InitializeComponent();

            // Ensure dialog is always centered on owner window
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }
    }
}
