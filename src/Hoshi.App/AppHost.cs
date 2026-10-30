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

        // Android has no files next to the app: the defaults travel inside the assembly.
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))
            && typeof(AppHost).Assembly.GetManifestResourceStream("Hoshi.appsettings.json") is { } defaults)
        {
            builder.Configuration.AddJsonStream(defaults);
        }

        // A backup chosen last time ("Import backup…") is merged now, before any service reads the files.
        try
        {
            ImportSummary = Services.Backup.BackupPackage.ApplyPending(AppPaths.DataDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException or FormatException)
        {
            ImportError = ex.Message;
            System.Diagnostics.Trace.WriteLine($"Hoshi: the pending import failed: {ex}");
        }

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

    /// <summary>What the import merged at this start (null when there was none).</summary>
    public static Services.Backup.BackupSummary? ImportSummary { get; private set; }

    /// <summary>Why the pending import could not be merged at this start, if it failed.</summary>
    public static string? ImportError { get; private set; }

    /// <summary>
    /// Phones (Android): windows become pages of the single view and dialogs a card (<see cref="Services.Phone.PhoneNavigation"/>).
    /// Call after <see cref="AddAppServices"/>; the later registrations win.
    /// </summary>
    public static IServiceCollection AddPhoneShell(this IServiceCollection services)
    {
        // Problems are resolved when first opened: their view model needs the game, whose dialogs are this navigation.
        services.AddSingleton(sp => new Services.Phone.PhoneNavigation(
            sp.GetRequiredService<PreferencesViewModel>(), sp.GetRequiredService<LobbyViewModel>(), sp.GetRequiredService<ReplaysViewModel>(),
            () => sp.GetRequiredService<TsumegoViewModel>()));
        services.AddSingleton<IPreferencesWindowService>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<ILobbyWindowService>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<IReplaysWindowService>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<ITsumegoWindowService>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<Services.Engines.IEngineWindows>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<Services.Phone.PhoneNavigation>());
        services.AddSingleton<PhoneShellViewModel>();
        return services;
    }

    /// <summary>Registers the application's own services, view models and views.</summary>
    public static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        services.AddSingleton<IHostLifetime, DesktopLifetime>();
        services.AddSingleton<IFileDialogService, AvaloniaFileDialogService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<Themes.ThemeService>();
        services.AddSingleton<SystemSoundService>();
        services.AddSingleton<ZenMode>();
        services.AddSingleton<ISoundService>(sp => new MutableSoundService(sp.GetRequiredService<SystemSoundService>(), sp.GetRequiredService<ISettingsService>(), sp.GetRequiredService<ZenMode>()));
        services.AddSingleton<AudioViewModel>();
        services.AddSingleton<EffectsViewModel>();
        services.AddSingleton<Services.Music.IMusicService, Services.Music.MusicService>();
        services.AddSingleton<AnalysisEngineHost>();
        services.AddSingleton<Services.Engines.GtpEngineHost>();
        services.AddSingleton<Services.Engines.IGtpEngineHost>(sp => sp.GetRequiredService<Services.Engines.GtpEngineHost>());
        services.AddSingleton<Services.Engines.AnalysisEngineSwitch>();
        services.AddSingleton<IAnalysisEngine>(sp => sp.GetRequiredService<Services.Engines.AnalysisEngineSwitch>());
        services.AddSingleton<GtpConsoleViewModel>();
        services.AddSingleton<Services.Engines.IEngineWindows, Services.Engines.EngineWindows>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();
        services.AddSingleton<PreferencesViewModel>();
        services.AddSingleton<IPreferencesWindowService, PreferencesWindowService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<LobbyViewModel>();
        services.AddSingleton<ILobbyWindowService, LobbyWindowService>();
        services.AddSingleton<IReplayStore, ReplayStore>();
        services.AddSingleton<ReplaysViewModel>();
        services.AddSingleton<IReplaysWindowService, ReplaysWindowService>();
        services.AddSingleton(_ => new Services.Tsumego.TsumegoLibrary(AppPaths.DataDirectory));
        services.AddSingleton(sp => new TsumegoViewModel(
            sp.GetRequiredService<Services.Tsumego.TsumegoLibrary>(), sp.GetRequiredService<IFileDialogService>(), sp.GetRequiredService<GameViewModel>()));
        services.AddSingleton<ITsumegoWindowService, TsumegoWindowService>();
        services.AddSingleton<IReplayMakerWindowService, ReplayMakerWindowService>();
        services.AddSingleton<WelcomeViewModel>();
        services.AddSingleton(sp => new Services.Presence.DiscordPresence(settings: sp.GetService<ISettingsService>(), logger: sp.GetService<ILogger<Services.Presence.DiscordPresence>>()));
        services.AddSingleton(_ => new Services.Summary.ReviewLog(AppPaths.DataDirectory));
        services.AddSingleton(sp => new Services.Summary.SummarySource(
            sp.GetService<IReplayStore>(), sp.GetRequiredService<Services.Summary.ReviewLog>(), sp.GetService<Services.Joseki.IJosekiLibrary>(),
            sp.GetRequiredService<Services.Tsumego.TsumegoLibrary>(), sp.GetService<ISettingsService>()));
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
        services.AddSingleton(sp => new BackupViewModel(sp.GetService<IFileDialogService>(), sp.GetService<IDialogService>()));
        services.AddSingleton(sp => new ReportViewModel(sp.GetService<IBrowserLauncher>(), sp.GetService<IClipboardService>()));
        services.AddSingleton<GameViewModel>();
        services.AddSingleton<EngineMatchViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<MainWindow>();
        return services;
    }
}
