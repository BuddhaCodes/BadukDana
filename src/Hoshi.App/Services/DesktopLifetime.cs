using Microsoft.Extensions.Hosting;

namespace Hoshi.App.Services;

/// <summary>
/// The host's lifetime for a desktop app: Avalonia owns the lifetime, so this one does nothing. It replaces the
/// default <c>ConsoleLifetime</c>, whose <c>AppDomain.ProcessExit</c> handler blocks until the host has been
/// disposed: when a service was slow to dispose, the process stayed alive after the window had closed (and even
/// <see cref="Environment.Exit(int)"/> waited for it).
/// </summary>
public sealed class DesktopLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
