using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class GameViewModel
{
    // ---------- Play and edit ----------

    /// <summary>
    /// Tap and confirm (phones): the first tap shows the stone, a second tap on the same point plays it. Avoids
    /// stones played by a slip of the finger on a small 19×19 board, online and locally.
    /// </summary>
    public bool ConfirmMoves
    {
        get => _confirmMoves;
        set
        {
            if (_confirmMoves != value)
            {
                _confirmMoves = value;
                OnPropertyChanged();
                PendingMove = null;
            }
        }
    }

    private bool _confirmMoves;

    /// <summary>The stone waiting for its second tap (with <see cref="ConfirmMoves"/>), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PendingStone), nameof(PendingMoveText), nameof(HasPendingMove), nameof(ConfirmPendingLabel))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmPendingCommand), nameof(CancelPendingCommand))]
    private Point? _pendingMove;

    public bool HasPendingMove => PendingMove is not null;

    /// <summary>The waiting stone's coordinate ("L7"), for the confirm button.</summary>
    public string PendingMoveText => PendingMove is { } p ? p.ToHuman(Board.Height) : string.Empty;

    /// <summary>"Confirmar L7".</summary>
    public string ConfirmPendingLabel => Tr.F("Phone.ConfirmMove", PendingMoveText);

    /// <summary>Plays the waiting stone (the same as tapping it again).</summary>
    [RelayCommand(CanExecute = nameof(HasPendingMove))]
    private void ConfirmPending()
    {
        if (PendingMove is { } point && PlayCommand.CanExecute(point))
        {
            PlayCommand.Execute(point);
        }
    }

    [RelayCommand(CanExecute = nameof(HasPendingMove))]
    private void CancelPending() => PendingMove = null;

    /// <summary>The colour of the waiting stone.</summary>
    public Stone PendingStone => PendingMove is null ? Stone.Empty : Online is { } o ? o.MyColor : Board.ToMove;

    /// <summary>With <see cref="ConfirmMoves"/>, true when this tap only shows the stone (or moves it elsewhere).</summary>
    private bool WaitsForConfirmation(Point point)
    {
        if (!ConfirmMoves || IsEditMode)
        {
            return false;
        }

        bool placeable = Online is { } online ? online.CanPlayAt(point) : !IsEngineTurn && Board.IsLegal(Board.ToMove, point);
        if (!placeable || PendingMove == point)
        {
            PendingMove = null;
            return false;
        }

        PendingMove = point;
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void Play(Point point)
    {
        if (_boardOverride?.Invoke(point) == true)
        {
            return;
        }

        if (WaitsForConfirmation(point))
        {
            return;
        }

        if (Online is { } online)
        {
            online.OnBoardClicked(point);
            return;
        }

        if (IsEditMode)
        {
            ApplyTool(point);
            return;
        }

        if (IsEngineTurn)
        {
            return;
        }

        MoveResult result = _cursor.Play(point);
        if (!result.IsLegal)
        {
            SetStatus(Tr.F("Game.IllegalMove", point.ToHuman(Board.Height), Describe(result.Reason)));
            return;
        }

        IsDirty = true;
        MovePlayed?.Invoke(this, CurrentNode);
    }

    /// <summary>
    /// The position in words, for screen readers: the move just shown, who plays and the captures
    /// ("Move 23: Black D4. White to play. Captures: Black 2, White 1.").
    /// </summary>
    public string BoardDescription
    {
        get
        {
            string move = CurrentNode.GetMove(Cursor.BoardSize) is { } m
                ? Tr.F("Access.Move", MoveNumber, Tr.T(m.Color == Stone.Black ? "Common.Black" : "Common.White"), m.Point is { } p ? p.ToHuman(Board.Height) : Tr.T("Viewer.Pass"))
                : Tr.T("Access.Start");
            string toPlay = IsGameOver ? Tr.T("Access.Over") : Tr.F("Access.ToPlay", Tr.T(Board.ToMove == Stone.White ? "Common.White" : "Common.Black"));
            return $"{move} {toPlay} {Tr.F("Access.Captures", Board.BlackCaptures, Board.WhiteCaptures)}";
        }
    }

    /// <summary>
    /// Plays a typed coordinate ("D4", "q16") or a pass ("pass", "pasa"); false when it is not a coordinate or the
    /// move was not played. Typed moves need no second tap.
    /// </summary>
    public bool PlayTyped(string? text)
    {
        string t = text?.Trim() ?? string.Empty;
        if (t.Length == 0)
        {
            return false;
        }

        int before = MoveNumber;
        if (t.Equals("pass", StringComparison.OrdinalIgnoreCase) || t.Equals(Tr.T("Viewer.Pass"), StringComparison.OrdinalIgnoreCase))
        {
            if (PassCommand.CanExecute(null))
            {
                PassCommand.Execute(null);
            }

            return Online is not null || MoveNumber > before;
        }

        if (!Point.TryParseHuman(t, Board.Height, out Point point) || !Board.IsOnBoard(point))
        {
            SetStatus(Tr.F("Access.NotACoordinate", t));
            return false;
        }

        PlayCommand.Execute(point);
        if (PendingMove == point)
        {
            PlayCommand.Execute(point); // tap-and-confirm: typing is the confirmation
        }

        return Online is not null || MoveNumber > before;
    }

    /// <summary>A stone was just played on the board by the user (not navigation, not an edit, not a pass).</summary>
    public event EventHandler<GameNode>? MovePlayed;

    [RelayCommand(CanExecute = nameof(CanPass))]
    private void Pass()
    {
        if (Online is { } online)
        {
            online.PassCommand.Execute(null);
            return;
        }

        if (IsEngineTurn)
        {
            return;
        }

        _cursor.Play(null);
        IsDirty = true;
    }

    /// <summary>The P shortcut: local games only, so a stray key press never passes in an online game.</summary>
    [RelayCommand(CanExecute = nameof(CanPassWithKey))]
    private void PassKey() => Pass();

    private bool CanPassWithKey() => Online is null && CanPass();

    /// <summary>
    /// Takes back the last move: removes it if it ends the line, otherwise just steps back. Against an engine it
    /// goes back to your turn (your move and the engine's reply).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (_cursor.Current.Children.Count == 0)
        {
            // The engine must not start answering the position in between.
            IsOpponentSuspended = true;
            try
            {
                _cursor.DeleteCurrent();
                bool humanPlays = Opponent is { } o && !(o.Controls(Stone.Black) && o.Controls(Stone.White));
                if (humanPlays && Opponent!.Controls(Board.ToMove) && _cursor.CanGoBack && _cursor.Current.Children.Count == 0
                    && _cursor.Current.GetMove(_cursor.BoardSize) is not null)
                {
                    _cursor.DeleteCurrent();
                }
            }
            finally
            {
                IsOpponentSuspended = false;
            }

            IsDirty = true;
            OnPropertyChanged(nameof(CurrentNode));
        }
        else
        {
            _cursor.Previous();
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void SelectTool(EditTool tool)
    {
        EditTool = tool;
        IsEditMode = true;
    }

    /// <summary>Deletes the shown move with everything after it (asks first when that is more than this move).</summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task DeleteNode()
    {
        int moves = 1 + CurrentNode.Descendants().Count(n => n.HasMove);
        if (moves > 1 && _dialogs is not null
            && !await _dialogs.ConfirmAsync(Tr.T("Main.DeleteNode"), Tr.F("Game.DeleteConfirm", moves)))
        {
            return;
        }

        _cursor.DeleteCurrent();
        IsDirty = true;
        SetStatus(Tr.F("Game.Deleted", moves));
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void PromoteVariation()
    {
        _cursor.PromoteToMainLine();
        IsDirty = true;
    }

    // ---------- Navigation ----------

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _cursor.Previous();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward() => _cursor.Next();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoFirst() => _cursor.First();

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoLast() => _cursor.Last();

    [RelayCommand]
    private void NextVariation() => _cursor.NextVariation();

    [RelayCommand]
    private void PreviousVariation() => _cursor.PreviousVariation();

    [RelayCommand]
    private void GoToNode(GameNode? node)
    {
        if (node is not null)
        {
            _cursor.GoTo(node);
        }
    }

    /// <summary>Mouse wheel over the board: negative goes back, positive forward.</summary>
    [RelayCommand]
    private void Scroll(int direction)
    {
        if (direction < 0)
        {
            _cursor.Previous();
        }
        else
        {
            _cursor.Next();
        }
    }
}
