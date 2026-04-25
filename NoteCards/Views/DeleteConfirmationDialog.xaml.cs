using System.Windows;
using NoteCards.Localization;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class DeleteConfirmationDialog : Window
    {
        public enum ConfirmationAction
        {
            Cancel,
            Confirm,
            Secondary
        }

        private bool _isClosingAnimationRunning;
        private bool _pendingDialogResult;
        private ConfirmationAction _pendingAction = ConfirmationAction.Cancel;

        public string TitleText { get; }
        public string MessageText { get; }
        public string ConfirmText { get; }
        public string CancelText { get; }
        public string SecondaryText { get; }
        public Visibility SecondaryActionVisibility { get; }
        public ConfirmationAction SelectedAction { get; private set; } = ConfirmationAction.Cancel;

        public DeleteConfirmationDialog(
            string? title = null,
            string? message = null,
            string? confirmText = null,
            string? cancelText = null,
            string? secondaryText = null)
        {
            TitleText = string.IsNullOrWhiteSpace(title)
                ? LocalizationService.GetString("ConfirmDelete")
                : title;

            MessageText = string.IsNullOrWhiteSpace(message)
                ? LocalizationService.GetString("DeleteNoteConfirmation")
                : message;

            ConfirmText = string.IsNullOrWhiteSpace(confirmText)
                ? LocalizationService.GetString("Confirm")
                : confirmText;
            CancelText = string.IsNullOrWhiteSpace(cancelText)
                ? LocalizationService.GetString("Cancel")
                : cancelText;
            SecondaryText = secondaryText ?? string.Empty;
            SecondaryActionVisibility = string.IsNullOrWhiteSpace(secondaryText)
                ? Visibility.Collapsed
                : Visibility.Visible;

            InitializeComponent();
            DataContext = this;
            Loaded += DeleteConfirmationDialog_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyOwnerBounds();
        }

        private void ApplyOwnerBounds()
        {
            OverlayDialogBoundsHelper.Apply(this);
        }

        private void DeleteConfirmationDialog_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyOwnerBounds();
            BeginOpenAnimation();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(true, ConfirmationAction.Confirm);
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(true, ConfirmationAction.Secondary);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(false, ConfirmationAction.Cancel);
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                BeginCloseAnimation(false, ConfirmationAction.Cancel);
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

        private void BeginCloseAnimation(bool dialogResult, ConfirmationAction action)
        {
            if (_isClosingAnimationRunning)
                return;

            _isClosingAnimationRunning = true;
            _pendingDialogResult = dialogResult;
            _pendingAction = action;

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
                SelectedAction = _pendingAction;
                DialogResult = _pendingDialogResult;
                Close();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
        }
    }
}
