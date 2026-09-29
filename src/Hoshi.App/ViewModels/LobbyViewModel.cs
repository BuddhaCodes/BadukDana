using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Ogs;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Realtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>A time-control choice in the "create challenge" form.</summary>
public sealed record TimePreset(string Label, TimeControlSettings Settings);

public sealed record ColorChoice(string Label, ChallengeColor Color);

public sealed record RulesChoice(string Label, RuleSet Rules);

public sealed record ServerChoice(string Label, OgsOptions Options);

/// <summary>One of the user's ongoing games.</summary>
public sealed record ActiveGameItem(OgsActiveGame Game, string Opponent, string Board, bool IsMyTurn)
{
    public string TurnText => Game.Phase switch
    {
        "play" => IsMyTurn ? "Tu turno" : "Turno rival",
        "stone removal" => "Conteo",
        _ => Game.Phase,
    };
}

/// <summary>An open challenge from the seek graph.</summary>
public sealed record OpenChallengeItem(OgsOpenChallenge Challenge, bool IsOwn)
{
    public string Player => $"{Challenge.Challenger.Username} [{Challenge.Challenger.Rank}]";

    public string Board => string.Create(CultureInfo.InvariantCulture, $"{Challenge.Width}×{Challenge.Height}");

    public string Details
    {
        get
        {
            var parts = new List<string> { Challenge.TimeControlSummary, Challenge.Rules?.Name ?? "?" };
            if (Challenge.Handicap != 0)
            {
                parts.Add(string.Create(CultureInfo.InvariantCulture, $"H{Challenge.Handicap}"));
            }

            if (Challenge.Ranked)
            {
                parts.Add("clasificatoria");
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Hoshi never plays ranked games (CLAUDE.md), and you cannot accept your own challenge.</summary>
    public bool CanAccept => !Challenge.Ranked && !IsOwn;
}

/// <summary>
/// The "Jugar en línea" window: sign-in, the user's active games, open challenges and challenge creation.
/// All OGS events are marshalled to the UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed partial class LobbyViewModel : ViewModelBase
{
    private readonly IOgsClient _ogs;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<LobbyViewModel> _logger;
    private CancellationTokenSource? _waitCts;
    private CreatedChallenge? _waiting;
    private bool _initialized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(IsSignedOut), nameof(UserText))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignInWithGoogleCommand), nameof(SignOutCommand), nameof(RefreshGamesCommand), nameof(CreateChallengeCommand), nameof(AcceptCommand))]
    private OgsSession? _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignInWithGoogleCommand), nameof(SignOutCommand), nameof(RefreshGamesCommand), nameof(CreateChallengeCommand), nameof(AcceptCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string _connectionText = "Sin conexión";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _opponentUsername = string.Empty;

    [ObservableProperty]
    private int _boardSize = 19;

    [ObservableProperty]
    private TimePreset _selectedTimePreset;

    [ObservableProperty]
    private ColorChoice _selectedColor;

    [ObservableProperty]
    private RulesChoice _selectedRules;

    [ObservableProperty]
    private int _handicap;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateChallengeCommand), nameof(CancelWaitingCommand), nameof(AcceptCommand))]
    private bool _isWaiting;

    [ObservableProperty]
    private string? _waitingText;

    public LobbyViewModel(IOgsClient ogs, IUiDispatcher ui, ILogger<LobbyViewModel>? logger = null)
    {
        _ogs = ogs ?? throw new ArgumentNullException(nameof(ogs));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger ?? NullLogger<LobbyViewModel>.Instance;
        _selectedTimePreset = TimePresets[1];
        _selectedColor = Colors[0];
        _selectedRules = RulesChoices[0];
        _session = ogs.Session;

        _ogs.SessionChanged += (_, _) => _ui.Post(() => Session = _ogs.Session);
        _ogs.ConnectionStateChanged += (_, _) => _ui.Post(UpdateConnectionText);
        _ogs.OpenChallengesChanged += (_, _) => _ui.Post(RefreshOpenChallenges);
        _ogs.ServerChanged += (_, _) => _ui.Post(OnServerChanged);
        Servers = [.. ogs.Servers.Select(o => new ServerChoice(
            o.IsProduction ? "online-go.com" : $"{o.BaseUrl.Host} (pruebas)", o))];
        UpdateConnectionText();
    }

    /// <summary>Raised when a game this user plays in has started (accepted or created challenge).</summary>
    public event EventHandler<long>? GameStarted;

    public ObservableCollection<ActiveGameItem> ActiveGames { get; } = [];

    public ObservableCollection<OpenChallengeItem> OpenChallenges { get; } = [];

    public IReadOnlyList<int> BoardSizes { get; } = [19, 13, 9];

    public IReadOnlyList<TimePreset> TimePresets { get; } =
    [
        new("Blitz · 30 s + 5×10 s", TimeControlSettings.ByoYomi(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), 5)),
        new("En vivo · 10 min + 5×30 s", TimeControlSettings.ByoYomi(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), 5)),
        new("En vivo · Fischer 5 min + 10 s", TimeControlSettings.Fischer(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(10))),
        new("Correspondencia · 3 días + 1 día", TimeControlSettings.Fischer(TimeSpan.FromDays(3), TimeSpan.FromDays(1), TimeSpan.FromDays(7))),
    ];

    public IReadOnlyList<ColorChoice> Colors { get; } =
    [
        new("Automático", ChallengeColor.Automatic),
        new("Negras", ChallengeColor.Black),
        new("Blancas", ChallengeColor.White),
        new("Aleatorio", ChallengeColor.Random),
    ];

    public IReadOnlyList<RulesChoice> RulesChoices { get; } =
    [
        new("Japonesas", RuleSet.Japanese),
        new("Chinas", RuleSet.Chinese),
        new("Coreanas", RuleSet.Korean),
        new("AGA", RuleSet.Aga),
    ];

    public IReadOnlyList<ServerChoice> Servers { get; }

    /// <summary>The server to sign in to; changing it is only possible while signed out.</summary>
    public ServerChoice? SelectedServer
    {
        get => Servers.FirstOrDefault(s => s.Options.BaseUrl == _ogs.Options.BaseUrl);
        set
        {
            if (value is null || value.Options.BaseUrl == _ogs.Options.BaseUrl || Session is not null)
            {
                return;
            }

            ErrorMessage = null;
            _ogs.SelectServer(value.Options);
            OnServerChanged();
        }
    }

    public string ServerName => _ogs.Options.BaseUrl.Host;

    public bool IsBeta => !_ogs.Options.IsProduction;

    public bool IsPasswordMode => _ogs.Options.AuthMode == OgsAuthMode.Password;

    public bool IsOAuthMode => !IsPasswordMode;

    public bool IsSignedIn => Session is not null;

    public bool IsSignedOut => Session is null;

    public string UserText => Session is { } s ? s.User.DisplayName : string.Empty;

    /// <summary>Called when the window first opens: restores a stored session and loads the lists.</summary>
    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (Session is not null)
        {
            await LoadAsync();
            return;
        }

        await RunAsync(async ct =>
        {
            if (await _ogs.RestoreAsync(ct))
            {
                Session = _ogs.Session;
            }
        });
        if (Session is not null)
        {
            await LoadAsync();
        }
    }

    private bool CanSignIn() => !IsBusy && Session is null
        && (!IsPasswordMode || (Username.Trim().Length > 0 && Password.Length > 0));

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        string password = Password;
        Password = string.Empty; // Never kept longer than the request.
        await RunAsync(async ct =>
        {
            if (IsPasswordMode)
            {
                await _ogs.SignInWithPasswordAsync(Username, password, ct);
            }
            else
            {
                StatusMessage = "Autoriza a Hoshi en la ventana del navegador…";
                await _ogs.SignInWithBrowserAsync(OgsLoginProvider.Ogs, ct);
            }

            Session = _ogs.Session;
            StatusMessage = null;
        });
        if (Session is not null)
        {
            await LoadAsync();
        }
    }

    private bool CanSignInWithGoogle() => !IsBusy && Session is null;

    /// <summary>Google sign-in handled by OGS in the browser; Hoshi only receives the OGS authorization.</summary>
    [RelayCommand(CanExecute = nameof(CanSignInWithGoogle))]
    private async Task SignInWithGoogleAsync()
    {
        await RunAsync(async ct =>
        {
            if (!IsOAuthMode)
            {
                // Beta cannot do OAuth: Google sign-in always goes to the server that supports it.
                ServerChoice oauth = Servers.FirstOrDefault(s => s.Options.AuthMode == OgsAuthMode.OAuth)
                    ?? throw new InvalidOperationException("No hay ningún servidor con inicio de sesión por navegador.");
                SelectedServer = oauth;
            }

            StatusMessage = "Entra con Google en el navegador y autoriza a Hoshi…";
            await _ogs.SignInWithBrowserAsync(OgsLoginProvider.Google, ct);
            Session = _ogs.Session;
            StatusMessage = null;
        });
        if (Session is not null)
        {
            await LoadAsync();
        }
    }

    private bool CanUseSession() => !IsBusy && Session is not null;

    [RelayCommand(CanExecute = nameof(CanUseSession))]
    private async Task SignOutAsync()
    {
        await CancelWaitingAsync();
        await RunAsync(async ct =>
        {
            await _ogs.SignOutAsync(ct);
            Session = null;
            ActiveGames.Clear();
            OpenChallenges.Clear();
            StatusMessage = "Sesión cerrada.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanUseSession))]
    private Task RefreshGamesAsync() => RunAsync(async ct =>
    {
        IReadOnlyList<OgsActiveGame> games = await _ogs.GetActiveGamesAsync(ct);
        long me = Session?.User.Id ?? 0;
        ActiveGames.Clear();
        foreach (OgsActiveGame g in games)
        {
            ActiveGames.Add(new ActiveGameItem(
                g,
                g.OpponentOf(me).DisplayName,
                string.Create(CultureInfo.InvariantCulture, $"{g.Width}×{g.Height}"),
                g.IsTurnOf(me)));
        }
    });

    private bool CanAccept(OpenChallengeItem? item) => CanUseSession() && !IsWaiting && item is { CanAccept: true };

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private Task AcceptAsync(OpenChallengeItem? item) => RunAsync(async ct =>
    {
        if (item is null || !item.CanAccept)
        {
            return;
        }

        long gameId = await _ogs.AcceptChallengeAsync(item.Challenge.ChallengeId, ct);
        StatusMessage = gameId > 0
            ? string.Create(CultureInfo.InvariantCulture, $"Desafío aceptado: partida #{gameId}.")
            : "Desafío aceptado.";
        if (gameId > 0)
        {
            GameStarted?.Invoke(this, gameId);
        }
    });

    private bool CanCreateChallenge() => CanUseSession() && !IsWaiting;

    [RelayCommand(CanExecute = nameof(CanCreateChallenge))]
    private async Task CreateChallengeAsync()
    {
        CreatedChallenge? created = null;
        await RunAsync(async ct =>
        {
            long? opponentId = null;
            string opponent = OpponentUsername.Trim();
            if (opponent.Length > 0)
            {
                OgsUser user = await _ogs.FindPlayerAsync(opponent, ct)
                    ?? throw new OgsApiException(System.Net.HttpStatusCode.NotFound, $"No existe el usuario «{opponent}» en {ServerName}.");
                if (user.Id == Session?.User.Id)
                {
                    throw new OgsApiException(System.Net.HttpStatusCode.BadRequest, "No puedes desafiarte a ti mismo.");
                }

                opponentId = user.Id;
            }

            created = await _ogs.CreateChallengeAsync(BuildRequest(opponentId is not null), opponentId, ct);
        });

        if (created is null)
        {
            return;
        }

        if (!created.IsLive)
        {
            StatusMessage = string.Create(CultureInfo.InvariantCulture, $"Desafío #{created.ChallengeId} creado. Ya es visible en {ServerName}.");
            return;
        }

        await WaitForOpponentAsync(created);
    }

    internal ChallengeRequest BuildRequest(bool direct) => new()
    {
        Name = "Hoshi",
        Width = BoardSize,
        Height = BoardSize,
        Rules = SelectedRules.Rules,
        Handicap = Math.Clamp(Handicap, 0, 9),
        Color = SelectedColor.Color,
        Private = direct,
        TimeControl = SelectedTimePreset.Settings,
    };

    private bool CanCancelWaiting() => IsWaiting;

    [RelayCommand(CanExecute = nameof(CanCancelWaiting))]
    private async Task CancelWaitingAsync()
    {
        if (_waiting is not { } challenge)
        {
            return;
        }

        _waiting = null;
        if (_waitCts is { } cts)
        {
            await cts.CancelAsync();
        }

        await RunAsync(ct => _ogs.CancelChallengeAsync(challenge.ChallengeId, ct));
        StatusMessage ??= "Desafío cancelado.";
    }

    private async Task WaitForOpponentAsync(CreatedChallenge created)
    {
        _waiting = created;
        using var cts = new CancellationTokenSource();
        _waitCts = cts;
        IsWaiting = true;
        WaitingText = string.Create(CultureInfo.InvariantCulture, $"Esperando rival para el desafío #{created.ChallengeId}…");
        StatusMessage = null;
        try
        {
            bool started = await _ogs.WaitForOpponentAsync(created, cts.Token);
            if (started)
            {
                _waiting = null;
                StatusMessage = string.Create(CultureInfo.InvariantCulture, $"¡Partida #{created.GameId} iniciada!");
                GameStarted?.Invoke(this, created.GameId);
            }
        }
        catch (Exception ex) when (ex is OgsRealtimeException or OperationCanceledException)
        {
            _logger.LogInformation("Stopped waiting for challenge {ChallengeId}: {Reason}", created.ChallengeId, ex.Message);
        }
        finally
        {
            _waitCts = null;
            IsWaiting = false;
            WaitingText = null;
        }
    }

    private async Task LoadAsync()
    {
        RefreshOpenChallenges();
        await RefreshGamesAsync();
    }

    private void RefreshOpenChallenges()
    {
        long me = Session?.User.Id ?? 0;
        OpenChallenges.Clear();
        foreach (OgsOpenChallenge c in _ogs.OpenChallenges)
        {
            OpenChallenges.Add(new OpenChallengeItem(c, c.Challenger.Id == me));
        }
    }

    private void OnServerChanged()
    {
        OnPropertyChanged(nameof(SelectedServer));
        OnPropertyChanged(nameof(ServerName));
        OnPropertyChanged(nameof(IsBeta));
        OnPropertyChanged(nameof(IsPasswordMode));
        OnPropertyChanged(nameof(IsOAuthMode));
        SignInCommand.NotifyCanExecuteChanged();
        UpdateConnectionText();
    }

    private void UpdateConnectionText() => ConnectionText = _ogs.ConnectionState switch
    {
        OgsConnectionState.Connected => "Conectado",
        OgsConnectionState.Connecting => "Conectando…",
        OgsConnectionState.Reconnecting => "Reconectando…",
        OgsConnectionState.Failed => "Conexión rechazada por el servidor",
        _ => "Sin conexión",
    };

    /// <summary>Runs an OGS call with the busy flag and turns failures into a user-facing message.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action(CancellationToken.None);
        }
        catch (OgsAuthException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (OgsApiException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("OGS request failed: {Error}", ex.Message);
            ErrorMessage = $"No se pudo conectar con {ServerName}.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = $"{ServerName} tardó demasiado en responder.";
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
