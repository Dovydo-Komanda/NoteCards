using System;
using NoteCards.Localization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class ModernInfoDialog : Window
    {
        private bool _isClosingAnimationRunning;

        public string TitleText { get; }
        public string MessageText { get; }
        public string OkText { get; }

        public ModernInfoDialog(string? title = null, string? message = null)
        {
            TitleText = title ?? LocalizationService.GetString("AppUpdate");

            MessageText = message ?? LocalizationService.GetString("LatestVersion");

            OkText = LocalizationService.GetString("Ok");

            InitializeComponent();
            DataContext = this;
            Loaded += ModernInfoDialog_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyOwnerBounds();
        }

        private void ApplyOwnerBounds()
        {
            if (Owner is null)
                return;

            var ownerWidth = Owner.ActualWidth > 0 ? Owner.ActualWidth : Owner.Width;
            var ownerHeight = Owner.ActualHeight > 0 ? Owner.ActualHeight : Owner.Height;

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Owner.Left;
            Top = Owner.Top;
            Width = ownerWidth;
            Height = ownerHeight;
        }

        private void ModernInfoDialog_Loaded(object sender, RoutedEventArgs e)
        {
            BeginOpenAnimation();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation();
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                BeginCloseAnimation();
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

        private void BeginCloseAnimation()
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
                DialogResult = true;
                Close();
            };

            BeginAnimation(OpacityProperty, fadeOut);
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
        }
    }
}
