using System.Text.Json;
using Avalonia.Platform;
using Hoshi.Sgf;
using Hoshi.Sgf.Problems;

namespace Hoshi.App.Services.Tsumego;

/// <summary>Where a problem came from: Hoshi's own set, or a collection the player opened.</summary>
public sealed record TsumegoSource(string Name, IReadOnlyList<TsumegoProblem> Problems);

/// <summary>How a problem is going for the player (Leitner box 0–5, next review, counts).</summary>
public sealed record TsumegoCard(int Box, DateTimeOffset Due, int Solved, int Failed);

/// <summary>
/// The problems and the player's progress. Hoshi ships a small set it wrote itself (no third-party collections:
/// their licences vary); the player can open any SGF problem collection. Progress is kept in the data folder.
/// </summary>
public sealed class TsumegoLibrary
{
    /// <summary>Days until the next review for each box (a right answer moves a problem up a box).</summary>
    public static readonly int[] Intervals = [0, 1, 3, 7, 21, 60];

    public const string BuiltInUri = "avares://Hoshi.App/Assets/Tsumego/hoshi-basics.sgf";

    private readonly string? _progressFile;
    private readonly Dictionary<string, TsumegoCard> _cards;

    public TsumegoLibrary(string? dataDirectory = null)
    {
        _progressFile = dataDirectory is null ? null : Path.Combine(dataDirectory, "tsumego-progress.json");
        _cards = Load(_progressFile);
    }

    public IReadOnlyDictionary<string, TsumegoCard> Cards => _cards;

    /// <summary>Hoshi's own problems (an empty list when the asset cannot be read, e.g. without Avalonia).</summary>
    public static TsumegoSource BuiltIn()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri(BuiltInUri));
            using var reader = new StreamReader(stream);
            return Parse("Hoshi", reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FileNotFoundException)
        {
            return new TsumegoSource("Hoshi", []);
        }
    }

    /// <summary>Every game of an SGF collection that has a position and an answer is a problem.</summary>
    public static TsumegoSource Parse(string name, string sgf)
    {
        SgfParseResult parsed = SgfParser.ParseCollection(sgf);
        return new TsumegoSource(name, [.. parsed.Games.Where(g => g.Root.Children.Count > 0).Select(TsumegoProblem.FromTree)]);
    }

    /// <summary>Records an attempt: right moves the problem up a box, wrong back to the first.</summary>
    public TsumegoCard Record(TsumegoProblem problem, bool solved, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(problem);
        TsumegoCard old = _cards.GetValueOrDefault(problem.Id) ?? new TsumegoCard(0, now, 0, 0);
        int box = solved ? Math.Min(old.Box + 1, Intervals.Length - 1) : 0;
        var card = new TsumegoCard(box, now.AddDays(Intervals[box]), old.Solved + (solved ? 1 : 0), old.Failed + (solved ? 0 : 1));
        _cards[problem.Id] = card;
        Save();
        return card;
    }

    /// <summary>The next problem to work on: the oldest due one, else one never tried, else the next in order.</summary>
    public TsumegoProblem? Next(IReadOnlyList<TsumegoProblem> problems, TsumegoProblem? after, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(problems);
        if (problems.Count == 0)
        {
            return null;
        }

        int start = after is null ? 0 : (IndexOf(problems, after) + 1) % problems.Count;
        IEnumerable<TsumegoProblem> ordered = problems.Skip(start).Concat(problems.Take(start)).Where(p => !ReferenceEquals(p, after));
        TsumegoProblem? due = ordered
            .Where(p => _cards.TryGetValue(p.Id, out TsumegoCard? c) && c.Due <= now)
            .OrderBy(p => _cards[p.Id].Due)
            .FirstOrDefault();
        return due ?? ordered.FirstOrDefault(p => !_cards.ContainsKey(p.Id)) ?? ordered.FirstOrDefault() ?? after;
    }

    private static int IndexOf(IReadOnlyList<TsumegoProblem> problems, TsumegoProblem p)
    {
        for (int i = 0; i < problems.Count; i++)
        {
            if (ReferenceEquals(problems[i], p))
            {
                return i;
            }
        }

        return -1;
    }

    private static Dictionary<string, TsumegoCard> Load(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                return JsonSerializer.Deserialize<Dictionary<string, TsumegoCard>>(File.ReadAllText(file)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file starts over.
        }

        return [];
    }

    private void Save()
    {
        if (_progressFile is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_progressFile)!);
            File.WriteAllText(_progressFile, JsonSerializer.Serialize(_cards));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Progress is a convenience; the trainer works without it.
        }
    }
}
