using NoteCards.Localization;
using System.Windows;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class QuizSubmitConfirmationDialog : Window
{
    private bool _isClosingAnimationRunning;
    private bool _pendingDialogResult;

    public string TitleText { get; }
    public string MessageText { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public QuizSubmitConfirmationDialog(
        string? title = null,
        string? message = null,
        string? confirmText = null,
        string? cancelText = null)
    {
        TitleText = string.IsNullOrWhiteSpace(title) ? LocalizationService.GetString("QuizSubmitDialogTitle") : title;
        MessageText = string.IsNullOrWhiteSpace(message)
            ? LocalizationService.GetString("QuizSubmitDialogMessage")
            : message;
        ConfirmText = string.IsNullOrWhiteSpace(confirmText) ? LocalizationService.GetString("QuizSubmitDialogConfirm") : confirmText;
        CancelText = string.IsNullOrWhiteSpace(cancelText) ? LocalizationService.GetString("QuizSubmitDialogCancel") : cancelText;

        InitializeComponent();
        NoteCards.Services.WindowThemeService.Register(this);
        DataContext = this;
        Loaded += QuizSubmitConfirmationDialog_Loaded;
    }

    protected override void OnSourceInitialized(System.EventArgs e)
    {
        base.OnSourceInitialized(e);
        OverlayDialogBoundsHelper.Apply(this);
    }

    private void QuizSubmitConfirmationDialog_Loaded(object sender, RoutedEventArgs e)
    {
        OverlayDialogBoundsHelper.Apply(this);
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

        if (DialogCard.RenderTransform is not System.Windows.Media.TranslateTransform translate)
        {
            translate = new System.Windows.Media.TranslateTransform();
            DialogCard.RenderTransform = translate;
        }

        translate.Y = 12;

        var duration = TimeSpan.FromMilliseconds(190);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(12, 0, duration) { EasingFunction = ease });
    }

    private void BeginCloseAnimation(bool dialogResult)
    {
        if (_isClosingAnimationRunning)
            return;

        _isClosingAnimationRunning = true;
        _pendingDialogResult = dialogResult;

        if (DialogCard.RenderTransform is not System.Windows.Media.TranslateTransform translate)
        {
            translate = new System.Windows.Media.TranslateTransform();
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
        translate.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
    }
}
