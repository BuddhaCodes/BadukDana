using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core.Localization;
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
        Services.Music.IMusicService? music = null,
        IReplayStore? replays = null,
        ReplaysViewModel? replaysList = null,
        IReplaysWindowService? replaysWindow = null,
        JosekiTrainerViewModel? joseki = null,
        JosekiAssistantViewModel? josekiHints = null,
        UpdateViewModel? updates = null,
        KataGoSetupViewModel? kataGoSetup = null,
        AudioViewModel? audio = null,
        EngineMatchViewModel? engineMatch = null,
        Services.Engines.IGtpEngineHost? engines = null,
        Services.Engines.IEngineWindows? engineWindows = null,
        EffectsViewModel? effects = null)
    {
        Effects = effects ?? new EffectsViewModel(settings);
        EngineMatch = engineMatch;
        _engines = engines;
        _engineWindows = engineWindows;
        Updates = updates;
        Audio = audio;
        KataGoSetup = kataGoSetup;
        Joseki = joseki ?? new JosekiTrainerViewModel(Services.Joseki.NullJosekiLibrary.Instance, game);
        JosekiHints = josekiHints ?? new JosekiAssistantViewModel(game);
        _preferences = preferences;
        _replaysWindow = replaysWindow;
        _replayStore = replays;
        if (replays is not null)
        {
            _recorder = new ReplayRecorder(game, replays);
        }

        if (replaysList is not null)
        {
            replaysList.OpenRequested += (_, entry) => OpenReplay(entry);
        }

        Analysis = new AnalysisViewModel(game, engine, ui, sounds, settings);
        Effects.Changed += (_, _) =>
        {
            Analysis.Refresh();
            game.ShowStatus(Tr.F("Effects.Status", Effects.LevelName));
        };
        _music = music;
        _settings = settings;
        _sounds = sounds;
        if (sounds is not null)
        {
            game.StonePlaced += (_, _) =>
            {
                AppSettings current = settings?.Current ?? new AppSettings();
                if (current.StoneSounds)
                {
                    sounds.Play(SoundEffect.Stone, current.SoundVolume / 100.0 * 0.8);
                }
            };
            game.GroupsEnteredAtari += (_, _) =>
            {
                AppSettings current = settings?.Current ?? new AppSettings();
                if (Analysis.IsAtariAlertActive && current.StoneSounds)
                {
                    sounds.Play(SoundEffect.Atari, current.SoundVolume / 100.0 * 0.7);
                }
            };
            game.StonesCaptured += (_, count) =>
            {
                AppSettings current = settings?.Current ?? new AppSettings();
                if (current.StoneSounds)
                {
                    sounds.Play(count >= 3 ? SoundEffect.CaptureBig : SoundEffect.CaptureSmall, current.SoundVolume / 100.0 * 0.85);
                }
            };
        }
        if (music is not null)
        {
            Analysis.MoveJudged += (_, verdict) => music.OnVerdict(verdict.Quality);
            Analysis.BattleHeat += (_, heat) => music.OnBattle(heat);
            AppSettings s = settings?.Current ?? new AppSettings();
            music.SetVolume(s.MusicVolume / 100.0);
            if (s.Music && !s.Muted)
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
            && !await _dialogs.ConfirmAsync(Tr.T("Dialog.UnsavedChanges"), Tr.T("Main.DiscardForOnline")))
        {
            return;
        }

        LeaveOnline();
        try
        {
            IOnlineGame game = _ogs.OpenGame(gameId);
            var online = new OnlineGameViewModel(game, Game, _ui, _dialogs, TimeProvider.System, _sounds, _settings);
            online.Left += (_, _) => { if (ReferenceEquals(Online, online)) { Online = null; } };
            Online = online;
            online.Connect();
            _logger.LogInformation("Opened online game {GameId}", gameId);
        }
        catch (InvalidOperationException ex)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Main.CouldNotOpenGame"), ex.Message);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsOnlineAvailable))]
    private void OpenLobby() => _lobbyWindow?.Show();

    public bool HasPreferences => _preferences is not null;

    /// <summary>The "Install KataGo" card (null without a host).</summary>
    public KataGoSetupViewModel? KataGoSetup { get; }

    /// <summary>The update banner and "Check for updates…" (null without a host, e.g. the designer).</summary>
    public UpdateViewModel? Updates { get; }

    /// <summary>Board effects strength (bottom bar, sound panel, Preferences, F).</summary>
    public EffectsViewModel Effects { get; }

    /// <summary>The speaker button: mute everything, volumes and music.</summary>
    public AudioViewModel? Audio { get; }

    /// <summary>The joseki trainer, shown on the main board (Ctrl+J).</summary>
    public JosekiTrainerViewModel Joseki { get; }

    /// <summary>Known joseki continuations on the main board while you play (J).</summary>
    public JosekiAssistantViewModel JosekiHints { get; }

    [RelayCommand]
    private void OpenJoseki() => Joseki.ToggleCommand.Execute(null);

    private readonly IReplaysWindowService? _replaysWindow;
    private readonly IReplayStore? _replayStore;
    private readonly ReplayRecorder? _recorder;

    public bool HasReplays => _replaysWindow is not null;

    /// <summary>Ctrl+R: the library of played games.</summary>
    [RelayCommand(CanExecute = nameof(HasReplays))]
    private void OpenReplays() => _replaysWindow?.Show();

    /// <summary>Shows a played game from the start, in review mode: stepping forward replays the AI effects.</summary>
    public void OpenReplay(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_replayStore is null)
        {
            return;
        }

        try
        {
            Hoshi.Sgf.GameTree tree = _replayStore.Load(entry);
            LeaveOnline();
            Game.Load(tree, path: null);
            Game.IsReview = true;
            _recorder?.MarkReplay(entry);
        }
        catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not open replay {Id}", entry.Id);
            _ = _dialogs?.ShowErrorAsync(Tr.T("Replays.CouldNotOpen"), ex.Message);
        }
    }

    /// <summary>Keeps the game on the board in the replay library (called when the app closes).</summary>
    public void SaveCurrentGame() => _recorder?.SaveCurrent();

    private readonly Services.Music.IMusicService? _music;
    private readonly ISettingsService? _settings;
    private readonly ISoundService? _sounds;

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

    private readonly Services.Engines.IGtpEngineHost? _engines;
    private readonly Services.Engines.IEngineWindows? _engineWindows;

    /// <summary>The game against an engine (or between two) on the board; null without a host.</summary>
    public EngineMatchViewModel? EngineMatch { get; }

    public bool HasEngines => _engines is not null && _engineWindows is not null && EngineMatch is not null;

    /// <summary>Ctrl+G: a new game against an engine, or between two engines.</summary>
    [RelayCommand(CanExecute = nameof(HasEngines))]
    private async Task PlayEngine()
    {
        if (_engines is null || _engineWindows is null || EngineMatch is null)
        {
            return;
        }

        var options = new NewEngineGameViewModel(_engines.Engines, Game.Board.Rules);
        if (!await _engineWindows.AskNewGameAsync(options))
        {
            return;
        }

        await StartEngineGameAsync(options);
    }

    /// <summary>Loads the new game and lets the chosen engines play (after the dialog, or from tests).</summary>
    public async Task StartEngineGameAsync(NewEngineGameViewModel options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (EngineMatch is null || !options.CanStart)
        {
            return;
        }

        if (Online is null && Game.IsDirty && _dialogs is not null
            && !await _dialogs.ConfirmAsync(Tr.T("Dialog.UnsavedChanges"), Tr.T("Game.DiscardChanges")))
        {
            return;
        }

        LeaveOnline();
        Game.Load(options.CreateTree(), path: null);
        EngineMatch.Start(options.Black.Engine, options.White.Engine);
    }

    [RelayCommand(CanExecute = nameof(HasEngines))]
    private void OpenGtpConsole() => _engineWindows?.ShowConsole();
}
