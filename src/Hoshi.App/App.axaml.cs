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
            desktop.MainWindow = _services is not null
                ? _services.GetRequiredService<MainWindow>()
                : new MainWindow { DataContext = new MainWindowViewModel() };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            // Phones: one view, the board on top and pages instead of windows.
            single.MainView = new PhoneView
            {
                DataContext = _services?.GetService<PhoneShellViewModel>()
                    ?? new PhoneShellViewModel(new MainWindowViewModel(), new Services.Phone.PhoneNavigation()),
            };
        }

        _services?.GetService<UpdateViewModel>()?.Start();
        base.OnFrameworkInitializationCompleted();
    }
}
