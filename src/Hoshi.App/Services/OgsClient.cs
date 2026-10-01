using Avalonia.Threading;
using Hoshi.Core.Localization;
using Hoshi.Ogs;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Games;
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
    /// <summary>The servers the user can pick from (online-go.com and beta).</summary>
    IReadOnlyList<OgsOptions> Servers { get; }

    /// <summary>The selected server.</summary>
    OgsOptions Options { get; }

    OgsSession? Session { get; }

    OgsConnectionState ConnectionState { get; }

    IReadOnlyList<OgsOpenChallenge> OpenChallenges { get; }

    event EventHandler? SessionChanged;

    event EventHandler? ConnectionStateChanged;

    event EventHandler? OpenChallengesChanged;

    event EventHandler? ServerChanged;

    /// <summary>Switches server; only allowed while signed out.</summary>
    void SelectServer(OgsOptions server);

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

    /// <summary>Joins a game on the current server (not yet connected: call <see cref="IOnlineGame.Connect"/>).</summary>
    IOnlineGame OpenGame(long gameId);
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
            throw new OgsAuthException(Tr.T("Online.BrowserFailed"));
        }
    }
}

public sealed class OgsClient : IOgsClient, IAsyncDisposable
{
    private readonly OgsServerCatalog _servers;
    private readonly IOgsConnectionFactory _factory;
    private readonly IBrowserLauncher _browser;
    private readonly ILogger<OgsClient> _logger;
    private readonly Dictionary<Uri, OgsConnection> _connections = [];
    private OgsConnection _current;
    private OgsSeekGraph? _seekGraph;

    public OgsClient(OgsServerCatalog servers, IOgsConnectionFactory factory, IBrowserLauncher browser, ILogger<OgsClient> logger)
    {
        _servers = servers;
        _factory = factory;
        _browser = browser;
        _logger = logger;
        _current = Connect(servers.Initial);
    }

    public event EventHandler? SessionChanged;

    public event EventHandler? ConnectionStateChanged;

    public event EventHandler? OpenChallengesChanged;

    public event EventHandler? ServerChanged;

    public IReadOnlyList<OgsOptions> Servers => _servers.All;

    public OgsOptions Options => _current.Auth.Options;

    public OgsSession? Session => _current.Auth.Session;

    public OgsConnectionState ConnectionState => _current.Realtime.State;

    public IReadOnlyList<OgsOpenChallenge> OpenChallenges => _seekGraph?.Challenges ?? [];

    public void SelectServer(OgsOptions server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (server.BaseUrl == Options.BaseUrl)
        {
            return;
        }

        if (Session is not null)
        {
            throw new InvalidOperationException(Tr.T("Online.SignOutBeforeSwitch"));
        }

        _current = Connect(server);
        _logger.LogInformation("Selected OGS server {Host}", server.BaseUrl.Host);
        ServerChanged?.Invoke(this, EventArgs.Empty);
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restores a stored OAuth session (only online-go.com keeps tokens), switching to that server.</summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        OgsOptions original = Options;
        foreach (OgsOptions server in _servers.All.OrderBy(s => s.BaseUrl == original.BaseUrl ? 0 : 1))
        {
            if (server.AuthMode != OgsAuthMode.OAuth)
            {
                continue;
            }

            OgsConnection c = Connect(server);
            if (await c.Auth.RestoreAsync(cancellationToken) is not null)
            {
                if (c != _current)
                {
                    _current = c;
                    ServerChanged?.Invoke(this, EventArgs.Empty);
                }

                await GoOnlineAsync(cancellationToken);
                return true;
            }
        }

        return false;
    }

    public async Task SignInWithBrowserAsync(OgsLoginProvider provider, CancellationToken cancellationToken)
    {
        await _current.Auth.SignInWithBrowserAsync(provider, _browser.OpenAsync, cancellationToken);
        await GoOnlineAsync(cancellationToken);
    }

    public async Task SignInWithPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        await _current.Auth.SignInWithPasswordAsync(username, password, cancellationToken);
        await GoOnlineAsync(cancellationToken);
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await GoOfflineAsync();
        await _current.Auth.SignOutAsync(cancellationToken);
    }

    public Task<IReadOnlyList<OgsActiveGame>> GetActiveGamesAsync(CancellationToken cancellationToken) =>
        _current.Rest.GetActiveGamesAsync(cancellationToken);

    public Task<OgsUser?> FindPlayerAsync(string username, CancellationToken cancellationToken) =>
        _current.Rest.FindPlayerAsync(username, cancellationToken);

    public Task<CreatedChallenge> CreateChallengeAsync(ChallengeRequest request, long? opponentId, CancellationToken cancellationToken) =>
        _current.Rest.CreateChallengeAsync(request, opponentId, cancellationToken);

    public Task<bool> WaitForOpponentAsync(CreatedChallenge challenge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return ChallengeKeepAlive.WaitForOpponentAsync(
            _current.Realtime, challenge.ChallengeId, challenge.GameId, TimeSpan.FromSeconds(1), cancellationToken);
    }

    public Task CancelChallengeAsync(long challengeId, CancellationToken cancellationToken) =>
        _current.Rest.CancelChallengeAsync(challengeId, cancellationToken);

    public Task<long> AcceptChallengeAsync(long challengeId, CancellationToken cancellationToken) =>
        _current.Rest.AcceptChallengeAsync(challengeId, cancellationToken);

    public IOnlineGame OpenGame(long gameId)
    {
        if (Session is null)
        {
            throw new InvalidOperationException(Tr.T("Online.SignInToOpen"));
        }

        var session = new OgsGameSession(_current.Realtime, gameId, _logger);
        return new OgsOnlineGame(session, _current.Realtime, Session.User.Id);
    }

    public async ValueTask DisposeAsync()
    {
        await GoOfflineAsync();
        foreach (OgsConnection c in _connections.Values)
        {
            await c.Realtime.DisposeAsync();
        }
    }

    private OgsConnection Connect(OgsOptions server)
    {
        if (!_connections.TryGetValue(server.BaseUrl, out OgsConnection? c))
        {
            c = _factory.Create(server);
            OgsConnection captured = c;
            c.Auth.SessionChanged += (_, _) =>
            {
                if (captured == _current)
                {
                    SessionChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            c.Realtime.StateChanged += (_, _) =>
            {
                if (captured == _current)
                {
                    ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
                }
            };
            c.Realtime.JwtUpdated += (_, jwt) => captured.Auth.UpdateJwt(jwt);
            _connections[server.BaseUrl] = c;
        }

        return c;
    }

    private async Task GoOnlineAsync(CancellationToken cancellationToken)
    {
        await _current.Realtime.StartAsync(CancellationToken.None);
        if (_seekGraph is null)
        {
            _seekGraph = new OgsSeekGraph(_current.Realtime);
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
        await _current.Realtime.StopAsync();
    }
}
