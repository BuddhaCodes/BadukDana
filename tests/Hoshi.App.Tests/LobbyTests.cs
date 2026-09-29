using System.Net;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Ogs;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Realtime;
using Microsoft.Extensions.Configuration;

namespace Hoshi.App.Tests;

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeOgsClient : IOgsClient
{
    public static readonly OgsUser Me = new(100, "hoshi-dev", 25, false);
    public static readonly OgsUser Rival = new(200, "rival", 27.5, false);

    private OgsSession? _session;
    private List<OgsOpenChallenge> _open = [];

    public static readonly OgsOptions Production = new() { AuthMode = OgsAuthMode.OAuth, ClientId = "id" };
    public static readonly OgsOptions Beta = new() { BaseUrl = new Uri("https://beta.online-go.com"), AuthMode = OgsAuthMode.Password };

    public IReadOnlyList<OgsOptions> Servers { get; } = [Production, Beta];

    public OgsOptions Options { get; set; } = Beta;

    public event EventHandler? ServerChanged;

    public void SelectServer(OgsOptions server)
    {
        Options = server;
        ServerChanged?.Invoke(this, EventArgs.Empty);
    }

    public OgsSession? Session => _session;

    public OgsConnectionState ConnectionState { get; set; }

    public IReadOnlyList<OgsOpenChallenge> OpenChallenges => _open;

    public event EventHandler? SessionChanged;

    public event EventHandler? ConnectionStateChanged;

    public event EventHandler? OpenChallengesChanged;

    public bool HasStoredSession { get; set; }

    public (string User, string Password)? PasswordSignIn { get; private set; }

    public List<OgsLoginProvider> BrowserSignIns { get; } = [];

    public Exception? SignInError { get; set; }

    public List<OgsActiveGame> Games { get; } = [];

    public List<(ChallengeRequest Request, long? Opponent)> Created { get; } = [];

    public List<long> Cancelled { get; } = [];

    public List<long> Accepted { get; } = [];

    public bool CreateLive { get; set; } = true;

    public TaskCompletionSource<bool> Opponent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void SetOpen(params OgsOpenChallenge[] challenges)
    {
        _open = [.. challenges];
        OpenChallengesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetState(OgsConnectionState state)
    {
        ConnectionState = state;
        ConnectionStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> RestoreAsync(CancellationToken cancellationToken)
    {
        if (HasStoredSession)
        {
            SignIn();
        }

        return Task.FromResult(HasStoredSession);
    }

    public Task SignInWithBrowserAsync(OgsLoginProvider provider, CancellationToken cancellationToken)
    {
        BrowserSignIns.Add(provider);
        if (SignInError is { } e)
        {
            throw e;
        }

        SignIn();
        return Task.CompletedTask;
    }

    public Task SignInWithPasswordAsync(string username, string password, CancellationToken cancellationToken)
    {
        PasswordSignIn = (username, password);
        if (SignInError is { } e)
        {
            throw e;
        }

        SignIn();
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken cancellationToken)
    {
        _session = null;
        SessionChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OgsActiveGame>> GetActiveGamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OgsActiveGame>>(Games);

    public Task<OgsUser?> FindPlayerAsync(string username, CancellationToken cancellationToken) =>
        Task.FromResult(username == Rival.Username ? Rival : username == Me.Username ? Me : null);

    public Task<CreatedChallenge> CreateChallengeAsync(ChallengeRequest request, long? opponentId, CancellationToken cancellationToken)
    {
        Created.Add((request, opponentId));
        return Task.FromResult(new CreatedChallenge(555, 777, CreateLive));
    }

    public async Task<bool> WaitForOpponentAsync(CreatedChallenge challenge, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration _ = cancellationToken.Register(() => Opponent.TrySetResult(false));
        return await Opponent.Task;
    }

    public Task CancelChallengeAsync(long challengeId, CancellationToken cancellationToken)
    {
        Cancelled.Add(challengeId);
        return Task.CompletedTask;
    }

    public Task<long> AcceptChallengeAsync(long challengeId, CancellationToken cancellationToken)
    {
        Accepted.Add(challengeId);
        return Task.FromResult(9001L);
    }

    public List<long> OpenedGames { get; } = [];

    public IOnlineGame OpenGame(long gameId)
    {
        OpenedGames.Add(gameId);
        return new FakeOnlineGame(gameId, Me.Id);
    }

    private void SignIn()
    {
        _session = new OgsSession(Me, "jwt");
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class LobbyViewModelTests
{
    private readonly FakeOgsClient _ogs = new();

    private LobbyViewModel Create() => new(_ogs, new ImmediateDispatcher());

    private static OgsOpenChallenge Challenge(long id, OgsUser by, bool ranked = false) =>
        new(id, id + 1000, by, "Friendly", 19, 19, ranked, 0, null, RuleSet.Japanese, "automatic", "live", "byoyomi 10:00 + 5×30 s", -1000, 1000);

    [Fact]
    public async Task Restores_a_stored_session_and_loads_games()
    {
        _ogs.HasStoredSession = true;
        _ogs.Games.Add(new OgsActiveGame(1, "Game", FakeOgsClient.Me, FakeOgsClient.Rival, 19, 19, FakeOgsClient.Me.Id, "play"));
        LobbyViewModel vm = Create();

        await vm.InitializeAsync();

        vm.IsSignedIn.Should().BeTrue();
        vm.UserText.Should().Be("hoshi-dev [5k]");
        vm.ActiveGames.Should().ContainSingle().Which.Should().Match<ActiveGameItem>(g => g.Opponent == "rival [3k]" && g.IsMyTurn && g.TurnText == "Tu turno");
    }

    [Fact]
    public async Task Password_sign_in_clears_the_password_immediately()
    {
        LobbyViewModel vm = Create();
        vm.SignInCommand.CanExecute(null).Should().BeFalse("username and password are required");

        vm.Username = "hoshi-dev";
        vm.Password = "s3cret";
        await vm.SignInCommand.ExecuteAsync(null);

        _ogs.PasswordSignIn.Should().Be(("hoshi-dev", "s3cret"));
        vm.Password.Should().BeEmpty();
        vm.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Failed_sign_in_shows_the_error_and_still_clears_the_password()
    {
        _ogs.SignInError = new OgsAuthException("Usuario o contraseña incorrectos.");
        LobbyViewModel vm = Create();
        vm.Username = "hoshi-dev";
        vm.Password = "wrong";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Usuario o contraseña incorrectos.");
        vm.Password.Should().BeEmpty();
        vm.IsSignedIn.Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task OAuth_mode_signs_in_through_the_browser_without_credentials()
    {
        _ogs.Options = FakeOgsClient.Production;
        LobbyViewModel vm = Create();

        vm.IsOAuthMode.Should().BeTrue();
        vm.IsBeta.Should().BeFalse();
        vm.SignInCommand.CanExecute(null).Should().BeTrue();
        await vm.SignInCommand.ExecuteAsync(null);

        _ogs.BrowserSignIns.Should().Equal(OgsLoginProvider.Ogs);
        vm.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Google_button_starts_the_browser_flow_at_OGS_google_login()
    {
        _ogs.Options = FakeOgsClient.Production;
        _ogs.Games.Add(new OgsActiveGame(1, "Game", FakeOgsClient.Me, FakeOgsClient.Rival, 19, 19, 200, "play"));
        LobbyViewModel vm = Create();

        vm.SignInWithGoogleCommand.CanExecute(null).Should().BeTrue();
        await vm.SignInWithGoogleCommand.ExecuteAsync(null);

        _ogs.BrowserSignIns.Should().Equal(OgsLoginProvider.Google);
        vm.IsSignedIn.Should().BeTrue();
        vm.ActiveGames.Should().ContainSingle();
        vm.SignInWithGoogleCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Google_from_beta_switches_to_online_go_com_first()
    {
        LobbyViewModel vm = Create();
        vm.IsPasswordMode.Should().BeTrue();
        vm.SignInWithGoogleCommand.CanExecute(null).Should().BeTrue("Google is offered whatever server is selected");

        await vm.SignInWithGoogleCommand.ExecuteAsync(null);

        _ogs.Options.Should().BeSameAs(FakeOgsClient.Production);
        _ogs.BrowserSignIns.Should().Equal(OgsLoginProvider.Google);
        vm.SelectedServer!.Label.Should().Be("online-go.com");
        vm.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public void The_server_can_be_chosen_while_signed_out()
    {
        LobbyViewModel vm = Create();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Servers.Select(s => s.Label).Should().Equal("online-go.com", "beta.online-go.com (pruebas)");
        vm.SelectedServer = vm.Servers[0];

        _ogs.Options.Should().BeSameAs(FakeOgsClient.Production);
        vm.IsOAuthMode.Should().BeTrue();
        vm.ServerName.Should().Be("online-go.com");
        changed.Should().Contain([nameof(LobbyViewModel.IsOAuthMode), nameof(LobbyViewModel.IsPasswordMode), nameof(LobbyViewModel.ServerName)]);
    }

    [Fact]
    public async Task The_server_cannot_change_while_signed_in()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();

        vm.SelectedServer = vm.Servers[0];

        _ogs.Options.Should().BeSameAs(FakeOgsClient.Beta);
    }

    [Fact]
    public async Task Network_errors_become_a_friendly_message()
    {
        _ogs.Options = FakeOgsClient.Production;
        _ogs.SignInError = new HttpRequestException("socket error");
        LobbyViewModel vm = Create();

        await vm.SignInCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("No se pudo conectar con online-go.com.");
    }

    [Fact]
    public async Task Open_challenges_follow_the_seek_graph_and_ranked_or_own_ones_cannot_be_accepted()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();

        _ogs.SetOpen(Challenge(1, FakeOgsClient.Rival), Challenge(2, FakeOgsClient.Rival, ranked: true), Challenge(3, FakeOgsClient.Me));

        vm.OpenChallenges.Should().HaveCount(3);
        vm.OpenChallenges.Select(c => c.CanAccept).Should().Equal(true, false, false);
        vm.OpenChallenges[1].Details.Should().Contain("clasificatoria");
        vm.AcceptCommand.CanExecute(vm.OpenChallenges[0]).Should().BeTrue();
        vm.AcceptCommand.CanExecute(vm.OpenChallenges[1]).Should().BeFalse();
        vm.AcceptCommand.CanExecute(vm.OpenChallenges[2]).Should().BeFalse();
    }

    [Fact]
    public async Task Accepting_a_challenge_reports_the_new_game()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();
        _ogs.SetOpen(Challenge(1, FakeOgsClient.Rival));
        long? started = null;
        vm.GameStarted += (_, id) => started = id;

        await vm.AcceptCommand.ExecuteAsync(vm.OpenChallenges[0]);

        _ogs.Accepted.Should().Equal(1);
        started.Should().Be(9001);
        vm.StatusMessage.Should().Contain("#9001");
    }

    [Fact]
    public async Task Creating_an_open_live_challenge_waits_until_someone_joins()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();
        vm.BoardSize = 9;
        long? started = null;
        vm.GameStarted += (_, id) => started = id;

        Task create = vm.CreateChallengeCommand.ExecuteAsync(null);
        vm.IsWaiting.Should().BeTrue();
        vm.WaitingText.Should().Contain("#555");
        vm.CreateChallengeCommand.CanExecute(null).Should().BeFalse();

        _ogs.Opponent.SetResult(true);
        await create;

        vm.IsWaiting.Should().BeFalse();
        started.Should().Be(777);
        (ChallengeRequest request, long? opponent) = _ogs.Created.Should().ContainSingle().Subject;
        opponent.Should().BeNull();
        request.Width.Should().Be(9);
        request.Private.Should().BeFalse();
        request.ToJson()["game"]!["ranked"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_a_waiting_challenge_deletes_it_on_the_server()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();

        Task create = vm.CreateChallengeCommand.ExecuteAsync(null);
        await vm.CancelWaitingCommand.ExecuteAsync(null);
        await create;

        _ogs.Cancelled.Should().Equal(555);
        vm.IsWaiting.Should().BeFalse();
        vm.StatusMessage.Should().Be("Desafío cancelado.");
    }

    [Fact]
    public async Task A_direct_challenge_looks_up_the_opponent_and_is_private()
    {
        _ogs.HasStoredSession = true;
        _ogs.CreateLive = false;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();
        vm.OpponentUsername = " rival ";
        vm.SelectedTimePreset = vm.TimePresets[^1];

        await vm.CreateChallengeCommand.ExecuteAsync(null);

        _ogs.Created.Should().ContainSingle().Which.Should().Match<(ChallengeRequest R, long? O)>(c => c.O == 200 && c.R.Private);
        vm.IsWaiting.Should().BeFalse("correspondence challenges need no keepalive");
        vm.StatusMessage.Should().Contain("#555");
    }

    [Fact]
    public async Task Unknown_or_self_opponents_are_rejected_before_creating_anything()
    {
        _ogs.HasStoredSession = true;
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();

        vm.OpponentUsername = "nobody";
        await vm.CreateChallengeCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Contain("nobody");

        vm.OpponentUsername = "hoshi-dev";
        await vm.CreateChallengeCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().Be("No puedes desafiarte a ti mismo.");

        _ogs.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Signing_out_clears_the_lists()
    {
        _ogs.HasStoredSession = true;
        _ogs.Games.Add(new OgsActiveGame(1, "Game", FakeOgsClient.Me, FakeOgsClient.Rival, 19, 19, 200, "play"));
        LobbyViewModel vm = Create();
        await vm.InitializeAsync();

        await vm.SignOutCommand.ExecuteAsync(null);

        vm.IsSignedIn.Should().BeFalse();
        vm.ActiveGames.Should().BeEmpty();
        vm.OpenChallenges.Should().BeEmpty();
    }

    [Fact]
    public void Connection_state_is_shown_in_spanish()
    {
        LobbyViewModel vm = Create();
        vm.ConnectionText.Should().Be("Sin conexión");

        _ogs.SetState(OgsConnectionState.Reconnecting);
        vm.ConnectionText.Should().Be("Reconectando…");

        _ogs.SetState(OgsConnectionState.Connected);
        vm.ConnectionText.Should().Be("Conectado");
    }
}

public sealed class LobbyWindowTests
{
    [AvaloniaFact]
    public void Beta_password_mode_shows_the_credentials_form()
    {
        var window = new LobbyWindow { DataContext = new LobbyViewModel(new FakeOgsClient(), new ImmediateDispatcher()) };
        window.Show();

        window.FindControl<TextBox>("UsernameBox")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<TextBox>("PasswordBox")!.PasswordChar.Should().Be('•');
        window.FindControl<Button>("BrowserSignInButton")!.IsEffectivelyVisible.Should().BeFalse();
        window.FindControl<Button>("GoogleSignInButton")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<ComboBox>("ServerBox")!.IsEffectivelyVisible.Should().BeTrue();
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "phase4-lobby-beta.png"));
        window.FindControl<TabControl>("Tabs")!.IsVisible.Should().BeFalse();
        window.FindControl<TextBlock>("SocialAccountHint")!.IsEffectivelyVisible.Should().BeTrue();
    }

    [AvaloniaFact]
    public void OAuth_mode_offers_Google_and_OGS_sign_in_and_is_saved_as_screenshot()
    {
        var ogs = new FakeOgsClient { Options = FakeOgsClient.Production };
        var window = new LobbyWindow { DataContext = new LobbyViewModel(ogs, new ImmediateDispatcher()) };
        window.Show();

        window.FindControl<Button>("GoogleSignInButton")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<Button>("BrowserSignInButton")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<TextBox>("UsernameBox")!.IsEffectivelyVisible.Should().BeFalse();

        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        string outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, "phase4-lobby-signin.png"));
    }

    [AvaloniaFact]
    public async Task Signed_in_view_lists_challenges_and_is_saved_as_screenshot()
    {
        var ogs = new FakeOgsClient { HasStoredSession = true };
        var vm = new LobbyViewModel(ogs, new ImmediateDispatcher());
        var window = new LobbyWindow { DataContext = vm };
        window.Show();
        await vm.InitializeAsync();
        ogs.SetOpen(new OgsOpenChallenge(1, 2, FakeOgsClient.Rival, "x", 19, 19, false, 0, null, RuleSet.Japanese, "automatic", "live", "byoyomi 10:00 + 5×30 s", -1000, 1000));
        ogs.SetState(OgsConnectionState.Connected);

        window.FindControl<TabControl>("Tabs")!.IsVisible.Should().BeTrue();
        window.FindControl<TextBlock>("UserLabel")!.Text.Should().Be("hoshi-dev [5k]");
        window.FindControl<TabControl>("Tabs")!.SelectedIndex = 1;

        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        string outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, "phase4-lobby.png"));
    }

    [AvaloniaFact]
    public void Main_window_opens_the_lobby_with_ctrl_L()
    {
        var lobby = new CountingLobby();
        var window = new MainWindow(new MainWindowViewModel(new GameViewModel(), lobby));
        window.Show();

        window.KeyPressQwerty(Avalonia.Input.PhysicalKey.L, Avalonia.Input.RawInputModifiers.Control);

        lobby.Shown.Should().Be(1);
    }

    private sealed class CountingLobby : ILobbyWindowService
    {
        public int Shown { get; private set; }

        public void Show() => Shown++;
    }
}

public sealed class OgsConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => KeyValuePair.Create(v.Key, (string?)v.Value))).Build();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Shipped_settings_start_on_online_go_com_in_every_environment(bool development)
    {
        string dir = AppContext.BaseDirectory;
        var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(dir, "appsettings.json"));
        if (development)
        {
            builder.AddJsonFile(Path.Combine(dir, "appsettings.Development.json"));
        }

        OgsServerCatalog catalog = OgsServerCatalog.FromConfiguration(builder.Build());

        catalog.Initial.Should().BeSameAs(catalog.Production);
        catalog.Production.AuthMode.Should().Be(OgsAuthMode.OAuth);
        catalog.Production.ClientId.Should().NotBeNullOrEmpty();
        catalog.Production.RedirectUri.Should().Be(new Uri("http://127.0.0.1:8734/callback"));
        catalog.Beta.BaseUrl.Host.Should().Be("beta.online-go.com");
        catalog.Beta.AuthMode.Should().Be(OgsAuthMode.Password);
        catalog.Beta.EffectiveWebSocketUrl.Should().Be(new Uri("wss://beta.online-go.com/"));
    }

    [Fact]
    public void Beta_can_be_the_initial_server()
    {
        OgsServerCatalog catalog = OgsServerCatalog.FromConfiguration(Config(("Ogs:DefaultServer", "beta")));

        catalog.Initial.Should().BeSameAs(catalog.Beta);
    }

    [Fact]
    public void Production_never_uses_password_login_whatever_the_configuration()
    {
        OgsServerCatalog catalog = OgsServerCatalog.FromConfiguration(Config(("Ogs:AuthMode", "Password"), ("Ogs:BaseUrl", "https://online-go.com")));

        catalog.All.Where(s => s.IsProduction).Should().OnlyContain(s => s.AuthMode == OgsAuthMode.OAuth);
    }

    [Fact]
    public void No_secret_is_shipped_in_settings()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));

        text.Should().NotContainEquivalentOf("secret");
        text.Should().NotContainEquivalentOf("password\":");
    }

    [Fact]
    public void Host_resolves_the_online_services()
    {
        using Microsoft.Extensions.Hosting.IHost host = AppHost.Create([]);

        IOgsClient ogs = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IOgsClient>(host.Services);
        ogs.Servers.Should().HaveCount(2);
        ogs.Options.IsProduction.Should().BeTrue();
        ogs.SelectServer(ogs.Servers[1]);
        ogs.Options.BaseUrl.Host.Should().Be("beta.online-go.com");
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MainWindowViewModel>(host.Services)
            .IsOnlineAvailable.Should().BeTrue();
    }
}

public sealed class SecureTokenStoreTests
{
    private sealed class RecordingRunner(params ProcessResult[] results) : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results = new(results);

        public List<(string File, string[] Args, string? Stdin)> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
        {
            Calls.Add((fileName, [.. arguments], standardInput));
            return Task.FromResult(_results.Count > 0 ? _results.Dequeue() : new ProcessResult(0, string.Empty));
        }
    }

    [Fact]
    public async Task Secret_tool_receives_the_secret_on_stdin_only()
    {
        var runner = new RecordingRunner();
        var store = new SecretToolTokenStore(runner);

        await store.WriteAsync("ogs.refresh_token", "tok-123", CancellationToken.None);

        (string file, string[] args, string? stdin) = runner.Calls.Should().ContainSingle().Subject;
        file.Should().Be("secret-tool");
        args.Should().StartWith("store").And.NotContain(a => a.Contains("tok-123", StringComparison.Ordinal));
        args.Should().ContainInConsecutiveOrder("key", "ogs.refresh_token");
        stdin.Should().Be("tok-123");
    }

    [Fact]
    public async Task Secret_tool_lookup_returns_null_when_missing()
    {
        var store = new SecretToolTokenStore(new RecordingRunner(new ProcessResult(1, string.Empty), new ProcessResult(0, "tok\n")));

        (await store.ReadAsync("k", CancellationToken.None)).Should().BeNull();
        (await store.ReadAsync("k", CancellationToken.None)).Should().Be("tok");
    }

    [Fact]
    public async Task Keychain_writes_through_interactive_stdin_so_the_token_is_not_in_argv()
    {
        var runner = new RecordingRunner();
        var store = new KeychainTokenStore(runner);

        await store.WriteAsync("ogs.refresh_token", "a\"b", CancellationToken.None);

        (_, string[] args, string? stdin) = runner.Calls.Should().ContainSingle().Subject;
        args.Should().Equal("-i");
        stdin.Should().Be("add-generic-password -U -s \"Hoshi\" -a \"ogs.refresh_token\" -w \"a\\\"b\"\n");
    }

    [Fact]
    public async Task Keychain_not_found_is_null()
    {
        var store = new KeychainTokenStore(new RecordingRunner(new ProcessResult(44, string.Empty)));

        (await store.ReadAsync("k", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Keychain_rejects_values_that_could_inject_commands()
    {
        Action quote = () => KeychainTokenStore.Quote("x\ndelete-keychain");

        quote.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Falls_back_to_memory_when_the_os_store_is_unavailable()
    {
        var logs = new List<string>();
        var store = new FallbackTokenStore(new BrokenStore(), new ListLogger(logs));

        await store.WriteAsync("k", "v", CancellationToken.None);

        (await store.ReadAsync("k", CancellationToken.None)).Should().Be("v");
        store.IsDegraded.Should().BeTrue();
        logs.Should().ContainSingle().Which.Should().NotContain("v\"");
        await store.DeleteAsync("k", CancellationToken.None);
        (await store.ReadAsync("k", CancellationToken.None)).Should().BeNull();
    }

    private sealed class BrokenStore : ITokenStore
    {
        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken) => throw new SecureStoreUnavailableException("secret-tool is not installed");

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken) => throw new SecureStoreUnavailableException("secret-tool is not installed");

        public Task DeleteAsync(string key, CancellationToken cancellationToken) => throw new SecureStoreUnavailableException("secret-tool is not installed");
    }

    private sealed class ListLogger(List<string> lines) : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Add(formatter(state, exception));
    }
}
