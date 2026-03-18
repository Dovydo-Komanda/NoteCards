using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using NoteCards.Localization;

namespace NoteCards.Views
{
    public partial class SettingsPanel : UserControl
    {
        private const int OverlayAnimationMs = 180;
        private const int PanelAnimationMs = 220;
        private const double PanelOffsetY = 14;

        private bool _isClosing;

        public SettingsPanel()
        {
            InitializeComponent();
        }

        public void ShowAnimated()
        {
            _isClosing = false;
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;

            OverlayRoot.BeginAnimation(OpacityProperty, null);
            PanelCard.BeginAnimation(OpacityProperty, null);
            var translate = EnsurePanelTranslate();
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            OverlayRoot.Opacity = 0;
            PanelCard.Opacity = 0;
            translate.Y = PanelOffsetY;

            var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
            var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };

            var openOverlay = new DoubleAnimation(0, 1, overlayDuration)
            {
                EasingFunction = easeOut
            };
            var openPanelOpacity = new DoubleAnimation(0, 1, panelDuration)
            {
                EasingFunction = easeOut
            };
            var openPanelShift = new DoubleAnimation(PanelOffsetY, 0, panelDuration)
            {
                EasingFunction = easeOut
            };
            openPanelShift.Completed += (_, _) =>
            {
                OverlayRoot.Opacity = 1;
                PanelCard.Opacity = 1;
                translate.Y = 0;
            };

            OverlayRoot.BeginAnimation(OpacityProperty, openOverlay);
            PanelCard.BeginAnimation(OpacityProperty, openPanelOpacity);
            translate.BeginAnimation(TranslateTransform.YProperty, openPanelShift);
        }

        public void HideAnimated()
        {
            if (_isClosing || Visibility != Visibility.Visible)
                return;

            _isClosing = true;
            IsHitTestVisible = false;

            var translate = EnsurePanelTranslate();
            var startOverlayOpacity = OverlayRoot.Opacity;
            var startPanelOpacity = PanelCard.Opacity;
            var startY = translate.Y;

            if (startOverlayOpacity <= 0)
                startOverlayOpacity = 1;

            if (startPanelOpacity <= 0)
                startPanelOpacity = 1;

            var overlayDuration = TimeSpan.FromMilliseconds(OverlayAnimationMs);
            var panelDuration = TimeSpan.FromMilliseconds(PanelAnimationMs);
            var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

            var closeOverlay = new DoubleAnimation(startOverlayOpacity, 0, overlayDuration)
            {
                EasingFunction = easeIn
            };
            var closePanelOpacity = new DoubleAnimation(startPanelOpacity, 0, panelDuration)
            {
                EasingFunction = easeIn
            };
            var closePanelShift = new DoubleAnimation(startY, PanelOffsetY, panelDuration)
            {
                EasingFunction = easeIn
            };

            closePanelShift.Completed += (_, _) =>
            {
                Visibility = Visibility.Collapsed;
                OverlayRoot.Opacity = 0;
                PanelCard.Opacity = 0;
                translate.Y = PanelOffsetY;
                _isClosing = false;
            };

            OverlayRoot.BeginAnimation(OpacityProperty, closeOverlay);
            PanelCard.BeginAnimation(OpacityProperty, closePanelOpacity);
            translate.BeginAnimation(TranslateTransform.YProperty, closePanelShift);
        }

        private TranslateTransform EnsurePanelTranslate()
        {
            if (PanelCard.RenderTransform is TranslateTransform translate)
                return translate;

            translate = new TranslateTransform();
            PanelCard.RenderTransform = translate;
            return translate;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideAnimated();
        }

        private void OverlayRoot_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender)
                HideAnimated();
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(LocalizationService.GetString("LatestVersion"),
                            LocalizationService.GetString("AppUpdate"),
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }
    }
}