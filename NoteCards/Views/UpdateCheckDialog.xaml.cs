using NoteCards.Localization;
using NoteCards.Services;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NoteCards.Views
{
    public partial class UpdateCheckDialog : Window
    {
        private const int MaxReleaseNotesLength = 700;

        private readonly string? _releasesUrl;
        private bool _isClosingAnimationRunning;

        public string TitleText { get; }
        public string MessageText { get; }
        public string IconText { get; }
        public string CurrentVersionText { get; }
        public string LatestVersionText { get; }
        public string PublishedText { get; }
        public string ReleaseNotesText { get; }
        public string CloseText { get; }
        public string OpenReleasesText { get; }
        public Visibility PublishedVisibility { get; }
        public Visibility ReleaseNotesVisibility { get; }
        public Visibility OpenReleasesVisibility { get; }

        public UpdateCheckDialog(GitHubUpdateResult result)
        {
            var latest = result.LatestRelease;
            var latestVersion = latest?.TagName ?? LocalizationService.GetString("UpdateNoReleaseVersion");

            TitleText = result.IsUpdateAvailable
                ? LocalizationService.GetString("UpdateAvailableTitle")
                : LocalizationService.GetString("AppUpdate");
            MessageText = BuildMessage(result);
            IconText = result.IsUpdateAvailable ? "↑" : "✓";
            CurrentVersionText = string.Format(LocalizationService.GetString("UpdateCurrentVersionFormat"), result.CurrentVersion);
            LatestVersionText = string.Format(LocalizationService.GetString("UpdateLatestVersionFormat"), latestVersion);
            PublishedText = latest?.PublishedAt is null
                ? string.Empty
                : string.Format(LocalizationService.GetString("UpdatePublishedFormat"), latest.PublishedAt.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            PublishedVisibility = string.IsNullOrWhiteSpace(PublishedText) ? Visibility.Collapsed : Visibility.Visible;
            ReleaseNotesText = TrimReleaseNotes(latest?.Body);
            ReleaseNotesVisibility = string.IsNullOrWhiteSpace(ReleaseNotesText) ? Visibility.Collapsed : Visibility.Visible;
            CloseText = LocalizationService.GetString("Close");
            OpenReleasesText = result.IsUpdateAvailable
                ? LocalizationService.GetString("OpenRelease")
                : LocalizationService.GetString("OpenReleases");
            OpenReleasesVisibility = Visibility.Visible;
            _releasesUrl = result.IsUpdateAvailable ? latest?.HtmlUrl : result.ReleasesUrl;

            InitializeComponent();
            NoteCards.Services.WindowThemeService.Register(this);
            DataContext = this;
            Loaded += UpdateCheckDialog_Loaded;
        }

        public UpdateCheckDialog(string title, string message, string? releasesUrl = null)
        {
            TitleText = title;
            MessageText = message;
            IconText = "!";
            CurrentVersionText = string.Format(LocalizationService.GetString("UpdateCurrentVersionFormat"), AppVersionInfo.DisplayVersion);
            LatestVersionText = LocalizationService.GetString("UpdateLatestVersionUnavailable");
            PublishedText = string.Empty;
            ReleaseNotesText = string.Empty;
            CloseText = LocalizationService.GetString("Close");
            OpenReleasesText = LocalizationService.GetString("OpenReleases");
            PublishedVisibility = Visibility.Collapsed;
            ReleaseNotesVisibility = Visibility.Collapsed;
            OpenReleasesVisibility = string.IsNullOrWhiteSpace(releasesUrl) ? Visibility.Collapsed : Visibility.Visible;
            _releasesUrl = releasesUrl;

            InitializeComponent();
            NoteCards.Services.WindowThemeService.Register(this);
            DataContext = this;
            Loaded += UpdateCheckDialog_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyOwnerBounds();
        }

        private static string BuildMessage(GitHubUpdateResult result)
        {
            if (result.LatestRelease is null)
                return LocalizationService.GetString("UpdateNoReleasesMessage");

            if (!result.HasComparableVersions)
                return LocalizationService.GetString("UpdateVersionCompareUnavailableMessage");

            if (result.IsUpdateAvailable)
            {
                return string.Format(
                    LocalizationService.GetString("UpdateAvailableMessageFormat"),
                    result.LatestRelease.TagName);
            }

            return LocalizationService.GetString("LatestVersion");
        }

        private static string TrimReleaseNotes(string? releaseNotes)
        {
            if (string.IsNullOrWhiteSpace(releaseNotes))
                return string.Empty;

            var normalized = releaseNotes.Trim();
            if (normalized.Length <= MaxReleaseNotesLength)
                return normalized;

            return normalized[..MaxReleaseNotesLength].TrimEnd() + "...";
        }

        private void ApplyOwnerBounds()
        {
            OverlayDialogBoundsHelper.Apply(this);
        }

        private void UpdateCheckDialog_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyOwnerBounds();
            BeginOpenAnimation();
        }

        private void OpenReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_releasesUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _releasesUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                }
            }

            BeginCloseAnimation();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
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
