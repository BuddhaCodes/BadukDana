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

    /// <summary>Coordinates around the main board (Preferences → Appearance; hidden on phones by default).</summary>
    [ObservableProperty]
    private bool _showCoordinates = true;

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
        IPreferencesWindowService? preferencesWindow = null,
        IAnalysisEngine? engine = null,
        ISoundService? sounds = null,
        ISettingsService? settings = null,
        Services.Music.IMusicService? music = null,
        IReplayStore? replays = null,
        ReplaysViewModel? replaysList = null,
        IReplaysWindowService? replaysWindow = null,
        ITsumegoWindowService? tsumegoWindow = null,
        JosekiTrainerViewModel? joseki = null,
        JosekiAssistantViewModel? josekiHints = null,
        UpdateViewModel? updates = null,
        KataGoSetupViewModel? kataGoSetup = null,
        AudioViewModel? audio = null,
        EngineMatchViewModel? engineMatch = null,
        Services.Engines.IGtpEngineHost? engines = null,
        Services.Engines.IEngineWindows? engineWindows = null,
        EffectsViewModel? effects = null,
        IFileDialogService? files = null,
        IBrowserLauncher? browser = null,
        PreferencesViewModel? preferences = null,
        ZenMode? zen = null)
    {
        _zen = zen;
        AppSettings initial = settings?.Current ?? new AppSettings();
        game.ConfirmMoves = PlatformInfo.ConfirmMoves(initial);
        game.MoveNumberMode = initial.MoveNumbers;
        _showCoordinates = PlatformInfo.ShowCoordinates(initial);
        if (preferences is not null)
        {
            preferences.ConfirmMovesChanged += (_, on) => game.ConfirmMoves = on;
            preferences.BoardDisplayChanged += (_, _) =>
            {
                ShowCoordinates = preferences.ShowCoordinates;
                game.MoveNumberMode = preferences.MoveNumbers;
            };
        }

        Effects = effects ?? new EffectsViewModel(settings);
        EngineMatch = engineMatch;
        _engines = engines;
        _engineWindows = engineWindows;
        Updates = updates;
        Audio = audio;
        KataGoSetup = kataGoSetup;
        Joseki = joseki ?? new JosekiTrainerViewModel(Services.Joseki.NullJosekiLibrary.Instance, game);
        JosekiHints = josekiHints ?? new JosekiAssistantViewModel(game);
        _preferences = preferencesWindow;
        _replaysWindow = replaysWindow;
        _tsumegoWindow = tsumegoWindow;
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
        Study = new StudyViewModel(game, Analysis, settings, files, dialogs, browser);
        Counting = new CountingViewModel(game, Analysis);
        Clock = new LocalClockViewModel(game, ui, sounds: sounds);
        Counting.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CountingViewModel.IsActive) or nameof(CountingViewModel.Territory))
            {
                OnPropertyChanged(nameof(BoardTerritory));
            }
        };
        Analysis.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AnalysisViewModel.Territory))
            {
                OnPropertyChanged(nameof(BoardTerritory));
            }
        };
        Effects.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EffectsViewModel.Level))
            {
                OnPropertyChanged(nameof(BoardEffects));
            }
        };
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
            game.MoveSettled += (_, move) =>
            {
                AppSettings current = settings?.Current ?? new AppSettings();
                if (current.StoneSounds)
                {
                    (SoundEffect sound, double loudness) = StoneSound(move);
                    sounds.Play(sound, current.SoundVolume / 100.0 * loudness);
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
            Analysis.MoveWeighed += (_, weight) => music.OnVerdict(weight.Quality, weight.Importance, weight.Opening);
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
        if (ogs is not null)
        {
            ogs.GameTurnChanged += (_, turn) => (ui ?? new AvaloniaUiDispatcher()).Post(() => OnGameTurn(turn));
        }
        _ui = ui;
        _dialogs = dialogs;
        _logger = logger ?? NullLogger<MainWindowViewModel>.Instance;
        if (lobby is not null)
        {
            lobby.GameStarted += async (_, id) => await OpenOnlineGameAsync(id);
            lobby.SgfOpened += async (_, e) => await game.OpenSgfTextAsync(e.Sgf, e.Description);
        }
    }

    public GameViewModel Game { get; }

    /// <summary>Territory estimate and the background engine review.</summary>
    public AnalysisViewModel Analysis { get; }

    /// <summary>The study panel: pins, drawings, the timeline, sharing, the quiz and the report (S).</summary>
    public StudyViewModel Study { get; }

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

    /// <summary>
    /// The stone's own sound: a soft thud for quiet moves (openings, extensions, connections) and a firmer clack for
    /// moves played with intent — touching an enemy stone, or leaving a group nearby short of liberties.
    /// </summary>
    public static (SoundEffect Sound, double Loudness) StoneSound(PlacedMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        int captured = move.Before.AllPoints.Count(p => move.Before[p] != Hoshi.Core.Stone.Empty && move.After[p] == Hoshi.Core.Stone.Empty);
        double intensity = Hoshi.Core.FightMeter.Intensity(move.Before, move.After, move.Point, captured);
        return intensity >= 0.6 ? (SoundEffect.StoneFirm, 0.9) : (SoundEffect.Stone, 0.7);
    }

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
    private readonly ITsumegoWindowService? _tsumegoWindow;

    public bool HasTsumego => _tsumegoWindow is not null;

    [RelayCommand]
    private void OpenTsumego() => _tsumegoWindow?.Show();
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

    // ---------- Your turn in another OGS game ----------

    private readonly Dictionary<long, long> _toMove = [];

    /// <summary>"Your move against rival" for a game other than the one on the board; null when none.</summary>
    [ObservableProperty]
    private TurnNotice? _turnNotice;

    private void OnGameTurn(Hoshi.Ogs.Realtime.OgsGameTurn turn)
    {
        bool was = _toMove.TryGetValue(turn.GameId, out long before);
        _toMove[turn.GameId] = turn.PlayerToMove;
        long? me = _ogs?.Session?.User.Id;
        if (me is null || turn.Phase != "play" || turn.PlayerToMove != me || (was && before == me) || Online?.GameId == turn.GameId)
        {
            return; // not newly your turn, or the game is already on the board
        }

        string opponent = turn.Black == _ogs!.Session!.User.Username ? turn.White : turn.Black;
        TurnNotice = new TurnNotice(turn.GameId, Tr.F("Turn.Notice", opponent));
        AppSettings s = _settings?.Current ?? new AppSettings();
        if (s.ChatSounds)
        {
            _sounds?.Play(SoundEffect.Chat, s.SoundVolume / 100.0);
        }

        PlatformInfo.Notify?.Invoke(Tr.T("Turn.Title"), TurnNotice.Text, turn.GameId);
    }

    [RelayCommand]
    private async Task OpenTurnGame()
    {
        if (TurnNotice is { } notice)
        {
            TurnNotice = null;
            await OpenOnlineGameAsync(notice.GameId);
        }
    }

    [RelayCommand]
    private void DismissTurnNotice() => TurnNotice = null;

    /// <summary>The clock of a local game (menu → Clock).</summary>
    public LocalClockViewModel Clock { get; }

    /// <summary>Counting at the end of a local game.</summary>
    public CountingViewModel Counting { get; }

    /// <summary>The territory the board shows: the count while counting, otherwise the estimate (T).</summary>
    public Hoshi.Core.TerritoryEstimate? BoardTerritory => Counting.IsActive ? Counting.Territory : Analysis.Territory;

    // ---------- Zen mode ----------

    private readonly ZenMode? _zen;
    private bool _musicBeforeZen;

    /// <summary>Zen mode: the board alone, a still background, quieter effects (Z, or the menu).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardEffects), nameof(IsBackgroundStill))]
    private bool _isZen;

    /// <summary>Zen mode also silences effects, music and sounds (Preferences).</summary>
    public bool ZenSilence => _settings?.Current.ZenSilence ?? false;

    /// <summary>The effects the board shows: as chosen, at most subtle in zen mode, none in a silent zen mode.</summary>
    public EffectsLevel BoardEffects => !IsZen ? Effects.Level
        : ZenSilence ? EffectsLevel.Off
        : Effects.Level == EffectsLevel.Off ? EffectsLevel.Off : EffectsLevel.Subtle;

    /// <summary>The theme's background stops moving in zen mode.</summary>
    public bool IsBackgroundStill => IsZen;

    [RelayCommand]
    private void ToggleZen() => IsZen = !IsZen;

    partial void OnIsZenChanged(bool value)
    {
        bool silent = value && ZenSilence;
        if (_zen is not null)
        {
            _zen.Silent = silent;
        }

        if (_music is not null)
        {
            if (silent && _music.IsPlaying)
            {
                _musicBeforeZen = true;
                _music.Stop();
            }
            else if (!value && _musicBeforeZen)
            {
                _musicBeforeZen = false;
                _music.Start();
            }
        }

        Game.ShowStatus(Tr.T(value ? "Zen.On" : "Zen.Off"));
    }

    /// <summary>The sidebar's width as last dragged (null = the default), kept between sessions.</summary>
    public double? SidebarWidth => _settings?.Current.SidebarWidth;

    public void RememberSidebarWidth(double width)
    {
        if (_settings is not null && width > 0 && Math.Abs((_settings.Current.SidebarWidth ?? 0) - width) >= 1)
        {
            _settings.Save(_settings.Current with { SidebarWidth = Math.Round(width) });
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

/// <summary>It is your move in another online game.</summary>
public sealed record TurnNotice(long GameId, string Text);
