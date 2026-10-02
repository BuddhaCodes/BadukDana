using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Hoshi.App;

/// <summary>Builds the generic host: configuration, logging and the DI container.</summary>
public static class AppHost
{
    public static IHost Create(string[] args, Action<IServiceCollection>? configureServices = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = "Hoshi",
        });

        string logDirectory = Path.Combine(AppPaths.DataDirectory, "logs");
        builder.Services.AddSerilog((_, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "hoshi-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        AddAppServices(builder.Services);
        builder.Services.AddOgs(builder.Configuration);
        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    public const string UpdatesHttpClient = "updates";

    /// <summary>Registers the application's own services, view models and views.</summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<IHostLifetime, DesktopLifetime>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<Themes.ThemeService>();
        services.AddSingleton<ISoundService, SystemSoundService>();
        services.AddSingleton<Services.Music.IMusicService, Services.Music.MusicService>();
        services.AddSingleton<AnalysisEngineHost>();
        services.AddSingleton<IAnalysisEngine>(sp => sp.GetRequiredService<AnalysisEngineHost>());
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<PreferencesViewModel>();
        services.AddSingleton<IPreferencesWindowService, PreferencesWindowService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<LobbyViewModel>();
        services.AddSingleton<ILobbyWindowService, LobbyWindowService>();
        services.AddSingleton<IReplayStore, ReplayStore>();
        services.AddSingleton<ReplaysViewModel>();
        services.AddSingleton<IReplaysWindowService, ReplaysWindowService>();
        services.AddSingleton<Services.Joseki.IJosekiLibrary, Services.Joseki.JosekiLibrary>();
        services.AddSingleton<Hoshi.Ogs.Joseki.IJosekiExplorerSource>(sp => new Hoshi.Ogs.Joseki.OgsJosekiClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OgsServiceRegistration.HttpClientName),
            cacheDirectory: Path.Combine(AppPaths.DataDirectory, "cache", "oje"),
            logger: sp.GetRequiredService<ILogger<Hoshi.Ogs.Joseki.OgsJosekiClient>>()));
        services.AddSingleton<Hoshi.Ogs.Joseki.JosekiExplorer>();
        services.AddSingleton<JosekiTrainerViewModel>();
        services.AddSingleton<JosekiAssistantViewModel>();
        services.AddHttpClient(UpdatesHttpClient, http =>
        {
            http.Timeout = Timeout.InfiniteTimeSpan; // downloads are long; each call has its own cancellation
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Hoshi/" + Services.Updates.UpdatePlatform.Display(Services.Updates.UpdatePlatform.CurrentVersion));
        });
        services.AddSingleton<Services.Updates.IReleaseSource>(sp =>
            new Services.Updates.GitHubReleaseSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient(UpdatesHttpClient)));
        services.AddSingleton<Services.Updates.IAppUpdater>(_ => new Services.Updates.VelopackUpdater());
        services.AddSingleton<Services.Updates.IUpdateService>(sp => new Services.Updates.UpdateService(
            sp.GetRequiredService<Services.Updates.IAppUpdater>(),
            sp.GetRequiredService<Services.Updates.IReleaseSource>(),
            sp.GetRequiredService<ILogger<Services.Updates.UpdateService>>()));
        services.AddSingleton<IAppShutdown, AvaloniaAppShutdown>();
        services.AddSingleton<IKataGoInstaller, DefaultKataGoInstaller>();
        services.AddSingleton<KataGoSetupViewModel>();
        services.AddSingleton<UpdateViewModel>();
        services.AddSingleton<GameViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
