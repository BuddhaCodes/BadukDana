namespace Hoshi.Core;

/// <summary>
/// Scores a finished position after dead stones have been agreed.
/// Empty regions (including points freed by dead stones) bordered by a single colour are that colour's territory;
/// anything touching both colours — for example the shared liberties of a seki — is dame.
/// Eyes of groups in seki are counted as territory, matching OGS's default (non-strict) seki handling.
/// </summary>
public static class Scoring
{
    public static ScoreResult Score(BoardState board, IEnumerable<Point> deadStones, double komi, int handicap = 0)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(deadStones);

        int width = board.Width;
        int height = board.Height;
        Stone[] cells = board.CopyCells();

        int deadBlack = 0;
        int deadWhite = 0;
        foreach (Point p in deadStones.Distinct())
        {
            if (!board.IsOnBoard(p) || board[p] == Stone.Empty)
            {
                throw new ArgumentException($"{p} does not contain a stone and cannot be marked dead.", nameof(deadStones));
            }

            if (board[p] == Stone.Black)
            {
                deadBlack++;
            }
            else
            {
                deadWhite++;
            }

            cells[(p.Y * width) + p.X] = Stone.Empty;
        }

        var owner = new Stone[cells.Length];
        var visited = new bool[cells.Length];
        var dame = new List<Point>();
        int blackTerritory = 0;
        int whiteTerritory = 0;
        int blackStones = 0;
        int whiteStones = 0;

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == Stone.Black)
            {
                blackStones++;
                owner[i] = Stone.Black;
            }
            else if (cells[i] == Stone.White)
            {
                whiteStones++;
                owner[i] = Stone.White;
            }
        }

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != Stone.Empty || visited[i])
            {
                continue;
            }

            (List<int> region, bool touchesBlack, bool touchesWhite) = FloodRegion(cells, visited, i, width, height);
            Stone regionOwner = touchesBlack == touchesWhite ? Stone.Empty : touchesBlack ? Stone.Black : Stone.White;
            foreach (int r in region)
            {
                owner[r] = regionOwner;
                if (regionOwner == Stone.Empty)
                {
                    dame.Add(new Point(r % width, r / width));
                }
            }

            if (regionOwner == Stone.Black)
            {
                blackTerritory += region.Count;
            }
            else if (regionOwner == Stone.White)
            {
                whiteTerritory += region.Count;
            }
        }

        RuleSet rules = board.Rules;
        int blackPrisoners = board.BlackCaptures + deadWhite;
        int whitePrisoners = board.WhiteCaptures + deadBlack;
        int compensation = rules.CompensationFor(handicap);

        double blackScore;
        double whiteScore;
        if (rules.Scoring == ScoringMethod.Territory)
        {
            blackScore = blackTerritory + blackPrisoners;
            whiteScore = whiteTerritory + whitePrisoners + komi + compensation;
        }
        else
        {
            blackScore = blackStones + blackTerritory;
            whiteScore = whiteStones + whiteTerritory + komi + compensation;
        }

        dame.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        return new ScoreResult(owner, width)
        {
            Method = rules.Scoring,
            BlackTerritory = blackTerritory,
            WhiteTerritory = whiteTerritory,
            BlackStones = blackStones,
            WhiteStones = whiteStones,
            BlackPrisoners = blackPrisoners,
            WhitePrisoners = whitePrisoners,
            Komi = komi,
            HandicapCompensation = compensation,
            BlackScore = blackScore,
            WhiteScore = whiteScore,
            Dame = dame,
        };
    }

    private static (List<int> Region, bool TouchesBlack, bool TouchesWhite) FloodRegion(
        Stone[] cells, bool[] visited, int start, int width, int height)
    {
        var region = new List<int>();
        var stack = new Stack<int>();
        bool black = false;
        bool white = false;
        Span<int> neighbours = stackalloc int[4];
        stack.Push(start);
        visited[start] = true;

        while (stack.Count > 0)
        {
            int i = stack.Pop();
            region.Add(i);
            int x = i % width;
            int y = i / width;

            int count = 0;
            if (x > 0)
            {
                neighbours[count++] = i - 1;
            }

            if (x < width - 1)
            {
                neighbours[count++] = i + 1;
            }

            if (y > 0)
            {
                neighbours[count++] = i - width;
            }

            if (y < height - 1)
            {
                neighbours[count++] = i + width;
            }

            for (int k = 0; k < count; k++)
            {
                int n = neighbours[k];
                switch (cells[n])
                {
                    case Stone.Black:
                        black = true;
                        break;
                    case Stone.White:
                        white = true;
                        break;
                    default:
                        if (!visited[n])
                        {
                            visited[n] = true;
                            stack.Push(n);
                        }

                        break;
                }
            }
        }

        return (region, black, white);
    }
}

/// <summary>Result of <see cref="Scoring.Score"/>.</summary>
public sealed class ScoreResult
{
    private readonly Stone[] _owner;
    private readonly int _width;

    internal ScoreResult(Stone[] owner, int width)
    {
        _owner = owner;
        _width = width;
    }

    public required ScoringMethod Method { get; init; }

    public required int BlackTerritory { get; init; }

    public required int WhiteTerritory { get; init; }

    /// <summary>Living black stones on the board.</summary>
    public required int BlackStones { get; init; }

    public required int WhiteStones { get; init; }

    /// <summary>Stones captured by Black during play plus dead white stones.</summary>
    public required int BlackPrisoners { get; init; }

    public required int WhitePrisoners { get; init; }

    public required double Komi { get; init; }

    /// <summary>Extra points for White from handicap stones, as defined by the rule set.</summary>
    public required int HandicapCompensation { get; init; }

    public required double BlackScore { get; init; }

    public required double WhiteScore { get; init; }

    /// <summary>Neutral points, ordered top to bottom, left to right.</summary>
    public required IReadOnlyList<Point> Dame { get; init; }

    /// <summary><see cref="Stone.Empty"/> for jigo.</summary>
    public Stone Winner => BlackScore > WhiteScore ? Stone.Black : WhiteScore > BlackScore ? Stone.White : Stone.Empty;

    public double Margin => Math.Abs(BlackScore - WhiteScore);

    /// <summary>Who owns a point after scoring: living stones, territory (including dead stones) or Empty for dame.</summary>
    public Stone OwnerAt(Point point) => _owner[(point.Y * _width) + point.X];
}
