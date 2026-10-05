using Hoshi.Core;

namespace Hoshi.Engines.KataGo;

/// <summary>A move of the game sent to the engine (null point = pass).</summary>
public sealed record EngineMove(Stone Color, Point? Point);

/// <summary>A position (and the moves that led to it) to analyse at the given turns (0 = before the first move).</summary>
public sealed record AnalysisQuery
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public RuleSet Rules { get; init; } = RuleSet.Japanese;

    public double Komi { get; init; } = 6.5;

    public IReadOnlyList<(Point Point, Stone Stone)> InitialStones { get; init; } = [];

    public Stone InitialPlayer { get; init; } = Stone.Black;

    public IReadOnlyList<EngineMove> Moves { get; init; } = [];

    /// <summary>Turns to analyse; empty = only the final position.</summary>
    public IReadOnlyList<int> Turns { get; init; } = [];

    public int MaxVisits { get; init; } = 200;

    public bool IncludeOwnership { get; init; } = true;

    /// <summary>KataGo serves queries with a higher priority first (e.g. the shown position before the game graph).</summary>
    public int Priority { get; init; }

    /// <summary>When set, KataGo also reports partial results every this many seconds while it searches.</summary>
    public double? ReportDuringSearchEvery { get; init; }
}

/// <summary>A candidate move. Winrate and score are from black's point of view.</summary>
public sealed record MoveCandidate(Point? Point, int Order, int Visits, double Winrate, double ScoreLead, double Prior, IReadOnlyList<Point?> Pv);

/// <summary>The engine's view of one turn. Winrate and score are from black's point of view.</summary>
public sealed record TurnAnalysis(
    int Turn,
    Stone ToMove,
    double Winrate,
    double ScoreLead,
    int Visits,
    IReadOnlyList<MoveCandidate> Candidates,
    IReadOnlyList<double>? Ownership)
{
    public MoveCandidate? Best => Candidates.Count > 0 ? Candidates[0] : null;
}

public enum MoveQuality
{
    /// <summary>The engine's own first choice.</summary>
    Best,

    /// <summary>Loses at most half a point.</summary>
    Excellent,

    /// <summary>Loses at most 1.5 points.</summary>
    Good,

    /// <summary>Loses at most 3 points.</summary>
    Inaccuracy,

    /// <summary>Loses at most 6 points.</summary>
    Mistake,

    /// <summary>Loses more than 6 points.</summary>
    Blunder,
}

/// <summary>How a played move compares with the engine's choices.</summary>
public sealed record MoveAssessment(MoveQuality Quality, double PointsLost, double WinrateLost, int? Rank, MoveCandidate Best, EngineMove Played);

/// <summary>Compares a played move with the analysis of the position before it.</summary>
public static class MoveReview
{
    /// <param name="before">Analysis of the position where the move was played.</param>
    /// <param name="after">Analysis after the move (used when the move is not among the candidates).</param>
    public static MoveAssessment? Assess(TurnAnalysis before, TurnAnalysis? after, EngineMove played)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(played);
        if (before.Best is not { } best)
        {
            return null;
        }

        double sign = played.Color == Stone.Black ? 1 : -1;
        MoveCandidate? same = before.Candidates.FirstOrDefault(c => c.Point == played.Point);
        double playedLead;
        double playedWinrate;
        if (same is not null)
        {
            playedLead = same.ScoreLead;
            playedWinrate = same.Winrate;
        }
        else if (after is not null)
        {
            playedLead = after.ScoreLead;
            playedWinrate = after.Winrate;
        }
        else
        {
            return null;
        }

        double lost = Math.Max(0, (best.ScoreLead - playedLead) * sign);
        double winLost = Math.Max(0, (best.Winrate - playedWinrate) * sign);
        int? rank = same?.Order;
        MoveQuality quality = rank == 0 ? MoveQuality.Best
            : lost <= 0.5 ? MoveQuality.Excellent
            : lost <= 1.5 ? MoveQuality.Good
            : lost <= 3 ? MoveQuality.Inaccuracy
            : lost <= 6 ? MoveQuality.Mistake
            : MoveQuality.Blunder;
        return new MoveAssessment(quality, lost, winLost, rank, best, played);
    }
}

/// <summary>
/// How much a move matters, for celebrations: a strong move in a quiet opening is routine; a strong move where the
/// alternatives lose a lot, or in the middle of a fight, is a turning point. Combines the move's quality, the
/// position's criticality (points between the engine's best move and its typical alternatives) and the fight
/// around the move (from the board alone).
/// </summary>
public static class MoveImportance
{
    /// <summary>Candidates (after the best) averaged as "the typical alternative".</summary>
    public const int Alternatives = 4;

    /// <summary>
    /// Points the position demanded: the best candidate's lead over the average of the next few candidates, from the
    /// mover's side (0 when the engine saw only one candidate, i.e. nothing to compare).
    /// </summary>
    public static double Criticality(TurnAnalysis before, Stone mover)
    {
        ArgumentNullException.ThrowIfNull(before);
        if (before.Best is not { } best)
        {
            return 0;
        }

        MoveCandidate[] others = [.. before.Candidates.Where(c => c != best && c.Visits > 0).OrderBy(c => c.Order).Take(Alternatives)];
        if (others.Length == 0)
        {
            return 0;
        }

        double sign = mover == Stone.White ? -1 : 1;
        return Math.Max(0, (best.ScoreLead - others.Average(c => c.ScoreLead)) * sign);
    }

    /// <summary>The opening: the first moves of the game, about 1/18 of the board (20 moves on 19×19).</summary>
    public static bool IsOpening(int moveNumber, int width, int height) => moveNumber <= Math.Max(6, width * height / 18);

    /// <summary>
    /// Celebration strength 0–3 (0 = only the stone's own sound): never above the move's quality (best 3,
    /// excellent 2, good 1), and only as high as the moment deserves.
    /// </summary>
    /// <param name="fightIntensity">The board's fight reading for the move (contact, short liberties, captures; 0–6).</param>
    public static int Strength(MoveQuality quality, double criticality, double fightIntensity, bool opening)
    {
        int byQuality = quality switch
        {
            MoveQuality.Best => 3,
            MoveQuality.Excellent => 2,
            MoveQuality.Good => 1,
            _ => 0,
        };
        if (byQuality == 0)
        {
            return 0;
        }

        // A quiet opening move is routine, however good: no fight, nothing much at stake.
        if (opening && fightIntensity < 1.5 && criticality < 4)
        {
            return 0;
        }

        double score = criticality + fightIntensity;
        int byMoment = score >= 8 ? 3 : score >= 4.5 ? 2 : score >= 2.5 ? 1 : 0;
        return Math.Min(byQuality, byMoment);
    }
}
