using Hoshi.Core;

namespace Hoshi.Sgf.Joseki;

public enum DrillAnswer
{
    /// <summary>The next move of the line; it was played.</summary>
    Correct,

    /// <summary>The last move of the line; the drill is over.</summary>
    Completed,

    /// <summary>A move of another known line: fine, but this drill asks for the line being trained.</summary>
    AlsoJoseki,

    /// <summary>Not joseki here (a mistake); the stone is not placed.</summary>
    Wrong,

    /// <summary>An occupied or illegal point, or the drill is already over: ignored.</summary>
    Ignored,
}

/// <summary>
/// One practice run of a joseki line in a given orientation. The player places every stone (both colours); a mirror
/// image of the line is accepted and followed, a move of another known line is acknowledged without advancing,
/// and after two misses on the same move the answer is shown in <see cref="Hint"/>.
/// </summary>
public sealed class JosekiDrill
{
    private readonly IReadOnlyList<JosekiLine> _library;
    private readonly List<Point> _played = [];
    private int _missesHere;

    public JosekiDrill(JosekiLine line, Symmetry symmetry, IReadOnlyList<JosekiLine> library)
    {
        Line = line ?? throw new ArgumentNullException(nameof(line));
        Symmetry = symmetry ?? throw new ArgumentNullException(nameof(symmetry));
        _library = library ?? [];
        Board = BoardState.Create(line.Size).WithToMove(line.Moves[0].Color);
    }

    public JosekiLine Line { get; }

    /// <summary>The orientation being followed (it changes when the player follows a mirror image).</summary>
    public Symmetry Symmetry { get; private set; }

    public BoardState Board { get; private set; }

    public int Position => _played.Count;

    public IReadOnlyList<Point> Played => _played;

    public Point? LastMove => _played.Count > 0 ? _played[^1] : null;

    public bool IsComplete => Position >= Line.Moves.Count;

    public Point? Expected => IsComplete ? null : Symmetry.Apply(Line.Moves[Position].Point, Line.Size);

    public Stone ToMove => IsComplete ? Stone.Empty : Line.Moves[Position].Color;

    /// <summary>The expected move, once the player missed it twice.</summary>
    public Point? Hint { get; private set; }

    /// <summary>Wrong moves in this run (a perfect run has none).</summary>
    public int Mistakes { get; private set; }

    public DrillAnswer Attempt(Point point)
    {
        if (IsComplete || !Board.IsLegal(ToMove, point))
        {
            return DrillAnswer.Ignored;
        }

        if (point != Expected)
        {
            Symmetry? mirror = Symmetry.All.FirstOrDefault(s => s != Symmetry && Matches(Line, s) && s.Apply(Line.Moves[Position].Point, Line.Size) == point);
            if (mirror is null)
            {
                if (IsOtherJoseki(point))
                {
                    return DrillAnswer.AlsoJoseki;
                }

                Mistakes++;
                if (++_missesHere >= 2)
                {
                    Hint = Expected;
                }

                return DrillAnswer.Wrong;
            }

            Symmetry = mirror;
        }

        Board = Board.Play(ToMove, point);
        _played.Add(point);
        _missesHere = 0;
        Hint = null;
        if (IsComplete)
        {
            return DrillAnswer.Completed;
        }

        Board = Board.WithToMove(ToMove);
        return DrillAnswer.Correct;
    }

    private bool IsOtherJoseki(Point point) =>
        _library.Any(l => l.Id != Line.Id
            && l.Size == Line.Size
            && l.Moves.Count > Position
            && l.Moves[Position].Color == ToMove
            && Symmetry.All.Any(s => Matches(l, s) && s.Apply(l.Moves[Position].Point, l.Size) == point));

    /// <summary>True when the moves played so far are the start of <paramref name="line"/> under <paramref name="s"/>.</summary>
    private bool Matches(JosekiLine line, Symmetry s)
    {
        for (int i = 0; i < _played.Count; i++)
        {
            if (line.Moves[i].Color != Line.Moves[i].Color || s.Apply(line.Moves[i].Point, line.Size) != _played[i])
            {
                return false;
            }
        }

        return true;
    }
}
