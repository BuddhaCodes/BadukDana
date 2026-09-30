namespace Hoshi.Core;

/// <summary>Where an estimate comes from.</summary>
public enum EstimateSource
{
    /// <summary>Hoshi's own heuristic (<see cref="TerritoryEstimator"/>): no reading, dead stones are not detected.</summary>
    Heuristic,

    /// <summary>An engine's ownership map (e.g. KataGo), which does read and does detect dead stones.</summary>
    Engine,
}

/// <summary>
/// Who is likely to own each intersection, as a value in [-1, 1] (positive = black), plus the counts derived from
/// it. "Secure" points are practically decided (|ownership| ≥ 0.8); the rest is "potential" (moyo / influence),
/// counted by its expected value. <see cref="Lead"/> is a Japanese-style estimate: territory + captures − komi.
/// </summary>
public sealed class TerritoryEstimate
{
    public const double SecureThreshold = 0.8;

    private readonly double[] _ownership;
    private readonly Stone[] _secure;

    private TerritoryEstimate(BoardState board, double[] ownership, double komi, EstimateSource source)
    {
        Width = board.Width;
        Height = board.Height;
        _ownership = ownership;
        _secure = new Stone[ownership.Length];
        Komi = komi;
        Source = source;
        BlackCaptures = board.CapturesBy(Stone.Black);
        WhiteCaptures = board.CapturesBy(Stone.White);

        for (int i = 0; i < ownership.Length; i++)
        {
            var p = new Point(i % Width, i / Width);
            double o = ownership[i];
            Stone stone = board[p];
            if (stone != Stone.Empty)
            {
                // A stone the map gives to the other side is dead: territory for the owner plus a prisoner.
                bool dead = (stone == Stone.Black && o <= -0.5) || (stone == Stone.White && o >= 0.5);
                if (dead)
                {
                    Stone owner = stone.Opponent();
                    _secure[i] = owner;
                    AddSecure(owner, 1);
                    if (owner == Stone.Black)
                    {
                        DeadWhite++;
                    }
                    else
                    {
                        DeadBlack++;
                    }
                }

                continue;
            }

            if (o >= SecureThreshold)
            {
                _secure[i] = Stone.Black;
                BlackSecure++;
            }
            else if (o <= -SecureThreshold)
            {
                _secure[i] = Stone.White;
                WhiteSecure++;
            }
            else if (o > 0)
            {
                BlackPotential += o;
            }
            else
            {
                WhitePotential -= o;
            }
        }
    }

    public int Width { get; }

    public int Height { get; }

    public EstimateSource Source { get; }

    public int BlackSecure { get; private set; }

    public int WhiteSecure { get; private set; }

    /// <summary>Expected extra points of black from undecided areas.</summary>
    public double BlackPotential { get; }

    public double WhitePotential { get; }

    public int BlackCaptures { get; }

    public int WhiteCaptures { get; }

    /// <summary>Black stones the map considers dead.</summary>
    public int DeadBlack { get; }

    public int DeadWhite { get; }

    public double Komi { get; }

    public double BlackTotal => BlackSecure + BlackPotential + BlackCaptures + DeadWhite;

    public double WhiteTotal => WhiteSecure + WhitePotential + WhiteCaptures + DeadBlack + Komi;

    /// <summary>Estimated margin in points: positive = black ahead.</summary>
    public double Lead => BlackTotal - WhiteTotal;

    public double OwnershipAt(Point p) => _ownership[(p.Y * Width) + p.X];

    /// <summary>The owner of a practically decided empty point (or of a dead stone), else Empty.</summary>
    public Stone SecureOwner(Point p) => _secure[(p.Y * Width) + p.X];

    /// <summary>Builds an estimate from an engine's ownership map (row-major from the top-left, positive = black).</summary>
    public static TerritoryEstimate FromOwnership(BoardState board, IReadOnlyList<double> ownership, double komi)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(ownership);
        if (ownership.Count != board.Width * board.Height)
        {
            throw new ArgumentException($"Expected {board.Width * board.Height} ownership values, got {ownership.Count}.", nameof(ownership));
        }

        return new TerritoryEstimate(board, [.. ownership.Select(o => Math.Clamp(o, -1, 1))], komi, EstimateSource.Engine);
    }

    internal static TerritoryEstimate FromHeuristic(BoardState board, double[] ownership, double komi) =>
        new(board, ownership, komi, EstimateSource.Heuristic);

    private void AddSecure(Stone owner, int n)
    {
        if (owner == Stone.Black)
        {
            BlackSecure += n;
        }
        else
        {
            WhiteSecure += n;
        }
    }
}

/// <summary>
/// A fast, engine-free territory estimate:
/// <list type="bullet">
/// <item>Empty regions bordered by a single colour (and not too large, so an almost empty board is not "owned")
/// are secure territory.</item>
/// <item>Every other empty point gets the stones' influence, decaying exponentially with distance, squashed to
/// (-0.75, 0.75) so it never looks as decided as enclosed territory.</item>
/// </list>
/// It does no reading: dead stones and weak groups are not recognised (an engine's ownership map is).
/// </summary>
public static class TerritoryEstimator
{
    /// <summary>A single-colour region larger than this fraction of the board is influence, not territory.</summary>
    public const double MaxSecureRegionFraction = 1.0 / 3;

    private const double Decay = 1.6;
    private const int Reach = 6;
    private const double Gain = 0.9;

    /// <summary>Influence stays below <see cref="TerritoryEstimate.SecureThreshold"/>: only enclosure makes territory secure.</summary>
    private const double MaxInfluence = 0.75;

    public static TerritoryEstimate Estimate(BoardState board, double komi)
    {
        ArgumentNullException.ThrowIfNull(board);
        int w = board.Width;
        int h = board.Height;
        var ownership = new double[w * h];

        // 1. Influence from every stone.
        var kernel = new double[(2 * Reach) + 1, (2 * Reach) + 1];
        for (int dy = -Reach; dy <= Reach; dy++)
        {
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                double d = Math.Sqrt((dx * dx) + (dy * dy));
                kernel[dx + Reach, dy + Reach] = d > Reach ? 0 : Math.Exp(-d / Decay);
            }
        }

        var raw = new double[w * h];
        foreach (Point s in board.AllPoints)
        {
            Stone stone = board[s];
            if (stone == Stone.Empty)
            {
                continue;
            }

            double sign = stone == Stone.Black ? 1 : -1;
            for (int y = Math.Max(0, s.Y - Reach); y <= Math.Min(h - 1, s.Y + Reach); y++)
            {
                for (int x = Math.Max(0, s.X - Reach); x <= Math.Min(w - 1, s.X + Reach); x++)
                {
                    raw[(y * w) + x] += sign * kernel[x - s.X + Reach, y - s.Y + Reach];
                }
            }
        }

        for (int i = 0; i < raw.Length; i++)
        {
            var p = new Point(i % w, i / w);
            ownership[i] = board[p] switch
            {
                Stone.Black => 1,
                Stone.White => -1,
                _ => MaxInfluence * Math.Tanh(Gain * raw[i]),
            };
        }

        // 2. Enclosed single-colour regions are secure.
        var seen = new bool[w * h];
        int maxRegion = (int)(w * h * MaxSecureRegionFraction);
        var region = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < w * h; start++)
        {
            if (seen[start] || board[new Point(start % w, start / w)] != Stone.Empty)
            {
                continue;
            }

            region.Clear();
            bool touchesBlack = false;
            bool touchesWhite = false;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                region.Add(i);
                foreach (Point n in board.Neighbors(new Point(i % w, i / w)))
                {
                    int j = (n.Y * w) + n.X;
                    Stone s = board[n];
                    if (s == Stone.Black)
                    {
                        touchesBlack = true;
                    }
                    else if (s == Stone.White)
                    {
                        touchesWhite = true;
                    }
                    else if (!seen[j])
                    {
                        seen[j] = true;
                        stack.Push(j);
                    }
                }
            }

            if (touchesBlack != touchesWhite && region.Count <= maxRegion)
            {
                double owner = touchesBlack ? 1 : -1;
                foreach (int i in region)
                {
                    ownership[i] = owner;
                }
            }
        }

        return TerritoryEstimate.FromHeuristic(board, ownership, komi);
    }
}
