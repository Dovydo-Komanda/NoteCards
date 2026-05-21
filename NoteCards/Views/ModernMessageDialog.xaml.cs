using NoteCards.Localization;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views;

public partial class ModernMessageDialog : Window
{
    private bool _isClosingAnimationRunning;
    private MessageBoxResult _pendingResult;
    private ModernDialogResult _pendingChoice;

    public string TitleText { get; }
    public string MessageText { get; }
    public string IconGlyph { get; }
    public Brush IconBackground { get; }
    public Brush IconBorder { get; }
    public Brush IconForeground { get; }
    public string PrimaryText { get; }
    public string NoText { get; }
    public string CancelText { get; }
    public Visibility PrimaryVisibility { get; }
    public Visibility NoVisibility { get; }
    public Visibility CancelVisibility { get; }
    public MessageBoxResult PrimaryResult { get; }
    public MessageBoxResult EscapeResult { get; }
    public MessageBoxResult SelectedResult { get; private set; }
    public ModernDialogResult SelectedChoice { get; private set; }

    private MessageBoxImage DialogImage { get; }
    private ModernDialogTone DialogTone { get; }
    private ModernDialogResult PrimaryChoice { get; }
    private ModernDialogResult SecondaryChoice { get; }
    private ModernDialogResult CancelChoice { get; }
    private ModernDialogResult EscapeChoice { get; }

    public ModernMessageDialog(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage image,
        MessageBoxResult defaultResult)
    {
        TitleText = string.IsNullOrWhiteSpace(caption)
            ? ResolveDefaultTitle(image)
            : caption;
        MessageText = messageBoxText ?? string.Empty;
        DialogImage = image;
        DialogTone = ResolveTone(image);

        (IconGlyph, IconBackground, IconBorder, IconForeground) = ResolveIcon(DialogTone);
        (PrimaryText, PrimaryResult, PrimaryVisibility, NoText, NoVisibility, CancelText, CancelVisibility, EscapeResult) =
            ResolveButtons(button);

        SelectedResult = ResolveInitialResult(button, defaultResult);
        SelectedChoice = ResultToChoice(SelectedResult);
        _pendingResult = SelectedResult;
        _pendingChoice = SelectedChoice;

        PrimaryChoice = ModernDialogResult.Primary;
        SecondaryChoice = ModernDialogResult.Secondary;
        CancelChoice = ModernDialogResult.Cancel;
        EscapeChoice = ResultToChoice(EscapeResult);

        InitializeComponent();
        ApplyPrimaryButtonStyle(DialogTone);
        ConfigureButtonLayout();
        NoteCards.Services.WindowThemeService.Register(this);
        DataContext = this;
        Loaded += ModernMessageDialog_Loaded;
    }

    public ModernMessageDialog(
        string title,
        string message,
        ModernDialogTone tone,
        string primaryText,
        string? cancelText = null,
        string? secondaryText = null,
        ModernDialogButtonStyle? primaryStyle = null,
        ModernDialogButtonStyle secondaryStyle = ModernDialogButtonStyle.Secondary,
        ModernDialogButtonStyle cancelStyle = ModernDialogButtonStyle.Secondary)
    {
        TitleText = title ?? string.Empty;
        MessageText = message ?? string.Empty;
        DialogTone = tone;
        DialogImage = ToneToImage(tone);

        (IconGlyph, IconBackground, IconBorder, IconForeground) = ResolveIcon(tone);

        PrimaryText = primaryText;
        PrimaryResult = MessageBoxResult.OK;
        PrimaryVisibility = Visibility.Visible;
        NoText = secondaryText ?? string.Empty;
        NoVisibility = string.IsNullOrWhiteSpace(secondaryText) ? Visibility.Collapsed : Visibility.Visible;
        CancelText = cancelText ?? string.Empty;
        CancelVisibility = string.IsNullOrWhiteSpace(cancelText) ? Visibility.Collapsed : Visibility.Visible;
        EscapeResult = CancelVisibility == Visibility.Visible
            ? MessageBoxResult.Cancel
            : NoVisibility == Visibility.Visible
                ? MessageBoxResult.No
                : MessageBoxResult.OK;

        SelectedResult = EscapeResult;
        SelectedChoice = CancelVisibility == Visibility.Visible
            ? ModernDialogResult.Cancel
            : NoVisibility == Visibility.Visible
                ? ModernDialogResult.Secondary
                : ModernDialogResult.Primary;
        _pendingResult = SelectedResult;
        _pendingChoice = SelectedChoice;

        PrimaryChoice = ModernDialogResult.Primary;
        SecondaryChoice = ModernDialogResult.Secondary;
        CancelChoice = ModernDialogResult.Cancel;
        EscapeChoice = SelectedChoice;

        InitializeComponent();
        ApplyButtonStyles(
            primaryStyle ?? DefaultPrimaryButtonStyleForTone(tone),
            secondaryStyle,
            cancelStyle);
        ConfigureButtonLayout();
        NoteCards.Services.WindowThemeService.Register(this);
        DataContext = this;
        Loaded += ModernMessageDialog_Loaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyOwnerBounds();
    }

    private void ModernMessageDialog_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyOwnerBounds();
        FocusDefaultButton();
        ConfigureNativeDialogButtons();
        PlayDialogSound();
        BeginOpenAnimation();
    }

    private void ApplyOwnerBounds()
    {
        OverlayDialogBoundsHelper.Apply(this);
    }

    private void FocusDefaultButton()
    {
        var target = SelectedChoice switch
        {
            ModernDialogResult.Cancel => CancelButton,
            ModernDialogResult.Secondary => NoButton,
            _ => PrimaryButton
        };

        target.Focus();
    }

    private void ConfigureNativeDialogButtons()
    {
        PrimaryButton.IsDefault = SelectedChoice == ModernDialogResult.Primary;
        NoButton.IsDefault = SelectedChoice == ModernDialogResult.Secondary;
        CancelButton.IsDefault = SelectedChoice == ModernDialogResult.Cancel;
    }

    private void PlayDialogSound()
    {
        if (DialogTone is ModernDialogTone.Danger or ModernDialogTone.Error)
        {
            SystemSounds.Hand.Play();
        }
        else if (DialogTone == ModernDialogTone.Warning)
        {
            SystemSounds.Exclamation.Play();
        }
        else if (DialogTone == ModernDialogTone.Question)
        {
            SystemSounds.Question.Play();
        }
        else if (DialogTone is ModernDialogTone.Info or ModernDialogTone.Success)
        {
            SystemSounds.Asterisk.Play();
        }
    }

    private static string ResolveDefaultTitle(MessageBoxImage image)
    {
        return ResolveTone(image) switch
        {
            ModernDialogTone.Error => LocalizationService.GetString("Error"),
            ModernDialogTone.Warning => "Warning",
            ModernDialogTone.Question => "Question",
            _ => "NoteCards"
        };
    }

    private static ModernDialogTone ResolveTone(MessageBoxImage image)
    {
        if (image is MessageBoxImage.Error or MessageBoxImage.Hand or MessageBoxImage.Stop)
            return ModernDialogTone.Error;

        if (image is MessageBoxImage.Warning or MessageBoxImage.Exclamation)
            return ModernDialogTone.Warning;

        if (image == MessageBoxImage.Question)
            return ModernDialogTone.Question;

        return ModernDialogTone.Info;
    }

    private static MessageBoxImage ToneToImage(ModernDialogTone tone)
    {
        return tone switch
        {
            ModernDialogTone.Danger or ModernDialogTone.Error => MessageBoxImage.Error,
            ModernDialogTone.Warning => MessageBoxImage.Warning,
            ModernDialogTone.Question => MessageBoxImage.Question,
            ModernDialogTone.Info or ModernDialogTone.Success => MessageBoxImage.Information,
            _ => MessageBoxImage.None
        };
    }

    private static (string Glyph, Brush Background, Brush Border, Brush Foreground) ResolveIcon(ModernDialogTone tone)
    {
        return tone switch
        {
            ModernDialogTone.Success => (
                "✓",
                ResolveResourceBrush("FlashcardKnownChipBackground", "#E8F8F0"),
                ResolveResourceBrush("FlashcardKnownChipBorder", "#B7E6CC"),
                ResolveResourceBrush("FlashcardKnownChipForeground", "#096B3F")),
            ModernDialogTone.Danger or ModernDialogTone.Error => (
                "!",
                CreateBrush("#FEE2E2"),
                CreateBrush("#FCA5A5"),
                CreateBrush("#DC2626")),
            ModernDialogTone.Warning => (
                "!",
                CreateBrush("#FFF4E0"),
                CreateBrush("#F6D28B"),
                CreateBrush("#B45309")),
            ModernDialogTone.Question => (
                "?",
                ResolveResourceBrush("EditorToolActiveBackground", "#EAF2FF"),
                ResolveResourceBrush("EditorToolActiveBorder", "#8FB1FF"),
                ResolveResourceBrush("EditorToolActiveForeground", "#1F4EA8")),
            _ => (
                "i",
                ResolveResourceBrush("EditorToolActiveBackground", "#EAF2FF"),
                ResolveResourceBrush("EditorToolActiveBorder", "#8FB1FF"),
                ResolveResourceBrush("EditorToolActiveForeground", "#1F4EA8"))
        };
    }

    private static Brush ResolveResourceBrush(string key, string fallback)
    {
        return Application.Current?.TryFindResource(key) as Brush
            ?? CreateBrush(fallback);
    }

    private static Brush CreateBrush(string color)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
        brush.Freeze();
        return brush;
    }

    private static (
        string PrimaryText,
        MessageBoxResult PrimaryResult,
        Visibility PrimaryVisibility,
        string NoText,
        Visibility NoVisibility,
        string CancelText,
        Visibility CancelVisibility,
        MessageBoxResult EscapeResult) ResolveButtons(MessageBoxButton button)
    {
        var ok = LocalizationService.GetString("Ok");
        var cancel = LocalizationService.GetString("Cancel");
        var yes = LocalizationService.GetString("InfoYes");
        var no = LocalizationService.GetString("InfoNo");

        return button switch
        {
            MessageBoxButton.OKCancel => (ok, MessageBoxResult.OK, Visibility.Visible, string.Empty, Visibility.Collapsed, cancel, Visibility.Visible, MessageBoxResult.Cancel),
            MessageBoxButton.YesNo => (yes, MessageBoxResult.Yes, Visibility.Visible, no, Visibility.Visible, string.Empty, Visibility.Collapsed, MessageBoxResult.No),
            MessageBoxButton.YesNoCancel => (yes, MessageBoxResult.Yes, Visibility.Visible, no, Visibility.Visible, cancel, Visibility.Visible, MessageBoxResult.Cancel),
            _ => (ok, MessageBoxResult.OK, Visibility.Visible, string.Empty, Visibility.Collapsed, string.Empty, Visibility.Collapsed, MessageBoxResult.OK)
        };
    }

    private static MessageBoxResult ResolveInitialResult(MessageBoxButton button, MessageBoxResult defaultResult)
    {
        if (defaultResult != MessageBoxResult.None)
            return defaultResult;

        return button switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            _ => MessageBoxResult.OK
        };
    }

    private static ModernDialogResult ResultToChoice(MessageBoxResult result)
    {
        return result switch
        {
            MessageBoxResult.No => ModernDialogResult.Secondary,
            MessageBoxResult.Cancel => ModernDialogResult.Cancel,
            _ => ModernDialogResult.Primary
        };
    }

    private static ModernDialogButtonStyle DefaultPrimaryButtonStyleForTone(ModernDialogTone tone)
    {
        return tone switch
        {
            ModernDialogTone.Danger or ModernDialogTone.Error => ModernDialogButtonStyle.Danger,
            ModernDialogTone.Warning => ModernDialogButtonStyle.Warning,
            _ => ModernDialogButtonStyle.Primary
        };
    }

    private void ApplyPrimaryButtonStyle(ModernDialogTone tone)
    {
        ApplyButtonStyles(DefaultPrimaryButtonStyleForTone(tone));
    }

    private void ApplyButtonStyles(
        ModernDialogButtonStyle primaryStyle,
        ModernDialogButtonStyle secondaryStyle = ModernDialogButtonStyle.Secondary,
        ModernDialogButtonStyle cancelStyle = ModernDialogButtonStyle.Secondary)
    {
        ApplyButtonStyle(PrimaryButton, primaryStyle);
        ApplyButtonStyle(NoButton, secondaryStyle);
        ApplyButtonStyle(CancelButton, cancelStyle);
    }

    private void ApplyButtonStyle(System.Windows.Controls.Button button, ModernDialogButtonStyle buttonStyle)
    {
        var styleKey = buttonStyle switch
        {
            ModernDialogButtonStyle.Danger => "DialogDangerButtonStyle",
            ModernDialogButtonStyle.Warning => "DialogWarningButtonStyle",
            ModernDialogButtonStyle.Primary => "DialogPrimaryButtonStyle",
            _ => "DialogSecondaryButtonStyle"
        };

        if (TryFindResource(styleKey) is Style style)
            button.Style = style;
    }

    private void ConfigureButtonLayout()
    {
        var visibleButtonCount =
            (CancelVisibility == Visibility.Visible ? 1 : 0) +
            (NoVisibility == Visibility.Visible ? 1 : 0) +
            (PrimaryVisibility == Visibility.Visible ? 1 : 0);
        var labelLength = CancelText.Length + NoText.Length + PrimaryText.Length;

        if (visibleButtonCount < 3 || labelLength <= 32)
            return;

        ButtonPanel.Orientation = System.Windows.Controls.Orientation.Vertical;
        ButtonPanel.HorizontalAlignment = HorizontalAlignment.Stretch;

        ConfigureStackedButton(
            CancelButton,
            CancelVisibility == Visibility.Visible && (NoVisibility == Visibility.Visible || PrimaryVisibility == Visibility.Visible));
        ConfigureStackedButton(
            NoButton,
            NoVisibility == Visibility.Visible && PrimaryVisibility == Visibility.Visible);
        ConfigureStackedButton(PrimaryButton, false);
    }

    private static void ConfigureStackedButton(System.Windows.Controls.Button button, bool addBottomMargin)
    {
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.MinWidth = 0;
        button.Margin = addBottomMargin
            ? new Thickness(0, 0, 0, 10)
            : new Thickness(0);
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        BeginCloseAnimation(PrimaryResult, PrimaryChoice);
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        BeginCloseAnimation(MessageBoxResult.No, SecondaryChoice);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        BeginCloseAnimation(MessageBoxResult.Cancel, CancelChoice);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            BeginCloseAnimation(EscapeResult, EscapeChoice);
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

    private void BeginCloseAnimation(MessageBoxResult result, ModernDialogResult choice)
    {
        if (_isClosingAnimationRunning)
            return;

        _isClosingAnimationRunning = true;
        _pendingResult = result;
        _pendingChoice = choice;

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
            SelectedResult = _pendingResult;
            SelectedChoice = _pendingChoice;
            Close();
        };

        BeginAnimation(OpacityProperty, fadeOut);
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translate.Y, 8, duration) { EasingFunction = ease });
    }
}
