using System.Text.Json;
using Hoshi.Core;
using Hoshi.Sgf;
using Hoshi.Sgf.Joseki;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Joseki;

/// <summary>The joseki lines the trainer knows, and the spaced-repetition progress on each.</summary>
public interface IJosekiLibrary
{
    event EventHandler? Changed;

    /// <summary>Folder with the user's joseki SGF files (imported or recorded).</summary>
    string UserDirectory { get; }

    IReadOnlyList<JosekiLine> Lines { get; }

    IReadOnlyDictionary<string, JosekiCard> Cards { get; }

    /// <summary>Copies an SGF file of joseki into the library; returns how many new lines it brought.</summary>
    int Import(string sgfPath);

    /// <summary>
    /// Adds one sequence to <paramref name="fileName"/> (default <c>my-lines.sgf</c>, merged with common prefixes);
    /// null when it is too short or already known.
    /// </summary>
    JosekiLine? AddLine(IReadOnlyList<JosekiMove> moves, int size, string name, string? comment = null, string? fileName = null);

    void Record(JosekiCard card);
}

/// <summary>
/// Lines come from the starter file shipped with Hoshi plus every <c>*.sgf</c> in <c>joseki/</c> under the data
/// folder; progress is kept in <c>joseki/progress.json</c>.
/// </summary>
public sealed class JosekiLibrary : IJosekiLibrary
{
    public const string MyLinesFile = "my-lines.sgf";

    /// <summary>Lines fetched from the OGS Joseki Explorer for practice (a personal cache, never shipped).</summary>
    public const string OgsLinesFile = "ogs-explorer.sgf";

    private const string StarterResource = "Hoshi.Joseki.starter.sgf";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly bool _includeStarter;
    private IReadOnlyList<JosekiLine>? _lines;
    private Dictionary<string, JosekiCard>? _cards;

    public JosekiLibrary(ILogger<JosekiLibrary>? logger = null, string? dataDirectory = null, bool includeStarter = true)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _includeStarter = includeStarter;
        UserDirectory = Path.Combine(dataDirectory ?? AppPaths.DataDirectory, "joseki");
    }

    public event EventHandler? Changed;

    public string UserDirectory { get; }

    private string ProgressPath => Path.Combine(UserDirectory, "progress.json");

    public IReadOnlyList<JosekiLine> Lines
    {
        get
        {
            lock (_gate)
            {
                return _lines ??= Load();
            }
        }
    }

    public IReadOnlyDictionary<string, JosekiCard> Cards
    {
        get
        {
            lock (_gate)
            {
                return new Dictionary<string, JosekiCard>(_cards ??= LoadCards());
            }
        }
    }

    public int Import(string sgfPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sgfPath);
        byte[] data = File.ReadAllBytes(sgfPath);
        string collection = Path.GetFileNameWithoutExtension(sgfPath);
        List<JosekiLine> found = [.. SgfParser.ParseCollection(data).Games.SelectMany(g => JosekiLibraryReader.ExtractLines(g, collection))];
        if (found.Count == 0)
        {
            throw new FormatException(Core.Localization.Tr.T("Joseki.NoLinesInFile"));
        }

        int added;
        lock (_gate)
        {
            HashSet<string> known = [.. (_lines ??= Load()).Select(l => l.Id)];
            added = found.Count(l => !known.Contains(l.Id));
            Directory.CreateDirectory(UserDirectory);
            string target = Path.Combine(UserDirectory, Path.GetFileName(sgfPath));
            for (int i = 2; File.Exists(target); i++)
            {
                target = Path.Combine(UserDirectory, $"{collection} ({i}).sgf");
            }

            File.WriteAllBytes(target, data);
            _lines = null;
        }

        _logger.LogInformation("Imported {Count} joseki lines ({New} new)", found.Count, added);
        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    public JosekiLine? AddLine(IReadOnlyList<JosekiMove> moves, int size, string name, string? comment = null, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(moves);
        if (moves.Count < 2)
        {
            return null;
        }

        string id = JosekiLine.CanonicalId(moves, size);
        lock (_gate)
        {
            if ((_lines ??= Load()).Any(l => l.Id == id))
            {
                return null;
            }

            Directory.CreateDirectory(UserDirectory);
            string file = string.IsNullOrWhiteSpace(fileName) ? MyLinesFile : Path.GetFileName(fileName);
            string path = Path.Combine(UserDirectory, file);
            GameTree tree = File.Exists(path) ? SgfParser.Parse(File.ReadAllBytes(path)) : GameTree.Create(size);
            if (tree.Info.Width != size)
            {
                path = Path.Combine(UserDirectory, $"{Path.GetFileNameWithoutExtension(file)}-{size}.sgf");
                tree = File.Exists(path) ? SgfParser.Parse(File.ReadAllBytes(path)) : GameTree.Create(size);
            }

            GameNode node = tree.Root;
            foreach (JosekiMove move in moves)
            {
                string prop = move.Color == Stone.Black ? "B" : "W";
                string value = move.Point.ToSgf();
                GameNode? next = node.Children.FirstOrDefault(c => c.GetValue(prop) == value);
                if (next is null)
                {
                    next = node.AddChild();
                    next.SetValue(prop, value);
                }

                node = next;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                node.SetValue("N", name.Trim());
            }

            if (!string.IsNullOrWhiteSpace(comment))
            {
                node.Comment = comment.Trim();
            }

            File.WriteAllBytes(path, SgfWriter.WriteBytes(tree));
            _lines = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Lines.FirstOrDefault(l => l.Id == id);
    }

    public void Record(JosekiCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        lock (_gate)
        {
            _cards ??= LoadCards();
            _cards[card.Id] = card;
            try
            {
                Directory.CreateDirectory(UserDirectory);
                string temp = ProgressPath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_cards.Values.OrderBy(c => c.Id, StringComparer.Ordinal), Json));
                File.Move(temp, ProgressPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not save joseki progress");
            }
        }
    }

    private List<JosekiLine> Load()
    {
        var lines = new List<JosekiLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void AddFrom(byte[] data, string collection)
        {
            foreach (GameTree game in SgfParser.ParseCollection(data).Games)
            {
                lines.AddRange(JosekiLibraryReader.ExtractLines(game, collection).Where(l => seen.Add(l.Id)));
            }
        }

        if (_includeStarter && typeof(JosekiLibrary).Assembly.GetManifestResourceStream(StarterResource) is { } stream)
        {
            using (stream)
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                AddFrom(ms.ToArray(), "Hoshi");
            }
        }

        if (Directory.Exists(UserDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(UserDirectory, "*.sgf").Order(StringComparer.Ordinal))
            {
                try
                {
                    AddFrom(File.ReadAllBytes(file), Path.GetFileNameWithoutExtension(file));
                }
                catch (Exception ex) when (ex is IOException or FormatException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Skipping joseki file {File}", Path.GetFileName(file));
                }
            }
        }

        return lines;
    }

    private Dictionary<string, JosekiCard> LoadCards()
    {
        try
        {
            if (File.Exists(ProgressPath))
            {
                List<JosekiCard> list = JsonSerializer.Deserialize<List<JosekiCard>>(File.ReadAllText(ProgressPath), Json) ?? [];
                return list.Where(c => !string.IsNullOrEmpty(c.Id)).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Last());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Joseki progress unreadable; starting over");
        }

        return [];
    }
}

/// <summary>An empty library that keeps nothing (designer, tests, or no data folder).</summary>
public sealed class NullJosekiLibrary : IJosekiLibrary
{
    public static NullJosekiLibrary Instance { get; } = new();

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public string UserDirectory => string.Empty;

    public IReadOnlyList<JosekiLine> Lines => [];

    public IReadOnlyDictionary<string, JosekiCard> Cards => new Dictionary<string, JosekiCard>();

    public int Import(string sgfPath) => throw new FormatException(Core.Localization.Tr.T("Joseki.NoLinesInFile"));

    public JosekiLine? AddLine(IReadOnlyList<JosekiMove> moves, int size, string name, string? comment = null, string? fileName = null) => null;

    public void Record(JosekiCard card)
    {
    }
}
