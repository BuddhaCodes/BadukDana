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
        // Velopack: install/uninstall/update hooks (shortcuts and the like) run here and exit; must come first.
        Velopack.VelopackApp.Build().SetArgs(args).Run();

        IHost host = AppHost.Create(args);
        host.Start();

        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(Program));
        logger.LogInformation("Hoshi {Version} starting on {OS}", typeof(Program).Assembly.GetName().Version, Environment.OSVersion);

        int exitCode = 1;
        try
        {
            exitCode = BuildAvaloniaApp(() => new App(host.Services)).StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Unhandled exception, shutting down");
        }
        finally
        {
            Shutdown(host, logger);
        }

        // Nothing may keep the process alive once the window is gone (a foreground thread, a native handle…).
        Environment.Exit(exitCode);
        return exitCode;
    }

    /// <summary>
    /// Saves the game, stops the music and KataGo, then disposes the host. Runs off the UI thread's
    /// SynchronizationContext, so no async disposal can deadlock waiting for a UI thread that no longer pumps.
    /// A watchdog terminates the process if anything still hangs.
    /// </summary>
    private static void Shutdown(IHost host, ILogger logger)
    {
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(8));
            try
            {
                logger.LogWarning("Shutdown did not finish in time; terminating");
            }
            catch (ObjectDisposedException)
            {
                // The logger is already gone.
            }

            System.Diagnostics.Process.GetCurrentProcess().Kill();
        })
        { IsBackground = true, Name = "Hoshi shutdown watchdog" };
        watchdog.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Synchronous and owned by the UI thread's objects: done here, before leaving it.
            host.Services.GetService<ViewModels.MainWindowViewModel>()?.SaveCurrentGame();
            host.Services.GetService<Services.Music.IMusicService>()?.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Error while saving before shutdown");
        }

        bool finished = Task.Run(async () =>
        {
            try
            {
                if (host.Services.GetService<Services.AnalysisEngineHost>() is { } engine)
                {
                    await engine.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4));
                }

                await host.StopAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is AggregateException or InvalidOperationException or ObjectDisposedException or OperationCanceledException or TimeoutException or IOException)
            {
                logger.LogWarning(ex, "Error while shutting down");
            }

            logger.LogInformation("Hoshi stopped ({Ms} ms)", clock.ElapsedMilliseconds);
            try
            {
                if (host is IAsyncDisposable async)
                {
                    await async.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                }
                else
                {
                    host.Dispose();
                }
            }
            catch (Exception ex) when (ex is AggregateException or InvalidOperationException or ObjectDisposedException or TimeoutException)
            {
                // Logging may already be closed; the process exits right after anyway.
                System.Diagnostics.Trace.WriteLine($"Hoshi: host disposal failed: {ex.Message}");
            }
        }).Wait(TimeSpan.FromSeconds(7));

        if (!finished)
        {
            System.Diagnostics.Trace.WriteLine("Hoshi: shutdown timed out");
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
