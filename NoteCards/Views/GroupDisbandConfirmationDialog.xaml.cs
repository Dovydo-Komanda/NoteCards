using System;
using NoteCards.Localization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public enum GroupDisbandChoice
    {
        Cancel,
        KeepNotesUngrouped,
        DeleteNotes
    }

    public partial class GroupDisbandConfirmationDialog : Window
    {
        private bool _isClosingAnimationRunning;

        public string TitleText { get; }
        public string MessageText { get; }
        public string KeepNotesText { get; }
        public string DeleteNotesText { get; }
        public string CancelText { get; }
        public GroupDisbandChoice SelectedChoice { get; private set; } = GroupDisbandChoice.Cancel;

        public GroupDisbandConfirmationDialog(string? title = null, string? message = null, string? keepText = null, string? deleteText = null)
        {
            TitleText = string.IsNullOrWhiteSpace(title)
                ? LocalizationService.GetString("DisbandGroup")
                : title;

            MessageText = string.IsNullOrWhiteSpace(message)
                ? LocalizationService.GetString("DisbandGroupPrompt")
                : message;

            KeepNotesText = string.IsNullOrWhiteSpace(keepText)
                ? LocalizationService.GetString("KeepNotesUngrouped")
                : keepText;
            DeleteNotesText = string.IsNullOrWhiteSpace(deleteText)
                ? LocalizationService.GetString("DeleteGroupNotes")
                : deleteText;
            CancelText = LocalizationService.GetString("Cancel");

            InitializeComponent();
            DataContext = this;
            Loaded += GroupDisbandConfirmationDialog_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyOwnerBounds();
        }

        private void GroupDisbandConfirmationDialog_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyOwnerBounds();
            BeginOpenAnimation();
        }

        private void ApplyOwnerBounds()
        {
            OverlayDialogBoundsHelper.Apply(this);
        }

        private void KeepNotesButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = GroupDisbandChoice.KeepNotesUngrouped;
            BeginCloseAnimation(true);
        }

        private void DeleteNotesButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = GroupDisbandChoice.DeleteNotes;
            BeginCloseAnimation(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedChoice = GroupDisbandChoice.Cancel;
            BeginCloseAnimation(false);
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                SelectedChoice = GroupDisbandChoice.Cancel;
                BeginCloseAnimation(false);
                e.Handled = true;
            }
        }

        private void BeginOpenAnimation()
        {
            Opacity = 0;

            if (DialogCard.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                DialogCard.RenderTransform = translate;
            }

            translate.Y = 12;

            var duration = TimeSpan.FromMilliseconds(190);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
        }

        private void BeginCloseAnimation(bool dialogResult)
        {
            if (_isClosingAnimationRunning)
                return;

            _isClosingAnimationRunning = true;

            if (DialogCard.RenderTransform is not TranslateTransform translate)
            {
                translate = new TranslateTransform();
                DialogCard.RenderTransform = translate;
            }

            var duration = TimeSpan.FromMilliseconds(150);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var fadeOut = new DoubleAnimation(Opacity, 0, duration) { EasingFunction = ease };
            fadeOut.Completed += (_, _) =>
            {
                DialogResult = dialogResult;
                Close();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
        }
    }
}
