using NoteCards.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class EditFlashcardDialog : Window
    {
        private bool _isClosingAnimationRunning;
        private bool _pendingDialogResult;

        public string DialogTitle { get; }
        public string DialogHint { get; }
        public IReadOnlyList<string> CategoryOptions { get; }

        public string Question
        {
            get => QuestionTextBox.Text;
            set => QuestionTextBox.Text = value;
        }

        public string Answer
        {
            get => AnswerTextBox.Text;
            set => AnswerTextBox.Text = value;
        }

        public string Category
        {
            get => CategoryComboBox.Text?.Trim() ?? string.Empty;
            set => CategoryComboBox.Text = value?.Trim() ?? string.Empty;
        }

        public EditFlashcardDialog(bool isNew = false, IEnumerable<string>? categoryOptions = null)
        {
            DialogTitle = LocalizationService.GetString(isNew ? "AddFlashcard" : "EditFlashcard");
            DialogHint = LocalizationService.GetString(isNew ? "AddFlashcardDialogHint" : "EditFlashcardDialogHint");
            CategoryOptions = categoryOptions is null
                ? Array.Empty<string>()
                : categoryOptions
                    .Select(category => category.Trim())
                    .Where(category => !string.IsNullOrWhiteSpace(category))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(category => category, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            InitializeComponent();
            NoteCards.Services.WindowThemeService.Register(this);
            DataContext = this;
            Loaded += EditFlashcardDialog_Loaded;
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

        private void EditFlashcardDialog_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyOwnerBounds();
            BeginOpenAnimation();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Question) || string.IsNullOrWhiteSpace(Answer))
            {
                MessageBox.Show(
                    LocalizationService.GetString("FlashcardEditEmptyError"),
                    LocalizationService.GetString("EditFlashcard"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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
