using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
