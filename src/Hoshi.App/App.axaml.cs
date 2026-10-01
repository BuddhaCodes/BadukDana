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

    /// <summary>Design-time / previewer constructor.</summary>
    public App()
    {
    }

    public App(IServiceProvider services)
    {
        _services = services;
    }

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

        base.OnFrameworkInitializationCompleted();
    }
}
