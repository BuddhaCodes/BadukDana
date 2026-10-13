using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>A time control to start the clock with, with its name for the menus.</summary>
public sealed record ClockPreset(TimeControl Control)
{
    public string Name => Control.Describe();
}

/// <summary>
/// The clock of a local game (two people at one computer, or against an engine): chosen from the menu, it runs for
/// the player to move while the game is at its last move, and loses on time with the result "B+T"/"W+T".
/// Online games use OGS's clock instead.
/// </summary>
public sealed partial class LocalClockViewModel : ViewModelBase, IDisposable
{
    /// <summary>Seconds left that turn the clock red (main time in absolute/Fischer; each byo-yomi period).</summary>
    public const double LowSeconds = 10;

    private readonly GameViewModel _game;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly ISoundService? _sounds;
    private ITimer? _timer;
    private DateTimeOffset _lastTick;
    private GameNode? _lastNode;
    private GameClock? _clock;
    private GameTree? _tree;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClock), nameof(BlackText), nameof(WhiteText), nameof(IsBlackLow), nameof(IsWhiteLow), nameof(Description))]
    private int _version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private bool _isPaused;

    public LocalClockViewModel(GameViewModel game, IUiDispatcher? ui = null, TimeProvider? time = null, ISoundService? sounds = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _ui = ui ?? new AvaloniaUiDispatcher();
        _time = time ?? TimeProvider.System;
        _sounds = sounds;
        _game.PropertyChanged += OnGameChanged;
    }

    public IReadOnlyList<ClockPreset> Presets { get; } =
    [
        new(TimeControl.Fischer(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10))),
        new(TimeControl.ByoYomi(TimeSpan.FromMinutes(10), 5, TimeSpan.FromSeconds(30))),
        new(TimeControl.ByoYomi(TimeSpan.FromMinutes(20), 5, TimeSpan.FromSeconds(30))),
        new(TimeControl.ByoYomi(TimeSpan.FromMinutes(30), 5, TimeSpan.FromSeconds(30))),
        new(TimeControl.ByoYomi(TimeSpan.FromHours(1), 5, TimeSpan.FromSeconds(60))),
        new(TimeControl.Absolute(TimeSpan.FromMinutes(20))),
    ];

    public bool HasClock => _clock is not null;

    public GameClock? Clock => _clock;

    public string Description => _clock?.Control.Describe() ?? string.Empty;

    public string BlackText => Text(Stone.Black);

    public string WhiteText => Text(Stone.White);

    public bool IsBlackLow => IsLow(Stone.Black);

    public bool IsWhiteLow => IsLow(Stone.White);

    /// <summary>The clock is counting down right now.</summary>
    public bool IsRunning => _clock is not null && !IsPaused && Ticking();

    /// <summary>Starts a clock for this game (both players get the full time), or removes it with null.</summary>
    [RelayCommand]
    private void Start(ClockPreset? preset)
    {
        StopTimer();
        _clock = preset is null ? null : new GameClock(preset.Control);
        IsPaused = false;
        _lastNode = _game.CurrentNode;
        _tree = _game.Tree;
        _lastTick = _time.GetUtcNow();
        if (_clock is not null)
        {
            _timer = _time.CreateTimer(_ => _ui.Post(() => Tick(_time.GetUtcNow())), null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
            _game.ShowStatus(Tr.F("Clock.Started", preset!.Name));
        }

        Version++;
    }

    [RelayCommand]
    private void Remove() => Start(null);

    [RelayCommand]
    private void TogglePause()
    {
        if (_clock is null)
        {
            return;
        }

        Tick(_time.GetUtcNow());
        IsPaused = !IsPaused;
        _lastTick = _time.GetUtcNow();
    }

    /// <summary>Time passes for the player to move (called by the timer; public for tests).</summary>
    public void Tick(DateTimeOffset now)
    {
        TimeSpan elapsed = now - _lastTick;
        _lastTick = now;
        if (_clock is null || IsPaused || !Ticking() || elapsed <= TimeSpan.Zero)
        {
            return;
        }

        Stone toMove = _game.Board.ToMove;
        bool wasLow = IsLow(toMove);
        if (_clock.Elapse(toMove, elapsed))
        {
            _game.SetResult(toMove == Stone.Black ? "W+T" : "B+T");
            _game.ShowStatus(Tr.F("Clock.Flagged", Tr.T(toMove == Stone.Black ? "Common.Black" : "Common.White")));
            _sounds?.Play(SoundEffect.Atari, 0.6);
            StopTimer();
        }
        else if (!wasLow && IsLow(toMove))
        {
            _sounds?.Play(SoundEffect.Atari, 0.35); // ten seconds left
        }

        Version++;
    }

    public void Dispose() => StopTimer();

    private bool Ticking() =>
        _game.Online is null && !_game.IsEditMode && !_game.IsGameOver && _game.CurrentNode.Children.Count == 0
        && string.IsNullOrEmpty(_game.Tree.Info.Result);

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameViewModel.TreeVersion))
        {
            return;
        }

        if (_clock is not null && !ReferenceEquals(_tree, _game.Tree))
        {
            Start(null); // another game: its clock goes away
            return;
        }

        // A move (or pass) just added at the end of the game: its player's period starts over, or gets the increment.
        GameNode now = _game.CurrentNode;
        GameNode? before = _lastNode;
        DateTimeOffset at = _time.GetUtcNow();
        TimeSpan thinking = at - _lastTick;
        _lastNode = now;
        _lastTick = at;
        if (_clock is not null && before is not null && now.Parent == before
            && now.GetMove(_game.Cursor.BoardSize) is { } move && !IsPaused)
        {
            // The thinking time since the last tick belongs to the player who just moved.
            if (_clock.Elapse(move.Color, thinking))
            {
                _game.SetResult(move.Color == Stone.Black ? "W+T" : "B+T");
                StopTimer();
            }
            else
            {
                _clock.MoveDone(move.Color);
            }
        }

        Version++;
        OnPropertyChanged(nameof(IsRunning));
    }

    private string Text(Stone color)
    {
        if (_clock is null)
        {
            return string.Empty;
        }

        PlayerClock p = _clock.For(color);
        return p.InOvertime && _clock.Control.System == TimeSystem.ByoYomi
            ? $"{GameClock.Format(p.Period)} ×{p.Periods}"
            : GameClock.Format(p.Main);
    }

    private bool IsLow(Stone color)
    {
        if (_clock is null)
        {
            return false;
        }

        PlayerClock p = _clock.For(color);
        TimeSpan left = p.InOvertime ? p.Period : p.Main;
        return p.IsFlagged || (left.TotalSeconds <= LowSeconds && (p.InOvertime || _clock.Control.System != TimeSystem.ByoYomi));
    }
}
