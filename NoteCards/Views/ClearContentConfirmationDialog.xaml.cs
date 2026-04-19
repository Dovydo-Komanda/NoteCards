using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class ClearContentConfirmationDialog : Window
    {
        private bool _isClosingAnimationRunning;
        private bool _pendingDialogResult;

        public ClearContentConfirmationDialog()
        {
            InitializeComponent();
            Loaded += ClearContentConfirmationDialog_Loaded;
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

        private void ClearContentConfirmationDialog_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyOwnerBounds();
            BeginOpenAnimation();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(false);
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
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
            _pendingDialogResult = dialogResult;

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
                DialogResult = _pendingDialogResult;
                Close();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
        }
    }
}
