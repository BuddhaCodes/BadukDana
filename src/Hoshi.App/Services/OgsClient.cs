using Avalonia.Threading;
using Hoshi.Ogs;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Realtime;
using Hoshi.Ogs.Rest;
using Microsoft.Extensions.Logging;

namespace Hoshi.App.Services;

/// <summary>
/// What the lobby needs from OGS: one facade over <see cref="OgsAuthService"/>, <see cref="OgsRestClient"/>,
/// <see cref="OgsRealtimeClient"/> and <see cref="OgsSeekGraph"/>. Events may fire on any thread.
/// </summary>
public interface IOgsClient
{
    OgsOptions Options { get; }

    OgsSession? Session { get; }

    OgsConnectionState ConnectionState { get; }

    IReadOnlyList<OgsOpenChallenge> OpenChallenges { get; }

    event EventHandler? SessionChanged;

    event EventHandler? ConnectionStateChanged;

    event EventHandler? OpenChallengesChanged;

    /// <summary>Signs in silently with a stored token; false when the user has to sign in.</summary>
    Task<bool> RestoreAsync(CancellationToken cancellationToken);

    /// <summary>Browser OAuth sign-in, optionally starting at one of OGS's social logins (Google…).</summary>
    Task SignInWithBrowserAsync(OgsLoginProvider provider, CancellationToken cancellationToken);

    Task SignInWithPasswordAsync(string username, string password, CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OgsActiveGame>> GetActiveGamesAsync(CancellationToken cancellationToken);

    Task<OgsUser?> FindPlayerAsync(string username, CancellationToken cancellationToken);

    Task<CreatedChallenge> CreateChallengeAsync(ChallengeRequest request, long? opponentId, CancellationToken cancellationToken);

    /// <summary>Keeps a live challenge alive until someone accepts it (true) or it is cancelled (false).</summary>
    Task<bool> WaitForOpponentAsync(CreatedChallenge challenge, CancellationToken cancellationToken);

    Task CancelChallengeAsync(long challengeId, CancellationToken cancellationToken);

    /// <summary>Accepts an open challenge; returns the new game's id (0 when the server does not say).</summary>
    Task<long> AcceptChallengeAsync(long challengeId, CancellationToken cancellationToken);
}

/// <summary>Opens a URL in the user's browser.</summary>
public interface IBrowserLauncher
{
    Task OpenAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>Posts work to the UI thread (immediate in tests).</summary>
public interface IUiDispatcher
{
    void Post(Action action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}

public sealed class AvaloniaBrowserLauncher : IBrowserLauncher
{
    public async Task OpenAsync(Uri uri, CancellationToken cancellationToken)
    {
        bool opened = await Dispatcher.UIThread.InvokeAsync(async () =>
            MainWindowLocator.MainWindow?.Launcher is { } launcher && await launcher.LaunchUriAsync(uri));
        if (!opened)
        {
            throw new OgsAuthException("No se pudo abrir el navegador. Abre manualmente la página de autorización de OGS.");
        }
    }
}

public sealed class OgsClient : IOgsClient, IAsyncDisposable
{
    private readonly OgsAuthService _auth;
    private readonly OgsRestClient _rest;
    private readonly OgsRealtimeClient _realtime;
    private readonly IBrowserLauncher _browser;
    private readonly ILogger<OgsClient> _logger;
    private OgsSeekGraph? _seekGraph;

    public OgsClient(OgsAuthService auth, OgsRestClient rest, OgsRealtimeClient realtime, IBrowserLauncher browser, ILogger<OgsClient> logger)
    {
        _auth = auth;
        _rest = rest;
        _realtime = realtime;
        _browser = browser;
        _logger = logger;
        _auth.SessionChanged += (_, _) => SessionChanged?.Invoke(this, EventArgs.Empty);
        _realtime.StateChanged += (_, _) => ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
        _realtime.JwtUpdated += (_, jwt) => _auth.UpdateJwt(jwt);
    }

    public event EventHandler? SessionChanged;

    public event EventHandler? ConnectionStateChanged;

    public event EventHandler? OpenChallengesChanged;

    public OgsOptions Options => _auth.Options;

    public OgsSession? Session => _auth.Session;

    public OgsConnectionState ConnectionState => _realtime.State;

    public IReadOnlyList<OgsOpenChallenge> OpenChallenges => _seekGraph?.Challenges ?? [];

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (await _auth.RestoreAsync(cancellationToken) is null)
        {
            return false;
        }

        await GoOnlineAsync(cancellationToken);
        return true;
    }

    public async Task SignInWithBrowserAsync(OgsLoginProvider provider, CancellationToken cancellationToken)
    {
        await _auth.SignInWithBrowserAsync(provider, _browser.OpenAsync, cancellationToken);
        await GoOnlineAsync(cancellationToken);
    }

    public async Task SignInWithPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        await _auth.SignInWithPasswordAsync(username, password, cancellationToken);
        await GoOnlineAsync(cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await GoOfflineAsync();
        await _auth.SignOutAsync(cancellationToken);
    }

    public Task<IReadOnlyList<OgsActiveGame>> GetActiveGamesAsync(CancellationToken cancellationToken) =>
        _rest.GetActiveGamesAsync(cancellationToken);

    public Task<OgsUser?> FindPlayerAsync(string username, CancellationToken cancellationToken) =>
        _rest.FindPlayerAsync(username, cancellationToken);

    public Task<CreatedChallenge> CreateChallengeAsync(ChallengeRequest request, long? opponentId, CancellationToken cancellationToken) =>
        _rest.CreateChallengeAsync(request, opponentId, cancellationToken);

    public Task<bool> WaitForOpponentAsync(CreatedChallenge challenge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return ChallengeKeepAlive.WaitForOpponentAsync(
            _realtime, challenge.ChallengeId, challenge.GameId, TimeSpan.FromSeconds(1), cancellationToken);
    }

    public Task CancelChallengeAsync(long challengeId, CancellationToken cancellationToken) =>
        _rest.CancelChallengeAsync(challengeId, cancellationToken);

    public Task<long> AcceptChallengeAsync(long challengeId, CancellationToken cancellationToken) =>
        _rest.AcceptChallengeAsync(challengeId, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await GoOfflineAsync();
        await _realtime.DisposeAsync();
    }

    private async Task GoOnlineAsync(CancellationToken cancellationToken)
    {
        await _realtime.StartAsync(CancellationToken.None);
        if (_seekGraph is null)
        {
            _seekGraph = new OgsSeekGraph(_realtime);
            _seekGraph.Changed += (_, _) => OpenChallengesChanged?.Invoke(this, EventArgs.Empty);
        }

        _logger.LogInformation("Signed in to {Host} as user {UserId}", Options.BaseUrl.Host, Session?.User.Id);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private async Task GoOfflineAsync()
    {
        _seekGraph?.Dispose();
        _seekGraph = null;
        OpenChallengesChanged?.Invoke(this, EventArgs.Empty);
        await _realtime.StopAsync();
    }
}
