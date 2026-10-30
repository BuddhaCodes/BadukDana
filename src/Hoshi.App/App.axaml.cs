using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace Hoshi.App;

public partial class App : Application
{
    private readonly IServiceProvider? _services;

    /// <summary>
    /// Used by the designer and by platforms that create the app themselves (Android): there,
    /// <see cref="ServiceFactory"/> builds the services first.
    /// </summary>
    public App()
    {
        _services = ServiceFactory?.Invoke();
        Services ??= _services;
    }

    public App(IServiceProvider services)
    {
        _services = services;
        Services = services;
    }

    /// <summary>Set by Android before the app starts: builds (and starts) the host.</summary>
    public static Func<IServiceProvider>? ServiceFactory { get; set; }

    /// <summary>The running app's services (for platform code: saving the game when Android pauses the app).</summary>
    public static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // Unhandled exceptions end in the log (and "Report a problem") instead of vanishing. Not without a host:
        // the designer and the UI tests keep Avalonia's own behaviour.
        if (_services?.GetService<Microsoft.Extensions.Logging.ILoggerFactory>() is { } loggers)
        {
            Hoshi.App.Services.Diagnostics.CrashGuard.Version = Hoshi.App.Services.Updates.UpdatePlatform.Display(Hoshi.App.Services.Updates.UpdatePlatform.CurrentVersion);
            Hoshi.App.Services.Diagnostics.CrashGuard.Install(loggers.CreateLogger("Hoshi.Crash"), Path.Combine(Hoshi.App.Services.AppPaths.DataDirectory, "logs"));
        }

        // "Your move" in another OGS game reaches the player outside the window (Android installs its own).
        if (_services is not null)
        {
            Hoshi.App.Services.DesktopNotifier.Install();
        }

        // The saved language (English by default). Without a host (designer, UI tests) the current language is kept.
        if (_services is not null)
        {
            Tr.SetLanguage(_services.GetService<Services.ISettingsService>()?.Current.Language ?? Tr.English);
        }

        // The saved theme (or Hoshi's default when there is no host, e.g. the designer and UI tests).
        if (_services?.GetService<Themes.ThemeService>() is { } themes)
        {
            themes.ApplyCurrent(Resources);
        }
        else
        {
            Themes.ThemeService.Apply(Themes.HoshiThemes.Default, animations: false, Resources);
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (_services is not null)
            {
                ShowSplashThenMainWindow(desktop, _services);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            desktop.MainWindow = new MainWindow { DataContext = new MainWindowViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // Phones: one view, the board on top and pages instead of windows.
            PhoneShellViewModel shell = _services?.GetService<PhoneShellViewModel>()
                ?? new PhoneShellViewModel(new MainWindowViewModel(), new Services.Phone.PhoneNavigation());
            shell.IsHome = true; // the app opens on its home screen
            single.MainView = new PhoneView { DataContext = shell };
        }

        // Phones are told about a new version and update by installing the new APK (from the website) over this one.
        if (Hoshi.App.Services.PlatformInfo.IsPhone)
        {
            _services?.GetService<UpdateViewModel>()?.Start();
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Desktops: the splash is drawn first, then the main window is built (a second or two: view models, the
    /// theme's textures) and takes its place.
    /// </summary>
    private static void ShowSplashThenMainWindow(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        var splash = new SplashWindow();
        desktop.MainWindow = splash;
        splash.Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                MainWindow main = services.GetRequiredService<MainWindow>();
                desktop.MainWindow = main;
                main.Show();
                splash.Close();
                services.GetService<UpdateViewModel>()?.Start();
            },
            Avalonia.Threading.DispatcherPriority.Background);
    }
}
