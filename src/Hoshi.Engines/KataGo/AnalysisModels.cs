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
