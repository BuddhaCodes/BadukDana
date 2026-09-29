namespace Hoshi.Ogs;

/// <summary>How Hoshi signs in to the server.</summary>
public enum OgsAuthMode
{
    /// <summary>OAuth2 authorization code + PKCE in the system browser (public client). Used for online-go.com.</summary>
    OAuth,

    /// <summary>
    /// Development only: the web client's username/password login (<c>/api/v0/login</c>), used for beta.online-go.com
    /// where OAuth applications cannot be registered. The password is sent once and never stored.
    /// </summary>
    Password,
}

/// <summary>
/// Where the user proves who they are before authorizing Hoshi. Social providers are OGS's own
/// (python-social-auth, <c>/login/{provider}/?next=</c>): Hoshi never talks to Google and never sees its tokens.
/// </summary>
public enum OgsLoginProvider
{
    /// <summary>OGS's own sign-in page (username/password, or any provider the user picks there).</summary>
    Ogs,
    Google,
    Facebook,
    GitHub,
    Apple,
}

/// <summary>Configuration for one OGS server (bound from <c>Ogs</c> in appsettings).</summary>
public sealed record OgsOptions
{
    public Uri BaseUrl { get; init; } = new("https://online-go.com");

    /// <summary>WebSocket endpoint; defaults to the base URL with a ws/wss scheme (the socket lives at the host root).</summary>
    public Uri? WebSocketUrl { get; init; }

    /// <summary>OAuth client id of the registered public application (not a secret).</summary>
    public string? ClientId { get; init; }

    public OgsAuthMode AuthMode { get; init; } = OgsAuthMode.OAuth;

    /// <summary>Loopback port registered in the OAuth application's redirect URI.</summary>
    public int RedirectPort { get; init; } = 8734;

    public string RedirectPath { get; init; } = "/callback";

    public string ClientName { get; init; } = "hoshi";

    public string ClientVersion { get; init; } = "0.1.0";

    public Uri RedirectUri => new($"http://127.0.0.1:{RedirectPort}{RedirectPath}");

    public Uri EffectiveWebSocketUrl => WebSocketUrl ?? new UriBuilder(BaseUrl)
    {
        Scheme = BaseUrl.Scheme == Uri.UriSchemeHttp ? "ws" : "wss",
        Path = "/",
    }.Uri;

    public bool IsProduction => BaseUrl.Host.Equals("online-go.com", StringComparison.OrdinalIgnoreCase);
}
