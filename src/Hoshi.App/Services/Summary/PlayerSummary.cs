using System.Text.Json;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.Services.Summary;

/// <summary>
/// One colour's moves in a reviewed game: how many KataGo judged, how many were good, how many its first choice, and
/// the moves and good moves in each phase (<see cref="GamePhases"/>; zero in reviews saved before 2026-11).
/// </summary>
public sealed record ReviewSide(string Name, int Moves, int Good, int Best,
    int OpeningMoves = 0, int OpeningGood = 0, int MiddleMoves = 0, int MiddleGood = 0, int EndMoves = 0, int EndGood = 0)
{
    public (int Moves, int Good) In(GamePhase phase) => phase switch
    {
        GamePhase.Opening => (OpeningMoves, OpeningGood),
        GamePhase.Middle => (MiddleMoves, MiddleGood),
        _ => (EndMoves, EndGood),
    };
}

/// <summary>A game KataGo reviewed ("Write KataGo's review into the game"), kept for the summaries.</summary>
public sealed record ReviewRecord(string Key, DateTimeOffset Played, DateTimeOffset Reviewed, ReviewSide Black, ReviewSide White);

/// <summary>
/// The reviews written into games, kept in <c>reviews.json</c> in the data folder so the month and year summaries
/// can tell the accuracy. Only counts are stored (no positions); nothing leaves the computer.
/// </summary>
public sealed class ReviewLog
{
    private readonly string? _file;
    private readonly Dictionary<string, ReviewRecord> _records;

    public ReviewLog(string? dataDirectory = null)
    {
        _file = dataDirectory is null ? null : Path.Combine(dataDirectory, "reviews.json");
        _records = Load(_file);
    }

    public IReadOnlyCollection<ReviewRecord> Records => _records.Values;

    /// <summary>Records (or replaces) the review of a game: moves judged good are Best, Excellent and Good.</summary>
    public ReviewRecord? Record(GameTree tree, IReadOnlyDictionary<GameNode, StudyVerdict> verdicts, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(verdicts);
        if (verdicts.Count == 0)
        {
            return null;
        }

        int size = Math.Max(tree.Info.Width, tree.Info.Height);
        int[] moves = new int[2], good = new int[2], best = new int[2];
        int[,] phaseMoves = new int[2, 3], phaseGood = new int[2, 3];
        Dictionary<GameNode, int> numbers = MoveNumbers(tree);
        foreach ((GameNode node, StudyVerdict v) in verdicts)
        {
            if (node.GetMove(size) is not { } move)
            {
                continue;
            }

            int side = move.Color == Stone.White ? 1 : 0;
            int phase = numbers.TryGetValue(node, out int number) ? (int)GamePhases.Of(number, Math.Max(size, 2)) : (int)GamePhase.Middle;
            moves[side]++;
            phaseMoves[side, phase]++;
            if (v.Quality is MoveQuality.Best or MoveQuality.Excellent or MoveQuality.Good)
            {
                good[side]++;
                phaseGood[side, phase]++;
            }

            if (v.Quality == MoveQuality.Best)
            {
                best[side]++;
            }
        }

        var record = new ReviewRecord(
            Key(tree),
            PlayedOn(tree) ?? now,
            now,
            Side(tree.Info.BlackPlayer, 0),
            Side(tree.Info.WhitePlayer, 1));
        _records[record.Key] = record;
        Save();
        return record;

        ReviewSide Side(string? name, int s) => new(name ?? string.Empty, moves[s], good[s], best[s],
            phaseMoves[s, 0], phaseGood[s, 0], phaseMoves[s, 1], phaseGood[s, 1], phaseMoves[s, 2], phaseGood[s, 2]);
    }

    /// <summary>Move numbers along every line (a variation's moves count from the root too).</summary>
    private static Dictionary<GameNode, int> MoveNumbers(GameTree tree)
    {
        var numbers = new Dictionary<GameNode, int>();
        var stack = new Stack<(GameNode Node, int Number)>();
        stack.Push((tree.Root, 0));
        while (stack.Count > 0)
        {
            (GameNode node, int number) = stack.Pop();
            int here = node.GetValue("B") is not null || node.GetValue("W") is not null ? number + 1 : number;
            numbers[node] = here;
            foreach (GameNode child in node.Children)
            {
                stack.Push((child, here));
            }
        }

        return numbers;
    }

    /// <summary>The same game gives the same key: players, date and the first moves.</summary>
    public static string Key(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var moves = new List<string>();
        for (GameNode n = tree.Root; n.Children.Count > 0 && moves.Count < 30;)
        {
            n = n.Children[0];
            moves.Add(n.GetValue("B") ?? n.GetValue("W") ?? string.Empty);
        }

        return string.Join('|', tree.Info.BlackPlayer, tree.Info.WhitePlayer, tree.Info.Date, string.Concat(moves));
    }

    /// <summary>The first date of the SGF <c>DT</c> (e.g. "2026-10-03" or "2026-10-03,04"), or null.</summary>
    public static DateTimeOffset? PlayedOn(GameTree tree)
    {
        string? date = tree?.Info.Date?.Split(',')[0].Trim();
        return date is { Length: >= 10 } && DateTimeOffset.TryParseExact(date[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out DateTimeOffset d) ? d : null;
    }

    private static Dictionary<string, ReviewRecord> Load(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                return (JsonSerializer.Deserialize<List<ReviewRecord>>(File.ReadAllText(file)) ?? []).ToDictionary(r => r.Key);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            // A damaged file starts over.
        }

        return [];
    }

    private void Save()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_records.Values.ToList()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The summary is a convenience.
        }
    }
}

/// <summary>A month or a year of Go, worked out on this computer.</summary>
public sealed record PlayerSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    string? Player,
    int Games,
    int OnlineGames,
    int Wins,
    int Losses,
    int ReviewedGames,
    double? Accuracy,
    int BestMoves,
    int JosekiReviewed,
    int JosekiLearned,
    int ProblemsSolved)
{
    /// <summary>Good moves per KataGo in the opening, middle game and endgame (null where too few moves were judged).</summary>
    public IReadOnlyDictionary<GamePhase, double> PhaseAccuracy { get; init; } = new Dictionary<GamePhase, double>();

    /// <summary>The phase clearly behind the others (at least 5 points below the best), worth studying; null if none.</summary>
    public GamePhase? ToStudy { get; init; }

    /// <summary>Fewer judged moves than this in a phase and its accuracy is not shown (too noisy).</summary>
    public const int MinPhaseMoves = 10;

    /// <summary>A card in box 3 or higher counts as learned (seen right several times in a row).</summary>
    public const int LearnedBox = 3;

    /// <summary>
    /// Builds the summary. The player is <paramref name="me"/> when given (the study author name), else the name in
    /// most of the saved games; their results and accuracy are counted, or nothing when no name is known.
    /// </summary>
    public static PlayerSummary Build(
        DateTimeOffset from,
        DateTimeOffset to,
        IEnumerable<ReplayEntry> games,
        IEnumerable<ReviewRecord> reviews,
        IEnumerable<JosekiCard> joseki,
        IEnumerable<Tsumego.TsumegoCard> problems,
        string? me = null)
    {
        var all = games.ToList();
        string? player = string.IsNullOrWhiteSpace(me) ? MostCommonName(all) : me.Trim();
        var inPeriod = all.Where(g => g.Played >= from && g.Played < to).ToList();
        int wins = 0, losses = 0;
        foreach (ReplayEntry g in inPeriod)
        {
            if (player is null || g.Result is not { Length: > 1 } result || result[1] != '+')
            {
                continue;
            }

            bool black = Same(g.Black, player), white = Same(g.White, player);
            if (black == white)
            {
                continue;
            }

            bool blackWon = char.ToUpperInvariant(result[0]) == 'B';
            if (black == blackWon)
            {
                wins++;
            }
            else
            {
                losses++;
            }
        }

        var reviewed = reviews.Where(r => r.Played >= from && r.Played < to).ToList();
        int moves = 0, good = 0, best = 0;
        var phaseMoves = new Dictionary<GamePhase, int>();
        var phaseGood = new Dictionary<GamePhase, int>();
        foreach (ReviewRecord r in reviewed)
        {
            foreach (ReviewSide side in new[] { r.Black, r.White })
            {
                if (player is null || Same(side.Name, player))
                {
                    moves += side.Moves;
                    good += side.Good;
                    best += side.Best;
                    foreach (GamePhase phase in Enum.GetValues<GamePhase>())
                    {
                        (int m, int g) = side.In(phase);
                        phaseMoves[phase] = phaseMoves.GetValueOrDefault(phase) + m;
                        phaseGood[phase] = phaseGood.GetValueOrDefault(phase) + g;
                    }
                }
            }
        }

        var accuracy = phaseMoves.Where(p => p.Value >= MinPhaseMoves).ToDictionary(p => p.Key, p => (double)phaseGood[p.Key] / p.Value);
        GamePhase? toStudy = null;
        if (accuracy.Count >= 2)
        {
            KeyValuePair<GamePhase, double> worst = accuracy.MinBy(p => p.Value);
            if (accuracy.Values.Max() - worst.Value >= 0.05)
            {
                toStudy = worst.Key;
            }
        }

        var cards = joseki.Where(c => c.LastReview is { } t && t >= from && t < to).ToList();
        return new PlayerSummary(
            from,
            to,
            player,
            inPeriod.Count,
            inPeriod.Count(g => g.Source == "ogs"),
            wins,
            losses,
            reviewed.Count,
            moves > 0 ? (double)good / moves : null,
            best,
            cards.Count,
            cards.Count(c => c.Box >= LearnedBox),
            problems.Count(p => p.LastSolved is { } t && t >= from && t < to))
        {
            PhaseAccuracy = accuracy,
            ToStudy = toStudy,
        };
    }

    private static bool Same(string? a, string b) => string.Equals(a?.Trim(), b, StringComparison.OrdinalIgnoreCase);

    // The user plays in every game they keep: the name that appears in most of them (at least two).
    private static string? MostCommonName(IReadOnlyList<ReplayEntry> games) =>
        games.SelectMany(g => new[] { g.Black, g.White })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .GroupBy(n => n.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .FirstOrDefault(g => g.Count >= 2 && g.Count * 2 > games.Count)
            .Name;
}

/// <summary>What the summaries read: the game library, the reviews, the joseki and problem progress.</summary>
public sealed class SummarySource(
    IReplayStore? replays,
    ReviewLog reviews,
    Joseki.IJosekiLibrary? joseki = null,
    Tsumego.TsumegoLibrary? problems = null,
    ISettingsService? settings = null)
{
    public ReviewLog Reviews { get; } = reviews ?? throw new ArgumentNullException(nameof(reviews));

    public PlayerSummary Build(DateTimeOffset from, DateTimeOffset to) => PlayerSummary.Build(
        from,
        to,
        [.. (replays?.List() ?? []).Where(e => e.Source != ReplayStore.PhotoSource)], // photos are positions, not games played
        Reviews.Records,
        joseki?.Cards.Values ?? [],
        problems?.Cards.Values ?? [],
        settings?.Current.StudyAuthor);
}
