using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Engines;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

/// <summary>
/// A local game where GTP engines play one or both colours. Whenever the shown position is the end of a line and an
/// engine is to move, it asks that engine (<c>genmove</c>) and plays the answer; navigating elsewhere abandons the
/// question. Works from any position, so you can go back, try another move and let the engine answer it.
/// </summary>
public sealed partial class EngineMatchViewModel : ViewModelBase, ILocalOpponent
{
    /// <summary>Pause between moves when two engines play each other, so the game can be followed.</summary>
    public static TimeSpan EngineVsEngineDelay { get; set; } = TimeSpan.FromMilliseconds(450);

    private readonly GameViewModel _game;
    private readonly IGtpEngineHost _host;
    private readonly IUiDispatcher _ui;
    private readonly ILogger _logger;
    private CancellationTokenSource? _thinking;
    private GameNode? _thinkingFor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(PauseLabel))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(PauseLabel))]
    private bool _isPaused;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private EngineChoice? _thinker;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _problem;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _finished;

    public EngineMatchViewModel(GameViewModel game, IGtpEngineHost host, IUiDispatcher ui, ILogger<EngineMatchViewModel>? logger = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _game.PropertyChanged += OnGamePropertyChanged;
        _game.TreeReplacing += (_, _) => Stop();
    }

    public EngineChoice? Black { get; private set; }

    public EngineChoice? White { get; private set; }

    public bool IsEngineVsEngine => Black is not null && White is not null;

    /// <summary>Who plays Black and White ("You" for the human).</summary>
    public string BlackLabel => Black?.Name ?? Tr.T("Engines.You");

    public string WhiteLabel => White?.Name ?? Tr.T("Engines.You");

    /// <summary>"KataGo vs You", e.g. for the tooltip.</summary>
    public string Title => Tr.F("Engines.MatchTitle", BlackLabel, WhiteLabel);

    public string PauseLabel => Tr.T(IsPaused ? "Engines.Resume" : "Engines.Pause");

    public string? StatusText =>
        !IsActive ? null
        : Problem is { } p ? p
        : Finished is { } f ? f
        : Thinker is { } t ? Tr.F("Engines.Thinking", t.Name)
        : IsPaused ? Tr.T("Engines.Paused")
        : null;

    /// <summary>The status line shows "Paused" only while the engine is the one to move.</summary>
    string? ILocalOpponent.StatusText =>
        IsActive && Problem is null && Finished is null && Thinker is null && IsPaused && !Controls(_game.Board.ToMove) ? null : StatusText;

    public bool Controls(Stone color) => IsActive && (color == Stone.White ? White : color == Stone.Black ? Black : null) is not null;

    /// <summary>Starts playing the game on the board (call after loading it).</summary>
    public void Start(EngineChoice? black, EngineChoice? white)
    {
        Stop();
        Black = black;
        White = white;
        Problem = null;
        Finished = null;
        IsPaused = false;
        IsActive = black is not null || white is not null;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BlackLabel));
        OnPropertyChanged(nameof(WhiteLabel));
        OnPropertyChanged(nameof(IsEngineVsEngine));
        _game.Opponent = IsActive ? this : null;
        Evaluate();
    }

    /// <summary>Ends the engine game; the moves stay on the board as a normal game.</summary>
    [RelayCommand]
    public void Stop()
    {
        CancelThinking();
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        if (ReferenceEquals(_game.Opponent, this))
        {
            _game.Opponent = null;
        }
    }

    [RelayCommand]
    private void TogglePause()
    {
        if (!IsActive)
        {
            return;
        }

        IsPaused = !IsPaused;
        if (IsPaused)
        {
            CancelThinking();
        }
        else
        {
            Problem = null;
            Finished = null;
        }

        _game.OpponentChanged();
        Evaluate();
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.CurrentNode) or nameof(GameViewModel.IsEditMode))
        {
            Evaluate();
        }
    }

    /// <summary>Asks the engine to move when the shown position is the end of a line and it is the engine's turn.</summary>
    private void Evaluate()
    {
        if (!IsActive)
        {
            return;
        }

        if (_game.IsOpponentSuspended)
        {
            return;
        }

        GameNode node = _game.CurrentNode;
        if (_thinkingFor == node)
        {
            return;
        }

        CancelThinking();
        if (IsPaused || Finished is not null || _game.IsEditMode || _game.IsOnline || node.Children.Count > 0 || _game.IsGameOver)
        {
            return;
        }

        Stone color = _game.Board.ToMove;
        if ((color == Stone.White ? White : Black) is not { } engine)
        {
            return;
        }

        GtpPosition position = GtpPositions.At(_game.Cursor, node);
        var cts = new CancellationTokenSource();
        _thinking = cts;
        _thinkingFor = node;
        Thinker = engine;
        _game.OpponentChanged();
        _ = ThinkAsync(engine, node, position, cts);
    }

    private async Task ThinkAsync(EngineChoice engine, GameNode node, GtpPosition position, CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        try
        {
            if (IsEngineVsEngine && position.Moves.Count > 0)
            {
                await Task.Delay(EngineVsEngineDelay, token);
            }

            GtpEngine gtp = await _host.AcquireAsync(engine, token);
            GtpMove move = await gtp.GenMoveAsync(position, token);
            _ui.Post(() => Apply(engine, node, position.ToMove, move, cts));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The user went elsewhere, paused or stopped.
        }
        catch (EngineException ex)
        {
            _logger.LogWarning("{Engine} failed: {Error}", engine.Name, ex.Message);
            _ui.Post(() =>
            {
                if (ReferenceEquals(_thinking, cts))
                {
                    Done();
                    Problem = ex.Message;
                    IsPaused = true;
                    _game.OpponentChanged();
                }
            });
        }
    }

    private void Apply(EngineChoice engine, GameNode node, Stone color, GtpMove move, CancellationTokenSource cts)
    {
        if (!ReferenceEquals(_thinking, cts) || cts.IsCancellationRequested || _game.CurrentNode != node)
        {
            return;
        }

        Done();
        if (move.Resign)
        {
            _game.SetResult(color == Stone.Black ? "W+R" : "B+R");
            Finished = Tr.F("Engines.Resigned", engine.Name);
            _game.OpponentChanged();
            return;
        }

        if (!_game.PlayEngineMove(move.Point))
        {
            Problem = Tr.F("Engines.IllegalMove", engine.Name, move.Point?.ToHuman(_game.Board.Height) ?? "pass");
            IsPaused = true;
            _game.OpponentChanged();
        }
    }

    private void Done()
    {
        _thinking?.Dispose();
        _thinking = null;
        _thinkingFor = null;
        Thinker = null;
    }

    private void CancelThinking()
    {
        if (_thinking is { } t)
        {
            t.Cancel();
            Done();
            _game.OpponentChanged();
        }
    }
}
