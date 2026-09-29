using System.Diagnostics.CodeAnalysis;

namespace Hoshi.Core;

public enum KoRule
{
    /// <summary>Only the immediate recapture of a single-stone ko is forbidden.</summary>
    Simple,

    /// <summary>A move may not recreate any earlier board position.</summary>
    PositionalSuperko,

    /// <summary>A move may not recreate an earlier position with the same player to move.</summary>
    SituationalSuperko,
}

public enum ScoringMethod
{
    /// <summary>Territory + prisoners (Japanese, Korean).</summary>
    Territory,

    /// <summary>Stones on the board + territory (Chinese, AGA, New Zealand, Ing).</summary>
    Area,
}

public enum HandicapCompensation
{
    None,

    /// <summary>White receives one point per handicap stone (Chinese).</summary>
    PerStone,

    /// <summary>White receives one point per handicap stone after the first (AGA).</summary>
    PerStoneMinusOne,
}

/// <summary>
/// A rule set. Names match the identifiers used by OGS (<c>japanese</c>, <c>chinese</c>, <c>aga</c>,
/// <c>korean</c>, <c>nz</c>, <c>ing</c>). Ing's special ko rules are approximated with positional superko.
/// </summary>
public sealed record RuleSet(
    string Name,
    KoRule Ko,
    bool AllowSuicide,
    ScoringMethod Scoring,
    double DefaultKomi,
    HandicapCompensation HandicapCompensation)
{
    public static RuleSet Japanese { get; } =
        new("japanese", KoRule.Simple, false, ScoringMethod.Territory, 6.5, HandicapCompensation.None);

    public static RuleSet Korean { get; } =
        new("korean", KoRule.Simple, false, ScoringMethod.Territory, 6.5, HandicapCompensation.None);

    public static RuleSet Chinese { get; } =
        new("chinese", KoRule.PositionalSuperko, false, ScoringMethod.Area, 7.5, HandicapCompensation.PerStone);

    public static RuleSet Aga { get; } =
        new("aga", KoRule.SituationalSuperko, false, ScoringMethod.Area, 7.5, HandicapCompensation.PerStoneMinusOne);

    public static RuleSet NewZealand { get; } =
        new("nz", KoRule.SituationalSuperko, true, ScoringMethod.Area, 7, HandicapCompensation.None);

    public static RuleSet Ing { get; } =
        new("ing", KoRule.PositionalSuperko, true, ScoringMethod.Area, 7.5, HandicapCompensation.PerStone);

    public static IReadOnlyList<RuleSet> All { get; } = [Japanese, Korean, Chinese, Aga, NewZealand, Ing];

    public static RuleSet FromName(string name) =>
        TryFromName(name, out RuleSet? rules)
            ? rules
            : throw new ArgumentException($"Unknown rule set '{name}'.", nameof(name));

    public static bool TryFromName(string? name, [NotNullWhen(true)] out RuleSet? rules)
    {
        rules = All.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        return rules is not null;
    }

    /// <summary>White's handicap compensation, in points, for the given number of handicap stones.</summary>
    public int CompensationFor(int handicapStones) => handicapStones < 2
        ? 0
        : HandicapCompensation switch
        {
            HandicapCompensation.PerStone => handicapStones,
            HandicapCompensation.PerStoneMinusOne => handicapStones - 1,
            _ => 0,
        };
}
