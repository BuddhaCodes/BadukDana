using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hoshi.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        IHost host = AppHost.Create(args);
        host.Start();

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        logger.LogInformation("Hoshi {Version} starting on {OS}", typeof(Program).Assembly.GetName().Version, Environment.OSVersion);

        try
        {
            return BuildAvaloniaApp(() => new App(host.Services)).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception, shutting down");
            throw;
        }
        finally
        {
            logger.LogInformation("Hoshi stopped");
            Shutdown(host, logger);
        }
    }

    /// <summary>
    /// Stops the music and KataGo, then disposes the host asynchronously (several services only implement
    /// IAsyncDisposable, which a synchronous Dispose would reject). A watchdog makes sure the process never
    /// lingers after the window has closed, whatever a service does.
    /// </summary>
    private static void Shutdown(IHost host, ILogger logger)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(8));
            logger.LogWarning("Shutdown did not finish in time; exiting");
            Environment.Exit(0);
        })
        { IsBackground = true, Name = "Hoshi shutdown watchdog" };
        watchdog.Start();

        try
        {
            host.Services.GetService<ViewModels.MainWindowViewModel>()?.SaveCurrentGame();
            host.Services.GetService<Services.Music.IMusicService>()?.Stop();
            if (host.Services.GetService<Services.AnalysisEngineHost>() is { } engine)
            {
                engine.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(4));
            }

            host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            if (host is IAsyncDisposable async)
            {
                async.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
            else
            {
                host.Dispose();
            }
        }
        catch (Exception ex) when (ex is AggregateException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Error while shutting down");
        }
    }

    /// <summary>Used by the Avalonia designer/previewer. Do not remove.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(() => new App());

    private static AppBuilder BuildAvaloniaApp(Func<Avalonia.Application> appFactory) =>
        AppBuilder.Configure(appFactory)
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
