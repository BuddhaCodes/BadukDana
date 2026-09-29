using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Ogs.Auth;

/// <summary>
/// Signs in to OGS and keeps the credentials fresh.
/// <list type="bullet">
/// <item>OAuth mode: authorization code + PKCE through the system browser and a 127.0.0.1 redirect. Only the refresh token
/// is persisted (in <see cref="ITokenStore"/>); the access token lives in memory.</item>
/// <item>Password mode (beta, development only): the web client's <c>/api/v0/login</c>. The password is sent once and never
/// kept; the session cookie lives in memory for this run only.</item>
/// </list>
/// Never logs tokens, JWTs, passwords or e-mail addresses.
/// </summary>
public sealed class OgsAuthService : IOgsCredentials
{
    public const string RefreshTokenKey = "ogs.refresh_token";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

    private readonly OgsOptions _options;
    private readonly HttpClient _http;
    private readonly ITokenStore _store;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CookieContainer _cookies = new();

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _accessExpires;

    public OgsAuthService(OgsOptions options, HttpClient http, ITokenStore store, TimeProvider? time = null, ILogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler? SessionChanged;

    public OgsOptions Options => _options;

    public OgsSession? Session { get; private set; }

    public bool IsSignedIn => Session is not null;

    /// <summary>Updates the JWT after the realtime socket pushes a new one (<c>user/jwt</c>).</summary>
    public void UpdateJwt(string jwt)
    {
        if (Session is { } s && !string.IsNullOrEmpty(jwt))
        {
            Session = s with { UserJwt = jwt };
        }
    }

    // ---------------- OAuth ----------------

    public async Task<OgsSession> SignInWithBrowserAsync(Func<Uri, CancellationToken, Task> openBrowser, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openBrowser);
        if (_options.AuthMode != OgsAuthMode.OAuth)
        {
            throw new InvalidOperationException("Este servidor usa inicio de sesión con usuario y contraseña.");
        }

        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new OgsAuthException("Falta Ogs:ClientId en la configuración (id de la aplicación OAuth registrada).");
        }

        string verifier = Pkce.CreateVerifier();
        string state = Pkce.CreateState();
        Uri redirect = _options.RedirectUri;
        var authorize = new UriBuilder(new Uri(_options.BaseUrl, "/oauth2/authorize/"))
        {
            Query = string.Join('&',
                Param("response_type", "code"),
                Param("client_id", _options.ClientId),
                Param("redirect_uri", redirect.GetLeftPart(UriPartial.Path)),
                Param("code_challenge", Pkce.ChallengeFor(verifier)),
                Param("code_challenge_method", "S256"),
                Param("state", state)),
        }.Uri;

        using LoopbackRedirectListener listener = LoopbackRedirectListener.Start(_options.RedirectPort, _options.RedirectPath);
        Task<string> codeTask = listener.WaitForCodeAsync(state, cancellationToken);
        _logger.LogInformation("Opening the browser for OGS authorization at {Host}", _options.BaseUrl.Host);
        await openBrowser(authorize, cancellationToken);
        string code = await codeTask;

        await RequestTokensAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirect.GetLeftPart(UriPartial.Path),
                ["client_id"] = _options.ClientId,
                ["code_verifier"] = verifier,
            },
            cancellationToken);

        return await LoadSessionAsync(cancellationToken)
            ?? throw new OgsAuthException("OGS aceptó el inicio de sesión pero no devolvió los datos del usuario.");
    }

    /// <summary>Signs in silently with the stored refresh token; null when there is none or it was revoked.</summary>
    public async Task<OgsSession?> RestoreAsync(CancellationToken cancellationToken)
    {
        if (_options.AuthMode != OgsAuthMode.OAuth)
        {
            return null;
        }

        _refreshToken = await _store.ReadAsync(RefreshTokenKey, cancellationToken);
        if (_refreshToken is null)
        {
            return null;
        }

        try
        {
            await RefreshAsync(cancellationToken);
            return await LoadSessionAsync(cancellationToken);
        }
        catch (OgsAuthException ex)
        {
            _logger.LogWarning("Stored OGS session could not be restored: {Reason}", ex.Message);
            await ForgetAsync(cancellationToken);
            return null;
        }
    }

    /// <summary>A valid access token, refreshing it when it is about to expire.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is null)
        {
            return null;
        }

        if (_time.GetUtcNow() + RefreshMargin >= _accessExpires && _refreshToken is not null)
        {
            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (_time.GetUtcNow() + RefreshMargin >= _accessExpires)
                {
                    await RefreshAsync(cancellationToken);
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        return _accessToken;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        if (_refreshToken is not null && _options.ClientId is not null)
        {
            try
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = _refreshToken,
                    ["token_type_hint"] = "refresh_token",
                    ["client_id"] = _options.ClientId,
                });
                using HttpResponseMessage _ = await _http.PostAsync(new Uri(_options.BaseUrl, "/oauth2/revoke_token/"), content, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogInformation("Token revocation failed ({Error}); forgetting it locally anyway", ex.StatusCode);
            }
        }

        await ForgetAsync(cancellationToken);
        _logger.LogInformation("Signed out of OGS");
    }

    // ---------------- Password (beta, development) ----------------

    public async Task<OgsSession> SignInWithPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (_options.AuthMode != OgsAuthMode.Password)
        {
            throw new InvalidOperationException("El inicio de sesión con contraseña solo está disponible en el modo de desarrollo (beta).");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrEmpty(password);

        // 1. Obtain the CSRF cookie the Django backend requires for POSTs.
        using (var probe = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUrl, "/api/v1/ui/config")))
        {
            AddCookies(probe);
            using HttpResponseMessage r = await _http.SendAsync(probe, cancellationToken);
            StoreCookies(r);
        }

        // 2. Log in exactly like the web client (views/SignIn).
        var body = new JsonObject
        {
            ["username"] = username.Trim(),
            ["password"] = password,
            ["ebi"] = $"{Guid.NewGuid():N}.0.0.0.{(int)TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes}",
            ["timezone"] = TimeZoneInfo.Local.Id,
        };
        using var login = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl, "/api/v0/login"))
        {
            Content = JsonContent.Create(body),
        };
        await ApplyAsync(login, cancellationToken);
        using HttpResponseMessage response = await _http.SendAsync(login, cancellationToken);
        StoreCookies(response);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OGS password login failed with {Status}", (int)response.StatusCode);
            throw new OgsAuthException(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest
                ? "Usuario o contraseña incorrectos."
                : string.Create(CultureInfo.InvariantCulture, $"OGS respondió {(int)response.StatusCode} al iniciar sesión."));
        }

        OgsSession session = ParseConfig(text)
            ?? throw new OgsAuthException("OGS no devolvió los datos del usuario tras iniciar sesión (¿verificación en dos pasos o SSO?).");
        SetSession(session);
        return session;
    }

    // ---------------- IOgsCredentials ----------------

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.AuthMode == OgsAuthMode.OAuth)
        {
            if (await GetAccessTokenAsync(cancellationToken) is { } token)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            return;
        }

        AddCookies(request);
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            if (_cookies.GetCookies(_options.BaseUrl)["csrftoken"]?.Value is { } csrf)
            {
                request.Headers.Add("X-CSRFToken", csrf);
            }

            request.Headers.Referrer = new Uri(_options.BaseUrl, "/");
        }
    }

    /// <summary>Records cookies set by a REST response made with these credentials (password mode).</summary>
    public void StoreCookies(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            foreach (string v in values)
            {
                try
                {
                    _cookies.SetCookies(_options.BaseUrl, v);
                }
                catch (CookieException)
                {
                    // Ignore malformed cookies.
                }
            }
        }
    }

    // ---------------- Internals ----------------

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_refreshToken is null || _options.ClientId is null)
        {
            throw new OgsAuthException("No hay token de actualización.");
        }

        await RequestTokensAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"] = _options.ClientId,
            },
            cancellationToken);
    }

    private async Task RequestTokensAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PostAsync(new Uri(_options.BaseUrl, "/oauth2/token/"), content, cancellationToken);
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string error = TryGetString(text, "error") ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            _logger.LogWarning("OGS token request ({Grant}) failed: {Error}", form["grant_type"], error);
            throw new OgsAuthException($"OGS rechazó la solicitud de token ({error}).");
        }

        using JsonDocument doc = JsonDocument.Parse(text);
        JsonElement root = doc.RootElement;
        _accessToken = root.GetProperty("access_token").GetString();
        int expiresIn = root.TryGetProperty("expires_in", out JsonElement e) && e.TryGetInt32(out int s) ? s : 3600;
        _accessExpires = _time.GetUtcNow().AddSeconds(expiresIn);
        if (root.TryGetProperty("refresh_token", out JsonElement rt) && rt.GetString() is { Length: > 0 } refresh)
        {
            _refreshToken = refresh;
            await _store.WriteAsync(RefreshTokenKey, refresh, cancellationToken);
        }

        _logger.LogInformation("Obtained OGS access token ({Grant}), valid for {Seconds} s", form["grant_type"], expiresIn);
    }

    private async Task<OgsSession?> LoadSessionAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.BaseUrl, "/api/v1/ui/config"));
        await ApplyAsync(request, cancellationToken);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OgsAuthException(string.Create(CultureInfo.InvariantCulture, $"OGS respondió {(int)response.StatusCode} al pedir ui/config."));
        }

        OgsSession? session = ParseConfig(await response.Content.ReadAsStringAsync(cancellationToken));
        if (session is not null)
        {
            SetSession(session);
        }

        return session;
    }

    private void SetSession(OgsSession session)
    {
        Session = session;
        _logger.LogInformation("Signed in to OGS ({Host}) as user {UserId}", _options.BaseUrl.Host, session.User.Id);
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ForgetAsync(CancellationToken cancellationToken)
    {
        bool hadSession = Session is not null;
        _accessToken = null;
        _refreshToken = null;
        Session = null;
        await _store.DeleteAsync(RefreshTokenKey, cancellationToken);
        if (hadSession)
        {
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Parses <c>ui/config</c> (or the login response, which has the same shape).</summary>
    internal static OgsSession? ParseConfig(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("user", out JsonElement user)
                || (user.TryGetProperty("anonymous", out JsonElement anon) && anon.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("user_jwt", out JsonElement jwt) || jwt.GetString() is not { Length: > 0 } token)
            {
                return null;
            }

            return new OgsSession(OgsJson.ReadUser(user), token);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void AddCookies(HttpRequestMessage request)
    {
        string header = _cookies.GetCookieHeader(_options.BaseUrl);
        if (header.Length > 0)
        {
            request.Headers.Remove("Cookie");
            request.Headers.Add("Cookie", header);
        }
    }

    private static string Param(string key, string value) => $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";

    private static string? TryGetString(string json, string property)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(property, out JsonElement v)
                ? v.ToString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
