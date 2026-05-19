using NoteCards.Localization;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class QuizStartOptionsDialog : Window
    {
        private const int MaxCustomTimeLimitMinutes = 1440;

        private bool _isClosingAnimationRunning;
        private bool _pendingDialogResult;
        private Window? _ownerWindow;

        public int TimeLimitSeconds { get; private set; }
        public int PassingScorePercent { get; private set; }

        public QuizStartOptionsDialog(int? initialTimeLimitSeconds, int initialPassingScorePercent)
        {
            InitializeComponent();
            NoteCards.Services.WindowThemeService.Register(this);
            Loaded += QuizStartOptionsDialog_Loaded;
            Closed += QuizStartOptionsDialog_Closed;

            SelectTimeLimit(initialTimeLimitSeconds.GetValueOrDefault());
            PassingScorePercent = Math.Clamp(initialPassingScorePercent, 0, 100);
            PassingScoreTextBox.Text = PassingScorePercent.ToString(CultureInfo.InvariantCulture);
            PassingScoreTextBox.SelectAll();
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

        private void AttachOwnerHandlers()
        {
            if (Owner is null)
                return;

            if (ReferenceEquals(_ownerWindow, Owner))
                return;

            DetachOwnerHandlers();

            _ownerWindow = Owner;
            _ownerWindow.LocationChanged += OwnerWindow_BoundsChanged;
            _ownerWindow.SizeChanged += OwnerWindow_BoundsChanged;
            _ownerWindow.StateChanged += OwnerWindow_BoundsChanged;
        }

        private void DetachOwnerHandlers()
        {
            if (_ownerWindow is null)
                return;

            _ownerWindow.LocationChanged -= OwnerWindow_BoundsChanged;
            _ownerWindow.SizeChanged -= OwnerWindow_BoundsChanged;
            _ownerWindow.StateChanged -= OwnerWindow_BoundsChanged;
            _ownerWindow = null;
        }

        private void OwnerWindow_BoundsChanged(object? sender, EventArgs e)
        {
            ApplyOwnerBounds();
        }

        private void QuizStartOptionsDialog_Loaded(object sender, RoutedEventArgs e)
        {
            AttachOwnerHandlers();
            ApplyOwnerBounds();
            BeginOpenAnimation();
            if (CustomTimePanel.Visibility == Visibility.Visible)
            {
                CustomTimeTextBox.Focus();
                CustomTimeTextBox.SelectAll();
            }
            else
            {
                PassingScoreTextBox.Focus();
            }
        }

        private void QuizStartOptionsDialog_Closed(object? sender, EventArgs e)
        {
            DetachOwnerHandlers();
        }

        private void SelectTimeLimit(int seconds)
        {
            var normalized = seconds switch
            {
                60 or 300 or 600 or 900 or 1800 => seconds,
                _ => 0
            };

            if (seconds > 0 && normalized == 0)
            {
                foreach (var item in TimeLimitComboBox.Items.OfType<ComboBoxItem>())
                {
                    if (string.Equals(item.Tag?.ToString(), "custom", StringComparison.Ordinal))
                    {
                        TimeLimitComboBox.SelectedItem = item;
                        CustomTimeTextBox.Text = Math.Clamp((int)Math.Ceiling(seconds / 60.0), 1, MaxCustomTimeLimitMinutes)
                            .ToString(CultureInfo.InvariantCulture);
                        return;
                    }
                }
            }

            foreach (var item in TimeLimitComboBox.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag?.ToString() == normalized.ToString(CultureInfo.InvariantCulture))
                {
                    TimeLimitComboBox.SelectedItem = item;
                    return;
                }
            }

            TimeLimitComboBox.SelectedIndex = 0;
        }

        private void TimeLimitComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var isCustom = TimeLimitComboBox.SelectedItem is ComboBoxItem item
                && string.Equals(item.Tag?.ToString(), "custom", StringComparison.Ordinal);

            CustomTimePanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
            TimeValidationText.Visibility = Visibility.Collapsed;

            if (isCustom && string.IsNullOrWhiteSpace(CustomTimeTextBox.Text))
                CustomTimeTextBox.Text = "20";
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryCommitValues())
                return;

            BeginCloseAnimation(true);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            BeginCloseAnimation(false);
        }

        private bool TryCommitValues()
        {
            if (!int.TryParse(PassingScoreTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var passingScore)
                || passingScore < 0
                || passingScore > 100)
            {
                ValidationText.Visibility = Visibility.Visible;
                PassingScoreTextBox.Focus();
                PassingScoreTextBox.SelectAll();
                return false;
            }

            ValidationText.Visibility = Visibility.Collapsed;
            PassingScorePercent = passingScore;

            if (TimeLimitComboBox.SelectedItem is ComboBoxItem { Tag: string tag }
                && string.Equals(tag, "custom", StringComparison.Ordinal))
            {
                if (!int.TryParse(CustomTimeTextBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var customMinutes)
                    || customMinutes <= 0
                    || customMinutes > MaxCustomTimeLimitMinutes)
                {
                    TimeValidationText.Visibility = Visibility.Visible;
                    CustomTimeTextBox.Focus();
                    CustomTimeTextBox.SelectAll();
                    return false;
                }

                TimeValidationText.Visibility = Visibility.Collapsed;
                TimeLimitSeconds = customMinutes * 60;
            }
            else if (TimeLimitComboBox.SelectedItem is ComboBoxItem { Tag: string presetTag }
                && int.TryParse(presetTag, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                TimeLimitSeconds = seconds;
            }
            else
            {
                TimeLimitSeconds = 0;
            }

            return true;
        }

        private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = e.Text.Any(ch => !char.IsDigit(ch));
        }

        private void NumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
                e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape)
            {
                BeginCloseAnimation(false);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (TryCommitValues())
                    BeginCloseAnimation(true);

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
