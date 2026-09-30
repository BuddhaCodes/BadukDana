using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILobbyWindowService? _lobbyWindow;
    private readonly IOgsClient? _ogs;
    private readonly IUiDispatcher? _ui;
    private readonly IDialogService? _dialogs;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly IPreferencesWindowService? _preferences;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(IsLocal))]
    private OnlineGameViewModel? _online;

    public MainWindowViewModel()
        : this(new GameViewModel())
    {
    }

    public MainWindowViewModel(
        GameViewModel game,
        ILobbyWindowService? lobbyWindow = null,
        LobbyViewModel? lobby = null,
        IOgsClient? ogs = null,
        IUiDispatcher? ui = null,
        IDialogService? dialogs = null,
        ILogger<MainWindowViewModel>? logger = null,
        IPreferencesWindowService? preferences = null,
        IAnalysisEngine? engine = null,
        ISoundService? sounds = null,
        ISettingsService? settings = null,
        Services.Music.IMusicService? music = null)
    {
        _preferences = preferences;
        Analysis = new AnalysisViewModel(game, engine, ui, sounds, settings);
        _music = music;
        _settings = settings;
        if (music is not null)
        {
            Analysis.MoveJudged += (_, verdict) => music.OnVerdict(verdict.Quality);
            AppSettings s = settings?.Current ?? new AppSettings();
            music.SetVolume(s.MusicVolume / 100.0);
            if (s.Music)
            {
                music.Start();
            }
        }
        Game = game;
        _lobbyWindow = lobbyWindow;
        _ogs = ogs;
        _ui = ui;
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;
        if (lobby is not null)
        {
            lobby.GameStarted += async (_, id) => await OpenOnlineGameAsync(id);
        }
    }

    public GameViewModel Game { get; }

    /// <summary>Territory estimate and the background engine review.</summary>
    public AnalysisViewModel Analysis { get; }

    /// <summary>False in design/test contexts without OGS services.</summary>
    public bool IsOnlineAvailable => _lobbyWindow is not null;

    public bool IsOnline => Online is not null;

    public bool IsLocal => Online is null;

    /// <summary>Shows an OGS game on the board (from the lobby: a started or an active game).</summary>
    public async Task OpenOnlineGameAsync(long gameId)
    {
        if (_ogs is null || _ui is null)
        {
            return;
        }

        if (Online?.GameId == gameId)
        {
            return;
        }

        if (Online is null && Game.IsDirty && _dialogs is not null
            && !await _dialogs.ConfirmAsync("Cambios sin guardar", "La partida local tiene cambios sin guardar. ¿Descartarlos y abrir la partida en línea?"))
        {
            return;
        }

        LeaveOnline();
        try
        {
            IOnlineGame game = _ogs.OpenGame(gameId);
            var online = new OnlineGameViewModel(game, Game, _ui, _dialogs, TimeProvider.System);
            online.Left += (_, _) => { if (ReferenceEquals(Online, online)) { Online = null; } };
            Online = online;
            online.Connect();
            _logger.LogInformation("Opened online game {GameId}", gameId);
        }
        catch (InvalidOperationException ex)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync("No se pudo abrir la partida", ex.Message);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsOnlineAvailable))]
    private void OpenLobby() => _lobbyWindow?.Show();

    public bool HasPreferences => _preferences is not null;

    private readonly Services.Music.IMusicService? _music;
    private readonly ISettingsService? _settings;

    /// <summary>M: music on/off (saved).</summary>
    [RelayCommand]
    private void ToggleMusic()
    {
        if (_music is null)
        {
            return;
        }

        bool on = !_music.IsPlaying;
        if (on)
        {
            _music.Start();
        }
        else
        {
            _music.Stop();
        }

        if (_settings is not null)
        {
            _settings.Save(_settings.Current with { Music = on });
        }
    }

    [RelayCommand(CanExecute = nameof(HasPreferences))]
    private Task OpenPreferences() => _preferences?.ShowAsync() ?? Task.CompletedTask;

    private void LeaveOnline() => Online?.LeaveCommand.Execute(null);
}
