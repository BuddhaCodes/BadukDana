using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.Core;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Two players on the same computer: play, pass and undo on a linear history of immutable positions.
/// (The variation tree arrives with SGF support in Phase 3.)
/// </summary>
public sealed partial class LocalGameViewModel : ViewModelBase
{
    private readonly List<HistoryEntry> _history = [];

    [ObservableProperty]
    private BoardState _board;

    [ObservableProperty]
    private Point? _lastMove;

    [ObservableProperty]
    private Point? _hoverPoint;

    [ObservableProperty]
    private Stone _ghostStone;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isGameOver;

    public LocalGameViewModel()
        : this(19, RuleSet.Japanese)
    {
    }

    public LocalGameViewModel(int size, RuleSet rules)
    {
        _board = BoardState.Create(size, rules);
        Reset(_board);
    }

    /// <summary>Number of moves (including passes) played so far.</summary>
    public int MoveNumber => _history.Count - 1;

    public string MoveNumberText => $"Jugada {MoveNumber}";

    public string CapturesText => $"Capturas ● {Board.BlackCaptures} · ○ {Board.WhiteCaptures}";

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play(Point point)
    {
        Stone color = Board.ToMove;
        MoveResult result = Board.TryPlay(color, point);
        if (result.State is not { } next)
        {
            StatusText = $"Jugada ilegal en {point.ToHuman(Board.Height)}: {Describe(result.Reason)}";
            return;
        }

        Push(next, point);
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Pass()
    {
        Push(Board.Pass(Board.ToMove), null);
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        _history.RemoveAt(_history.Count - 1);
        HistoryEntry previous = _history[^1];
        Board = previous.State;
        LastMove = previous.Move;
        IsGameOver = false;
        Refresh();
    }

    [RelayCommand]
    private void NewGame(int size)
    {
        Reset(BoardState.Create(size is >= BoardState.MinSize and <= BoardState.MaxSize ? size : Board.Width, Board.Rules));
    }

    private bool CanPlay() => !IsGameOver;

    private bool CanUndo() => _history.Count > 1;

    partial void OnHoverPointChanged(Point? value) => UpdateGhost();

    partial void OnIsGameOverChanged(bool value)
    {
        PlayCommand.NotifyCanExecuteChanged();
        PassCommand.NotifyCanExecuteChanged();
    }

    private void Reset(BoardState start)
    {
        _history.Clear();
        _history.Add(new HistoryEntry(start, null, false));
        Board = start;
        LastMove = null;
        IsGameOver = false;
        Refresh();
    }

    private void Push(BoardState next, Point? move)
    {
        bool isPass = move is null;
        bool secondPass = isPass && _history[^1].IsPass;
        _history.Add(new HistoryEntry(next, move, isPass));
        Board = next;
        LastMove = move;
        IsGameOver = secondPass;
        Refresh();
    }

    private void Refresh()
    {
        StatusText = IsGameOver
            ? "Partida terminada: dos pases seguidos"
            : _history[^1].IsPass
                ? $"{Name(Board.ToMove.Opponent())} pasa · juegan {Name(Board.ToMove).ToLowerInvariant()}"
                : $"Juegan {Name(Board.ToMove).ToLowerInvariant()}";
        OnPropertyChanged(nameof(MoveNumber));
        OnPropertyChanged(nameof(MoveNumberText));
        OnPropertyChanged(nameof(CapturesText));
        UndoCommand.NotifyCanExecuteChanged();
        UpdateGhost();
    }

    private void UpdateGhost()
    {
        GhostStone = !IsGameOver && HoverPoint is { } p && Board.IsLegal(Board.ToMove, p) ? Board.ToMove : Stone.Empty;
    }

    private static string Name(Stone color) => color == Stone.Black ? "Negras" : "Blancas";

    private static string Describe(IllegalMoveReason? reason) => reason switch
    {
        IllegalMoveReason.Occupied => "punto ocupado",
        IllegalMoveReason.Suicide => "suicidio",
        IllegalMoveReason.Ko => "ko",
        IllegalMoveReason.Superko => "superko (repite una posición)",
        IllegalMoveReason.OutOfBounds => "fuera del tablero",
        _ => "no permitida",
    };

    private sealed record HistoryEntry(BoardState State, Point? Move, bool IsPass);
}
