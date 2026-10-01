namespace Hoshi.Sgf.Joseki;

/// <summary>Spaced-repetition state of one joseki line (Leitner boxes 1…<see cref="Leitner.MaxBox"/>).</summary>
public sealed record JosekiCard(string Id, int Box, DateTimeOffset Due, int Reviews, int Lapses, DateTimeOffset? LastReview)
{
    public static JosekiCard New(string id) => new(id, 1, DateTimeOffset.MinValue, 0, 0, null);

    public bool IsDue(DateTimeOffset now) => Due <= now;

    /// <summary>A perfect run moves the card up one box; any mistake sends it back to box 1.</summary>
    public JosekiCard Review(bool perfect, DateTimeOffset now)
    {
        int box = perfect ? Math.Min(Leitner.MaxBox, Box + 1) : 1;
        return this with
        {
            Box = box,
            Due = now + Leitner.Interval(box),
            Reviews = Reviews + 1,
            Lapses = Lapses + (perfect ? 0 : 1),
            LastReview = now,
        };
    }
}

public static class Leitner
{
    public const int MaxBox = 6;

    /// <summary>How long a card waits in each box: 10 min, 1, 3, 7, 14 and 30 days.</summary>
    public static TimeSpan Interval(int box) => box switch
    {
        <= 1 => TimeSpan.FromMinutes(10),
        2 => TimeSpan.FromDays(1),
        3 => TimeSpan.FromDays(3),
        4 => TimeSpan.FromDays(7),
        5 => TimeSpan.FromDays(14),
        _ => TimeSpan.FromDays(30),
    };

    /// <summary>
    /// The next line to practise: a due card (one of the three most overdue, for variety), else a line never
    /// practised, else null.
    /// </summary>
    public static string? Next(IEnumerable<string> ids, IReadOnlyDictionary<string, JosekiCard> cards, DateTimeOffset now, Random random)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(random);
        List<string> all = [.. ids];
        List<JosekiCard> due = [.. all.Where(cards.ContainsKey).Select(id => cards[id]).Where(c => c.IsDue(now)).OrderBy(c => c.Due).Take(3)];
        if (due.Count > 0)
        {
            return due[random.Next(due.Count)].Id;
        }

        List<string> fresh = [.. all.Where(id => !cards.ContainsKey(id))];
        return fresh.Count > 0 ? fresh[random.Next(fresh.Count)] : null;
    }

    /// <summary>Lines due now, counting never-practised ones.</summary>
    public static int DueCount(IEnumerable<string> ids, IReadOnlyDictionary<string, JosekiCard> cards, DateTimeOffset now) =>
        ids.Count(id => !cards.TryGetValue(id, out JosekiCard? c) || c.IsDue(now));
}
