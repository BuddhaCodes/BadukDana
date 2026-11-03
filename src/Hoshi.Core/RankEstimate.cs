using System.Globalization;

namespace Hoshi.Core;

/// <summary>
/// The player's strength as Go ranks on one line: 1k = 0, 2k = −1 … 20k = −19, 1d = 1, 5d = 5 (kyu k ↦ 1 − k).
/// Updated after each game against a rival of known strength (KataGo's human-style profiles), Elo-like: a win
/// against someone stronger moves it more than one against someone weaker, and the first games move it most.
/// One rank stronger ≈ 70 % wins, as handicap tradition has it.
/// </summary>
public sealed record RankEstimate(double Rating, int Games)
{
    /// <summary>The logistic slope: 1 rank apart → 70 % expected for the stronger side.</summary>
    public const double Slope = 0.847;

    /// <summary>Uncertainty in ranks: wide at first, about one rank after 25 games.</summary>
    public double Uncertainty => Math.Max(1, 5 / Math.Sqrt(Games + 1));

    /// <summary>The probability of beating a rival of rank value <paramref name="opponent"/>.</summary>
    public double Expected(double opponent) => 1 / (1 + Math.Exp(-Slope * (Rating - opponent)));

    /// <summary>After a game: <paramref name="score"/> 1 for a win, 0 for a loss, 0.5 for jigo.</summary>
    public RankEstimate After(double opponent, double score)
    {
        double k = Math.Max(0.6, 3 / Math.Sqrt(Games + 1));
        double rating = Math.Clamp(Rating + (k * (score - Expected(opponent))), -29, 9);
        return new RankEstimate(rating, Games + 1);
    }

    /// <summary>"8k", "2d" (nearest rank).</summary>
    public string Label => RankEstimates.Label(Rating);
}

public static class RankEstimates
{
    /// <summary>Rank value of "15k" / "3d"; null when the text is not a rank.</summary>
    public static double? Parse(string? rank)
    {
        if (rank is not { Length: >= 2 } r || !int.TryParse(r[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 1)
        {
            return null;
        }

        return char.ToLowerInvariant(r[^1]) switch
        {
            'k' => 1 - n,
            'd' => n,
            _ => null,
        };
    }

    public static string Label(double value)
    {
        int v = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return v >= 1 ? $"{v}d" : $"{1 - v}k";
    }

    /// <summary>How many ranks one handicap stone is worth on a board of this size (smaller boards: more).</summary>
    public static double RanksPerStone(int size) => size switch
    {
        <= 9 => 4,
        <= 13 => 2,
        _ => 1,
    };

    /// <summary>
    /// The strength the player effectively met: the rival's rank, minus what the handicap gave the player (Black with
    /// <paramref name="handicap"/> stones) or plus what it gave the rival (the player took White).
    /// </summary>
    public static double Opponent(double rivalRank, int handicap, int size, bool playerIsBlack)
    {
        double stones = handicap >= 2 ? handicap * RanksPerStone(size) : 0;
        return playerIsBlack ? rivalRank - stones : rivalRank + stones;
    }

    /// <summary>
    /// "Rival at your level": the closest rank in <paramref name="levels"/> at or above the player, with handicap
    /// stones (player Black) making up the gap, so each game is near 50 %.
    /// </summary>
    public static (string Level, int Handicap) RivalFor(RankEstimate player, IReadOnlyList<string> levels, int size)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(levels);
        var ranked = levels.Select(l => (Level: l, Value: Parse(l) ?? 0)).OrderBy(l => l.Value).ToList();
        (string Level, double Value) rival = ranked.FirstOrDefault(l => l.Value >= player.Rating - 0.5);
        if (rival.Level is null)
        {
            return (ranked[^1].Level, 0); // stronger than every profile: an even game against the strongest
        }

        int stones = (int)Math.Round((rival.Value - player.Rating) / RanksPerStone(size), MidpointRounding.AwayFromZero);
        int max = Handicap.MaxFixed(size);
        int handicap = stones < 2 ? 0 : Math.Min(stones, max);
        return (rival.Level, handicap);
    }
}
