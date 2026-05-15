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
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnWindowLoaded));

        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
            WindowThemeService.ApplyTheme(window, ThemeManager.CurrentTheme);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        BundledModelHostService.Instance.Stop();
        base.OnExit(e);
    }
}
