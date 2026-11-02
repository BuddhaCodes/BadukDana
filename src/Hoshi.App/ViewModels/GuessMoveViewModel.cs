using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;

namespace Hoshi.App.ViewModels;

/// <summary>Whose moves to guess.</summary>
public enum GuessSide
{
    Both,
    Black,
    White,
}

/// <summary>A side choice in the menu.</summary>
public sealed record GuessSideOption(GuessSide Side)
{
    public string Name => Tr.T("Guess.Side." + Side);

    public override string ToString() => Name;
}

/// <summary>
/// "Guess the move": open a game (a professional game from a book or a site, or one of your own), start, and click
/// where you think the next move goes. Two tries per move; the right move is then shown and the game goes on. The
/// other side's moves play by themselves when only one colour is guessed. Works on the main line from the position on
/// the board. Closed during your live OGS games. Hoshi ships no professional games (only records with a clear
/// licence may go in the repository): any SGF opened in Hoshi works.
/// </summary>
public sealed partial class GuessMoveViewModel : ViewModelBase
{
    public const int Tries = 2;

    private readonly GameViewModel _game;
    private int _triesLeft;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText), nameof(Overlay))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private int _guessed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private int _firstTry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScoreText))]
    private int _total;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Overlay))]
    private Point? _wrong;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Overlay))]
    private Point? _revealed;

    [ObservableProperty]
    private GuessSideOption _side;

    public GuessMoveViewModel(GameViewModel game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        Sides = [.. Enum.GetValues<GuessSide>().Select(s => new GuessSideOption(s))];
        _side = Sides[0];
    }

    public IReadOnlyList<GuessSideOption> Sides { get; }

    /// <summary>"12 of 20 (9 at the first try)".</summary>
    public string ScoreText => Tr.F("Guess.Score", Guessed, Total, FirstTry);

    /// <summary>The wrong try (red) and, once shown, the move that was played (green).</summary>
    public IReadOnlyList<StudyDrawing> Overlay => !IsActive ? [] :
    [
        .. Wrong is { } w ? [new StudyDrawing("guess-wrong", DrawingKind.Mark, string.Empty, StudyColor.Red, [w])] : Array.Empty<StudyDrawing>(),
        .. Revealed is { } r ? [new StudyDrawing("guess-answer", DrawingKind.Mark, string.Empty, StudyColor.Green, [r])] : Array.Empty<StudyDrawing>(),
    ];

    /// <summary>Not in the player's live OGS game, and only where the game goes on.</summary>
    public bool CanStart => _game.Online is not { IsPlayer: true, IsFinished: false } && NextMove() is not null;

    [RelayCommand]
    private void Start()
    {
        if (!CanStart)
        {
            _game.ShowStatus(Tr.T("Guess.NothingToGuess"));
            return;
        }

        Guessed = 0;
        FirstTry = 0;
        Total = 0;
        Wrong = null;
        Revealed = null;
        Message = string.Empty;
        IsActive = true;
        PrepareNext();
    }

    [RelayCommand]
    private void Stop()
    {
        IsActive = false;
        Wrong = null;
        Revealed = null;
        Message = string.Empty;
        _game.ShowStatus(Tr.F("Guess.Finished", ScoreText));
    }

    /// <summary>"Show": gives up this move (counts as missed) and plays it.</summary>
    [RelayCommand]
    private void Reveal()
    {
        if (IsActive && NextMove() is { } next)
        {
            Total++;
            Advance(next, Tr.F("Guess.WasMove", Name(next)), showAnswer: true);
        }
    }

    /// <summary>A click on the board while guessing.</summary>
    public void Answer(Point point)
    {
        if (!IsActive || NextMove() is not { } next)
        {
            return;
        }

        // The marks of the last move go with the first click on the next one.
        Revealed = null;
        if (_triesLeft == Tries)
        {
            Wrong = null;
        }

        if (next.Point == point)
        {
            Total++;
            Guessed++;
            if (_triesLeft == Tries)
            {
                FirstTry++;
            }

            Advance(next, Tr.T(_triesLeft == Tries ? "Guess.Right" : "Guess.RightSecond"), showAnswer: false);
            return;
        }

        _triesLeft--;
        Wrong = point;
        if (_triesLeft > 0)
        {
            Message = Tr.T("Guess.TryAgain");
            return;
        }

        Total++;
        Advance(next, Tr.F("Guess.WasMove", Name(next)), showAnswer: true);
    }

    private void Advance((GameNode Node, Point? Point, Stone Color) move, string message, bool showAnswer)
    {
        Revealed = showAnswer ? move.Point : null;
        _game.GoToNodeCommand.Execute(move.Node);
        Message = message;
        PrepareNext();
    }

    /// <summary>Plays the moves not to be guessed (passes, the other colour) and waits for the next guess.</summary>
    private void PrepareNext()
    {
        _triesLeft = Tries;
        while (NextMove() is { } next && (next.Point is null || !Guesses(next.Color)))
        {
            _game.GoToNodeCommand.Execute(next.Node);
        }

        if (NextMove() is null)
        {
            IsActive = false;
            Message = Tr.F("Guess.Finished", ScoreText);
            _game.ShowStatus(Message);
            return;
        }

        if (Message.Length == 0)
        {
            Message = Tr.F("Guess.ToPlay", Tr.T(NextMove()!.Value.Color == Stone.White ? "Common.White" : "Common.Black"));
        }
    }

    private bool Guesses(Stone color) => Side.Side switch
    {
        GuessSide.Black => color == Stone.Black,
        GuessSide.White => color == Stone.White,
        _ => true,
    };

    /// <summary>The main line's next move from the position on the board.</summary>
    private (GameNode Node, Point? Point, Stone Color)? NextMove()
    {
        GameNode current = _game.CurrentNode;
        if (current.Children.Count == 0 || current.Children[0].GetMove(_game.Cursor.BoardSize) is not { } move)
        {
            return null;
        }

        return (current.Children[0], move.Point, move.Color);
    }

    private string Name((GameNode Node, Point? Point, Stone Color) move) =>
        move.Point is { } p ? p.ToHuman(_game.Board.Height) : Tr.T("Main.Pass");
}
