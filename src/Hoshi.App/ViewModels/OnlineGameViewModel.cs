using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Ogs;
using Hoshi.Ogs.Games;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

public sealed record ChatLineItem(string Username, string Body, bool IsMine, int MoveNumber)
{
    public string Header => string.Create(CultureInfo.InvariantCulture, $"{Username} · jugada {MoveNumber}");
}

/// <summary>
/// The OGS game coordinator (ARCHITECTURE.md "OgsGameCoordinator"): the only place that knows both the online session
/// and the SGF tree. <c>gamedata</c> rebuilds the tree; each move is appended to the main line (the view follows it
/// when the user was at the last move); board clicks become <c>game/move</c> only when it is the user's turn and the
/// last move is shown. Stones are added when the server echoes the move, so the tree never diverges from OGS.
/// </summary>
public sealed partial class OnlineGameViewModel : ViewModelBase, IDisposable
{
    private readonly IOnlineGame _game;
    private readonly GameViewModel _board;
    private readonly IUiDispatcher _ui;
    private readonly IDialogService? _dialogs;
    private readonly ITimer? _timer;
    private OgsGameSnapshot? _snapshot;
    private OgsClock? _clock;
    private HashSet<Point> _removed = [];
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlayPhase), nameof(IsStoneRemoval), nameof(IsFinished), nameof(IsMyTurn), nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(PassCommand), nameof(ResignCommand), nameof(AcceptScoreCommand), nameof(RejectScoreCommand), nameof(RequestUndoCommand))]
    private OgsGamePhase _phase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(PassCommand))]
    private bool _isSending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _message;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string _blackClock = "—";

    [ObservableProperty]
    private string _whiteClock = "—";

    [ObservableProperty]
    private bool _isBlackClockLow;

    [ObservableProperty]
    private bool _isWhiteClockLow;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendChatCommand))]
    private string _chatInput = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptUndoCommand))]
    private bool _isUndoRequested;

    public OnlineGameViewModel(
        IOnlineGame game, GameViewModel board, IUiDispatcher ui, IDialogService? dialogs = null, TimeProvider? timers = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _dialogs = dialogs;

        _game.GamedataReceived += (_, g) => _ui.Post(() => OnGamedata(g));
        _game.MoveReceived += (_, m) => _ui.Post(() => OnMove(m));
        _game.ClockChanged += (_, c) => _ui.Post(() => { _clock = c; Tick(); });
        _game.PhaseChanged += (_, p) => _ui.Post(() => OnPhase(p));
        _game.RemovedStonesChanged += (_, r) => _ui.Post(() => SetRemoved(r));
        _game.GameEnded += (_, r) => _ui.Post(() => OnEnded(r));
        _game.ChatReceived += (_, l) => _ui.Post(() => ChatLines.Add(new ChatLineItem(l.Username, l.Body, l.PlayerId == _game.MyPlayerId, l.MoveNumber)));
        _game.ErrorReceived += (_, e) => _ui.Post(() => { IsSending = false; Message = e; });
        _game.UndoRequested += (_, _) => _ui.Post(() => IsUndoRequested = true);
        _game.UndoAccepted += (_, n) => _ui.Post(() => OnUndoAccepted(n));

        // Clocks are recomputed locally between server updates (null in tests: they call Tick()).
        _timer = timers?.CreateTimer(_ => _ui.Post(Tick), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Raised when the user leaves the game view (the game itself goes on on the server).</summary>
    public event EventHandler? Left;

    public long GameId => _game.GameId;

    public ObservableCollection<ChatLineItem> ChatLines { get; } = [];

    public string Title => _snapshot is { } s
        ? string.Create(CultureInfo.InvariantCulture, $"OGS #{s.GameId} · {s.Black.Username} vs {s.White.Username}")
        : string.Create(CultureInfo.InvariantCulture, $"OGS #{GameId}");

    public string BlackName => _snapshot?.Black.DisplayName ?? "Negras";

    public string WhiteName => _snapshot?.White.DisplayName ?? "Blancas";

    /// <summary>The user's colour in this game (Empty when watching).</summary>
    public Stone MyColor => _snapshot?.ColorOf(_game.MyPlayerId) ?? Stone.Empty;

    public bool IsPlayer => MyColor != Stone.Empty;

    public bool IsPlayPhase => Phase == OgsGamePhase.Play;

    public bool IsStoneRemoval => Phase == OgsGamePhase.StoneRemoval;

    public bool IsFinished => Phase == OgsGamePhase.Finished;

    /// <summary>
    /// Whose turn it is, from the moves received so far (not the clock, which may arrive after a move's echo;
    /// free handicap placement gives black several moves in a row).
    /// </summary>
    public Stone ToMove => _snapshot?.ColorForMove(_board.MainLineMoveCount) ?? Stone.Empty;

    public bool IsMyTurn => IsPlayPhase && IsPlayer && ToMove == MyColor && _snapshot is not null;

    public IReadOnlySet<Point> Removed => _removed;

    public string StatusText
    {
        get
        {
            if (Message is { } m)
            {
                return m;
            }

            if (_snapshot is null)
            {
                return "Conectando con la partida…";
            }

            return Phase switch
            {
                OgsGamePhase.Finished => ResultText ?? "Partida terminada",
                OgsGamePhase.StoneRemoval => "Conteo: marca las piedras muertas y acepta",
                _ when IsSending => "Enviando jugada…",
                _ when !IsPlayer => "Observando",
                _ when IsMyTurn => "Tu turno",
                _ => "Turno del rival",
            };
        }
    }

    /// <summary>Dead stones shown as crosses during stone removal.</summary>
    public IReadOnlyList<Markup> Overlay => IsStoneRemoval || IsFinished
        ? [.. _removed.Select(p => new Markup(p, MarkupKind.Cross))]
        : [];

    public void Connect() => _game.Connect();

    /// <summary>Whether a click on the board does something right now (drives the ghost stone).</summary>
    public bool CanClickBoard => IsStoneRemoval ? IsPlayer : IsMyTurn && !IsSending && IsAtLastMove;

    public bool CanPlayAt(Point point) =>
        CanClickBoard && !IsStoneRemoval && _board.Board.IsLegal(MyColor, point);

    /// <summary>A click on the board (routed here by <see cref="GameViewModel"/> while online).</summary>
    public void OnBoardClicked(Point point)
    {
        if (IsStoneRemoval)
        {
            ToggleDead(point);
            return;
        }

        if (!IsAtLastMove)
        {
            _board.Cursor.Last();
            return;
        }

        if (!IsMyTurn || IsSending)
        {
            return;
        }

        MoveResult result = _board.Board.TryPlay(MyColor, point);
        if (!result.IsLegal)
        {
            Message = $"Jugada ilegal en {point.ToHuman(_board.Board.Height)}";
            return;
        }

        Message = null;
        IsSending = true;
        _game.Play(point);
        _board.RefreshOnline();
    }

    public void Tick()
    {
        if (_clock is not { } clock || _snapshot is not { } s)
        {
            return;
        }

        (OgsClockReading black, OgsClockReading white) = OgsClockMath.Read(clock, s.TimeControl, _game.ServerNow);
        BlackClock = s.TimeControl.System == "none" ? "sin límite" : black.Format();
        WhiteClock = s.TimeControl.System == "none" ? "sin límite" : white.Format();
        IsBlackClockLow = IsLow(black) && ToMove == Stone.Black && IsPlayPhase;
        IsWhiteClockLow = IsLow(white) && ToMove == Stone.White && IsPlayPhase;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _game.Dispose();
    }

    private bool CanPassOrResign() => IsPlayPhase && IsPlayer && _snapshot is not null;

    private bool CanPass() => CanPassOrResign() && IsMyTurn && !IsSending;

    [RelayCommand(CanExecute = nameof(CanPass))]
    private void Pass()
    {
        if (!IsMyTurn || IsSending)
        {
            Message = "Solo puedes pasar en tu turno";
            return;
        }

        Message = null;
        IsSending = true;
        _game.Play(null);
    }

    [RelayCommand(CanExecute = nameof(CanPassOrResign))]
    private async Task Resign()
    {
        if (_dialogs is not null && !await _dialogs.ConfirmAsync("Abandonar", "¿Seguro que quieres abandonar la partida?"))
        {
            return;
        }

        _game.Resign();
    }

    private bool CanRequestUndo() => IsPlayPhase && IsPlayer && _board.Tree.Root.Children.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRequestUndo))]
    private void RequestUndo()
    {
        _game.RequestUndo();
        Message = "Deshacer solicitado al rival";
    }

    private bool CanAcceptUndo() => IsUndoRequested;

    [RelayCommand(CanExecute = nameof(CanAcceptUndo))]
    private void AcceptUndo()
    {
        _game.AcceptUndo();
        IsUndoRequested = false;
    }

    private bool CanScore() => IsStoneRemoval && IsPlayer;

    [RelayCommand(CanExecute = nameof(CanScore))]
    private void AcceptScore()
    {
        _game.AcceptRemovedStones(_removed);
        Message = "Conteo aceptado; esperando al rival";
    }

    [RelayCommand(CanExecute = nameof(CanScore))]
    private void RejectScore()
    {
        _game.RejectRemovedStones();
        Message = null;
    }

    private bool CanSendChat() => ChatInput.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSendChat))]
    private void SendChat()
    {
        _game.SendChat(ChatInput);
        ChatInput = string.Empty;
    }

    /// <summary>Leaves the game view; the tree stays loaded as a normal (savable) game.</summary>
    [RelayCommand]
    private void Leave()
    {
        Dispose();
        if (ReferenceEquals(_board.Online, this))
        {
            _board.DetachOnline();
        }

        Left?.Invoke(this, EventArgs.Empty);
    }

    private bool IsAtLastMove => _board.CurrentNode.Children.Count == 0;

    private static bool IsLow(OgsClockReading r) =>
        (r.Main > TimeSpan.Zero ? r.Main : r.PeriodLeft ?? r.BlockLeft ?? TimeSpan.Zero) <= TimeSpan.FromSeconds(10);

    private void OnGamedata(OgsGameSnapshot g)
    {
        _snapshot = g;
        _clock = g.Clock ?? _clock;
        _board.LoadOnline(BuildTree(g), this);
        IsSending = false;
        Message = null;
        Phase = g.Phase;
        ResultText = g.Result?.Describe();
        SetRemoved(g.Removed);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(BlackName));
        OnPropertyChanged(nameof(WhiteName));
        OnPropertyChanged(nameof(MyColor));
        OnPropertyChanged(nameof(IsPlayer));
        OnPropertyChanged(nameof(IsMyTurn));
        OnPropertyChanged(nameof(StatusText));
        PassCommand.NotifyCanExecuteChanged();
        ResignCommand.NotifyCanExecuteChanged();
        RequestUndoCommand.NotifyCanExecuteChanged();
        AcceptScoreCommand.NotifyCanExecuteChanged();
        RejectScoreCommand.NotifyCanExecuteChanged();
        Tick();
    }

    private void OnMove(OgsGameMove move)
    {
        if (_snapshot is null)
        {
            return;
        }

        IsSending = false;
        Message = null;
        _board.AppendOnlineMove(move.Color, move.Point);
        OnPropertyChanged(nameof(IsMyTurn));
        PassCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusText));
        RequestUndoCommand.NotifyCanExecuteChanged();
    }

    private void OnPhase(OgsGamePhase phase)
    {
        Phase = phase;
        Message = null;
        if (phase == OgsGamePhase.Play)
        {
            SetRemoved([]);
        }

        _board.RefreshOnline();
    }

    private void OnEnded(OgsGameResult result)
    {
        Phase = OgsGamePhase.Finished;
        ResultText = result.Describe();
        Message = null;
        _board.Tree.Info.Result = result.ToSgf();
        _board.RefreshOnline();
        OnPropertyChanged(nameof(StatusText));
        Tick();
    }

    private void OnUndoAccepted(int moveNumber)
    {
        IsUndoRequested = false;
        Message = null;
        _board.TruncateOnlineMoves(moveNumber);
        OnPropertyChanged(nameof(IsMyTurn));
        PassCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusText));
    }

    private void SetRemoved(IEnumerable<Point> removed)
    {
        _removed = [.. removed];
        OnPropertyChanged(nameof(Removed));
        OnPropertyChanged(nameof(Overlay));
        _board.RefreshOnline();
    }

    private void ToggleDead(Point point)
    {
        BoardState board = _board.Board;
        if (!IsPlayer || !board.IsOnBoard(point) || board[point] == Stone.Empty)
        {
            return;
        }

        IReadOnlyList<Point> group = board.GetGroup(point);
        _game.SetRemovedStones(group, removed: !_removed.Contains(point));
    }

    internal static GameTree BuildTree(OgsGameSnapshot g)
    {
        GameTree tree = GameTree.Create(Math.Max(g.Width, g.Height), g.Rules, g.Komi);
        GameNode root = tree.Root;
        if (g.Width != g.Height)
        {
            root.SetValue("SZ", string.Create(CultureInfo.InvariantCulture, $"{g.Width}:{g.Height}"));
        }

        tree.Info.BlackPlayer = g.Black.Username;
        tree.Info.WhitePlayer = g.White.Username;
        tree.Info.BlackRank = g.Black.Rank;
        tree.Info.WhiteRank = g.White.Rank;
        tree.Info.GameName = string.IsNullOrWhiteSpace(g.Name) ? null : g.Name;
        tree.Info.Place = string.Create(CultureInfo.InvariantCulture, $"OGS #{g.GameId}");
        if (g.Handicap > 0)
        {
            root.SetValue("HA", g.Handicap.ToString(CultureInfo.InvariantCulture));
        }

        if (g.InitialBlack.Count > 0)
        {
            root.SetValues("AB", g.InitialBlack.Select(p => p.ToSgf()));
        }

        if (g.InitialWhite.Count > 0)
        {
            root.SetValues("AW", g.InitialWhite.Select(p => p.ToSgf()));
        }

        root.SetValue("PL", g.InitialPlayer == Stone.White ? "W" : "B");
        if (g.Result is { } result)
        {
            tree.Info.Result = result.ToSgf();
        }

        GameNode node = root;
        foreach (OgsGameMove m in g.Moves)
        {
            node = node.AddChild();
            node.SetValue(m.Color == Stone.White ? "W" : "B", m.Point?.ToSgf() ?? string.Empty);
        }

        return tree;
    }
}
