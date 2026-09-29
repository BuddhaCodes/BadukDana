using System.Net;
using System.Text.Json;
using System.Web;
using Hoshi.Ogs.Auth;

namespace Hoshi.Ogs.Tests;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_matches_the_RFC_7636_example()
    {
        Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk")
            .Should().Be("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM");
    }

    [Fact]
    public void Verifiers_are_long_random_and_url_safe()
    {
        string a = Pkce.CreateVerifier();
        string b = Pkce.CreateVerifier();

        a.Length.Should().BeInRange(43, 128);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        a.Should().NotBe(b);
    }
}

public sealed class LoopbackRedirectListenerTests
{
    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Receives_the_authorization_code()
    {
        int port = FreePort();
        using var listener = LoopbackRedirectListener.Start(port, "/callback");
        Task<string> code = listener.WaitForCodeAsync("state-1", CancellationToken.None);

        using var http = new HttpClient();
        string page = await http.GetStringAsync($"http://127.0.0.1:{port}/callback?code=abc123&state=state-1");

        (await code).Should().Be("abc123");
        page.Should().Contain("Hoshi");
    }

    [Fact]
    public async Task Ignores_unrelated_requests_such_as_favicon()
    {
        int port = FreePort();
        using var listener = LoopbackRedirectListener.Start(port, "/callback");
        Task<string> code = listener.WaitForCodeAsync("s", CancellationToken.None);

        using var http = new HttpClient();
        (await http.GetAsync($"http://127.0.0.1:{port}/favicon.ico")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await http.GetStringAsync($"http://127.0.0.1:{port}/callback?code=c&state=s");

        (await code).Should().Be("c");
    }

    [Fact]
    public async Task Rejects_a_mismatched_state()
    {
        int port = FreePort();
        using var listener = LoopbackRedirectListener.Start(port, "/callback");
        Task<string> code = listener.WaitForCodeAsync("expected", CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync($"http://127.0.0.1:{port}/callback?code=c&state=forged");

        await FluentActions.Awaiting(() => code).Should().ThrowAsync<OgsAuthException>().WithMessage("*state*");
    }

    [Fact]
    public async Task Reports_an_authorization_error()
    {
        int port = FreePort();
        using var listener = LoopbackRedirectListener.Start(port, "/callback");
        Task<string> code = listener.WaitForCodeAsync("s", CancellationToken.None);

        using var http = new HttpClient();
        await http.GetAsync($"http://127.0.0.1:{port}/callback?error=access_denied&state=s");

        await FluentActions.Awaiting(() => code).Should().ThrowAsync<OgsAuthException>().WithMessage("*access_denied*");
    }

    [Fact]
    public async Task Can_be_cancelled()
    {
        using var listener = LoopbackRedirectListener.Start(FreePort(), "/callback");
        using var cts = new CancellationTokenSource(50);

        await FluentActions.Awaiting(() => listener.WaitForCodeAsync("s", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Busy_port_is_reported_clearly()
    {
        int port = FreePort();
        using var first = LoopbackRedirectListener.Start(port, "/callback");

        FluentActions.Invoking(() => LoopbackRedirectListener.Start(port, "/callback"))
            .Should().Throw<OgsAuthException>().WithMessage($"*{port}*");
    }
}

public sealed class OgsAuthServiceTests
{
    private static readonly OgsOptions OAuthOptions = new()
    {
        BaseUrl = new Uri("https://online-go.com"),
        ClientId = "test-client-id",
        AuthMode = OgsAuthMode.OAuth,
    };

    private static readonly OgsOptions PasswordOptions = new()
    {
        BaseUrl = new Uri("https://beta.online-go.com"),
        AuthMode = OgsAuthMode.Password,
    };

    private static (OgsAuthService Service, FakeHttpHandler Http, InMemoryTokenStore Store, FakeClock Clock, CapturingLoggerFactory Logs)
        Create(OgsOptions options)
    {
        var http = new FakeHttpHandler();
        var store = new InMemoryTokenStore();
        var clock = new FakeClock();
        var logs = new CapturingLoggerFactory();
        var client = new HttpClient(http) { BaseAddress = options.BaseUrl };
        var service = new OgsAuthService(options, client, store, clock, logs.CreateLogger("auth"));
        return (service, http, store, clock, logs);
    }

    /// <summary>Plays the part of the browser: follows the authorize URL straight to our redirect with a code.</summary>
    private static Func<Uri, CancellationToken, Task> Browser(Action<Uri>? inspect = null) => async (uri, ct) =>
    {
        inspect?.Invoke(uri);
        var q = HttpUtility.ParseQueryString(uri.Query);
        using var http = new HttpClient();
        await http.GetAsync($"{q["redirect_uri"]}?code=auth-code-1&state={q["state"]}", ct);
    };

    [Fact]
    public async Task Browser_sign_in_uses_authorization_code_with_PKCE()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, _) = Create(OAuthOptions with { RedirectPort = FreePort() });
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK, Fixtures.Read("token_response.json"))
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));
        Uri? authorize = null;

        OgsSession session = await auth.SignInWithBrowserAsync(Browser(u => authorize = u), CancellationToken.None);

        var q = HttpUtility.ParseQueryString(authorize!.Query);
        authorize.GetLeftPart(UriPartial.Path).Should().Be("https://online-go.com/oauth2/authorize/");
        q["response_type"].Should().Be("code");
        q["client_id"].Should().Be("test-client-id");
        q["redirect_uri"].Should().StartWith("http://127.0.0.1:").And.EndWith("/callback");
        q["code_challenge_method"].Should().Be("S256");

        var form = HttpUtility.ParseQueryString(http.Single("POST", "/oauth2/token/").Body!);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("auth-code-1");
        form["client_id"].Should().Be("test-client-id");
        form["redirect_uri"].Should().Be(q["redirect_uri"]);
        Pkce.ChallengeFor(form["code_verifier"]!).Should().Be(q["code_challenge"]);
        form["client_secret"].Should().BeNull("a desktop app is a public client");

        http.Single("GET", "/api/v1/ui/config").Request.Headers.Authorization!.ToString().Should().Be("Bearer access-token-1");
        session.User.Should().Be(new OgsUser(1001, "kuro_test", 25.4, false));
        session.UserJwt.Should().Be("eyJ0eXAiOiJKV1QifQ.test-jwt.signature");
        store.Values.Should().ContainKey(OgsAuthService.RefreshTokenKey).WhoseValue.Should().Be("refresh-token-1");
        auth.Session.Should().BeSameAs(session);
    }

    [Fact]
    public async Task Google_sign_in_goes_through_OGS_social_login_and_then_the_same_PKCE_authorization()
    {
        (OgsAuthService auth, FakeHttpHandler http, _, _, _) = Create(OAuthOptions with { RedirectPort = FreePort() });
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK, Fixtures.Read("token_response.json"))
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));
        Uri? opened = null;

        // The real browser signs in with Google on OGS, which then redirects to `next` (the authorize URL).
        OgsSession session = await auth.SignInWithBrowserAsync(
            OgsLoginProvider.Google,
            (uri, ct) =>
            {
                opened = uri;
                var next = new Uri(new Uri("https://online-go.com"), HttpUtility.ParseQueryString(uri.Query)["next"]!);
                return Browser()(next, ct);
            },
            CancellationToken.None);

        opened!.GetLeftPart(UriPartial.Path).Should().Be("https://online-go.com/login/google-oauth2/");
        string next = HttpUtility.ParseQueryString(opened.Query)["next"]!;
        next.Should().StartWith("/oauth2/authorize/?", "social login only accepts a same-site relative next");
        var q = HttpUtility.ParseQueryString(new Uri(new Uri("https://online-go.com"), next).Query);
        q["client_id"].Should().Be("test-client-id");
        q["code_challenge_method"].Should().Be("S256");
        q["state"].Should().NotBeNullOrEmpty();
        session.User.Username.Should().Be("kuro_test");
        opened.ToString().Should().NotContain("secret");
    }

    [Theory]
    [InlineData(OgsLoginProvider.Google, "google-oauth2")]
    [InlineData(OgsLoginProvider.GitHub, "github")]
    [InlineData(OgsLoginProvider.Facebook, "facebook")]
    [InlineData(OgsLoginProvider.Apple, "apple-id")]
    public void Social_providers_map_to_the_OGS_login_routes(OgsLoginProvider provider, string slug)
    {
        OgsAuthService.SocialLoginPath(provider).Should().Be($"/login/{slug}/");
    }

    [Fact]
    public async Task Browser_sign_in_requires_a_client_id()
    {
        (OgsAuthService auth, _, _, _, _) = Create(OAuthOptions with { ClientId = null });

        await FluentActions.Awaiting(() => auth.SignInWithBrowserAsync(Browser(), CancellationToken.None))
            .Should().ThrowAsync<OgsAuthException>().WithMessage("*ClientId*");
    }

    [Fact]
    public async Task Restore_uses_the_stored_refresh_token_and_stores_the_rotated_one()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, _) = Create(OAuthOptions);
        store.Values[OgsAuthService.RefreshTokenKey] = "old-refresh";
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK,
                """{"access_token":"a2","expires_in":3600,"refresh_token":"new-refresh","token_type":"Bearer"}""")
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));

        OgsSession? session = await auth.RestoreAsync(CancellationToken.None);

        session!.User.Username.Should().Be("kuro_test");
        var form = HttpUtility.ParseQueryString(http.Single("POST", "/oauth2/token/").Body!);
        form["grant_type"].Should().Be("refresh_token");
        form["refresh_token"].Should().Be("old-refresh");
        store.Values[OgsAuthService.RefreshTokenKey].Should().Be("new-refresh");
    }

    [Fact]
    public async Task Restore_without_a_stored_token_returns_null()
    {
        (OgsAuthService auth, FakeHttpHandler http, _, _, _) = Create(OAuthOptions);

        (await auth.RestoreAsync(CancellationToken.None)).Should().BeNull();
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Restore_with_a_revoked_token_clears_it()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, _) = Create(OAuthOptions);
        store.Values[OgsAuthService.RefreshTokenKey] = "revoked";
        http.On("POST", "/oauth2/token/", HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");

        (await auth.RestoreAsync(CancellationToken.None)).Should().BeNull();
        store.Values.Should().NotContainKey(OgsAuthService.RefreshTokenKey);
    }

    [Fact]
    public async Task Access_token_is_refreshed_shortly_before_it_expires()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, FakeClock clock, _) = Create(OAuthOptions);
        store.Values[OgsAuthService.RefreshTokenKey] = "r1";
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK, """{"access_token":"a1","expires_in":600,"refresh_token":"r2"}""")
            .On("POST", "/oauth2/token/", HttpStatusCode.OK, """{"access_token":"a2","expires_in":600,"refresh_token":"r3"}""")
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));
        await auth.RestoreAsync(CancellationToken.None);

        (await auth.GetAccessTokenAsync(CancellationToken.None)).Should().Be("a1");
        clock.Now += TimeSpan.FromMinutes(9.5);

        (await auth.GetAccessTokenAsync(CancellationToken.None)).Should().Be("a2");
        store.Values[OgsAuthService.RefreshTokenKey].Should().Be("r3");
    }

    [Fact]
    public async Task Sign_out_forgets_the_session_and_the_stored_token()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, _) = Create(OAuthOptions);
        store.Values[OgsAuthService.RefreshTokenKey] = "r1";
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK, Fixtures.Read("token_response.json"))
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"))
            .On("POST", "/oauth2/revoke_token/", HttpStatusCode.OK, "{}");
        await auth.RestoreAsync(CancellationToken.None);
        int changes = 0;
        auth.SessionChanged += (_, _) => changes++;

        await auth.SignOutAsync(CancellationToken.None);

        auth.Session.Should().BeNull();
        store.Values.Should().BeEmpty();
        changes.Should().Be(1);
    }

    [Fact]
    public async Task Password_sign_in_uses_the_web_login_with_csrf_and_stores_nothing()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, _) = Create(PasswordOptions);
        http.On("GET", "/api/v1/ui/config", HttpStatusCode.OK, """{"user":{"anonymous":true,"id":0,"username":"Guest"}}""",
                setCookie: "csrftoken=csrf-123; Path=/; Secure")
            .On("POST", "/api/v0/login", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));

        OgsSession session = await auth.SignInWithPasswordAsync("kuro_test", "hunter2", CancellationToken.None);

        (HttpRequestMessage login, string? body) = http.Single("POST", "/api/v0/login");
        login.Headers.GetValues("X-CSRFToken").Should().Equal("csrf-123");
        login.Headers.Referrer.Should().Be(new Uri("https://beta.online-go.com/"));
        using JsonDocument json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("username").GetString().Should().Be("kuro_test");
        json.RootElement.GetProperty("password").GetString().Should().Be("hunter2");
        json.RootElement.TryGetProperty("ebi", out _).Should().BeTrue();
        session.User.Id.Should().Be(1001);
        store.Values.Should().BeEmpty("the password mode never persists credentials");
    }

    [Fact]
    public async Task Password_sign_in_is_refused_in_OAuth_mode()
    {
        (OgsAuthService auth, _, _, _, _) = Create(OAuthOptions);

        await FluentActions.Awaiting(() => auth.SignInWithPasswordAsync("u", "p", CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Wrong_password_is_reported_without_echoing_it()
    {
        (OgsAuthService auth, FakeHttpHandler http, _, _, CapturingLoggerFactory logs) = Create(PasswordOptions);
        http.On("GET", "/api/v1/ui/config", HttpStatusCode.OK, "{}", setCookie: "csrftoken=c; Path=/")
            .On("POST", "/api/v0/login", HttpStatusCode.Forbidden, """{"errors":"Invalid username or password"}""");

        OgsAuthException ex = (await FluentActions.Awaiting(() => auth.SignInWithPasswordAsync("kuro", "s3cret-pass", CancellationToken.None))
            .Should().ThrowAsync<OgsAuthException>()).Which;

        ex.Message.Should().NotContain("s3cret-pass");
        logs.Lines.Should().NotContain(l => l.Contains("s3cret-pass"));
    }

    [Fact]
    public async Task Secrets_never_reach_the_logs()
    {
        (OgsAuthService auth, FakeHttpHandler http, InMemoryTokenStore store, _, CapturingLoggerFactory logs) = Create(OAuthOptions);
        store.Values[OgsAuthService.RefreshTokenKey] = "refresh-token-0";
        http.On("POST", "/oauth2/token/", HttpStatusCode.OK, Fixtures.Read("token_response.json"))
            .On("GET", "/api/v1/ui/config", HttpStatusCode.OK, Fixtures.Read("ui_config.json"));

        await auth.RestoreAsync(CancellationToken.None);

        logs.Lines.Should().NotBeEmpty();
        logs.Lines.Should().NotContain(l =>
            l.Contains("access-token-1") || l.Contains("refresh-token") || l.Contains("test-jwt") || l.Contains("example.invalid"));
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
