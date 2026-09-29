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
        using IHost host = AppHost.Create(args);
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
            host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
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
