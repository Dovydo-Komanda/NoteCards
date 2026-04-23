using NoteCards.Localization;
using NoteCards.Services;
using System.Windows;

namespace NoteCards;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = AppSettingsService.Load();
        LocalizationService.SetCulture(settings.Language);
        ThemeManager.SetTheme(settings.Theme);

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BundledModelHostService.Instance.Stop();
        base.OnExit(e);
    }
}
