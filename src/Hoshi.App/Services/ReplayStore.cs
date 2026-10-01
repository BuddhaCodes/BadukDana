using System.Globalization;
using System.Text.Json;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services;

/// <summary>A game kept in the local replay library.</summary>
/// <param name="Source">"local" or "ogs".</param>
public sealed record ReplayEntry(
    string Id,
    string FileName,
    DateTimeOffset Played,
    string Black,
    string White,
    string? Result,
    int Size,
    int Moves,
    string Source,
    long? OgsGameId);

/// <summary>The library of played games (SGF files plus a small index), for reviewing them later with the AI.</summary>
public interface IReplayStore
{
    event EventHandler? Changed;

    IReadOnlyList<ReplayEntry> List();

    /// <summary>Saves (or, with the same id, updates) a game; games shorter than <see cref="ReplayStore.MinMoves"/> are skipped.</summary>
    ReplayEntry? Save(GameTree tree, string id, string source, long? ogsGameId = null);

    GameTree Load(ReplayEntry entry);

    void Delete(ReplayEntry entry);
}

/// <summary>
/// Keeps played games in <c>replays/</c> under the data folder: one SGF per game and <c>index.json</c> with the
/// list. Local games are saved when they are replaced or the app closes; OGS games when they end.
/// </summary>
public sealed class ReplayStore : IReplayStore
{
    public const int MinMoves = 10;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly string _directory;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    public ReplayStore(ILogger<ReplayStore>? logger = null, string? dataDirectory = null)
    {
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _directory = Path.Combine(dataDirectory ?? AppPaths.DataDirectory, "replays");
    }

    public event EventHandler? Changed;

    private string IndexPath => Path.Combine(_directory, "index.json");

    public IReadOnlyList<ReplayEntry> List()
    {
        lock (_gate)
        {
            return [.. ReadIndex().OrderByDescending(e => e.Played)];
        }
    }

    public ReplayEntry? Save(GameTree tree, string id, string source, long? ogsGameId = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrEmpty(id);
        int moves = CountMoves(tree);
        if (moves < MinMoves)
        {
            return null;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                List<ReplayEntry> index = ReadIndex();
                ReplayEntry? existing = index.FirstOrDefault(e => e.Id == id);
                string fileName = existing?.FileName ?? $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Safe(id)}.sgf";
                File.WriteAllBytes(Path.Combine(_directory, fileName), SgfWriter.WriteBytes(tree));

                GameInfo info = tree.Info;
                var entry = new ReplayEntry(
                    id,
                    fileName,
                    existing?.Played ?? DateTimeOffset.Now,
                    string.IsNullOrWhiteSpace(info.BlackPlayer) ? string.Empty : info.BlackPlayer!,
                    string.IsNullOrWhiteSpace(info.WhitePlayer) ? string.Empty : info.WhitePlayer!,
                    info.Result,
                    info.Width,
                    moves,
                    source,
                    ogsGameId);
                index.RemoveAll(e => e.Id == id);
                index.Add(entry);
                WriteIndex(index);
                Changed?.Invoke(this, EventArgs.Empty);
                return entry;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not save replay {Id}", id);
                return null;
            }
        }
    }

    public GameTree Load(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return SgfParser.Parse(File.ReadAllBytes(Path.Combine(_directory, entry.FileName)));
    }

    public void Delete(ReplayEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            List<ReplayEntry> index = ReadIndex();
            index.RemoveAll(e => e.Id == entry.Id);
            WriteIndex(index);
            try
            {
                File.Delete(Path.Combine(_directory, entry.FileName));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not delete replay file {File}", entry.FileName);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Moves on the main line.</summary>
    public static int CountMoves(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        int moves = 0;
        for (GameNode n = tree.Root; ;)
        {
            if (n.HasMove)
            {
                moves++;
            }

            if (n.Children.Count == 0)
            {
                return moves;
            }

            n = n.Children[0];
        }
    }

    private List<ReplayEntry> ReadIndex()
    {
        try
        {
            return File.Exists(IndexPath)
                ? JsonSerializer.Deserialize<List<ReplayEntry>>(File.ReadAllText(IndexPath), Json) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Replay index unreadable; starting a new one");
            return [];
        }
    }

    private void WriteIndex(List<ReplayEntry> index)
    {
        string temp = IndexPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(index, Json));
        File.Move(temp, IndexPath, overwrite: true);
    }

    private static string Safe(string id) =>
        new string([.. id.Where(c => char.IsLetterOrDigit(c) || c == '-').Take(24)]) is { Length: > 0 } s ? s : "game";

    internal static string NewLocalId() => "local-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12];
}
