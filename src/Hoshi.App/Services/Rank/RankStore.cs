using System.Text.Json;
using Hoshi.Core;

namespace Hoshi.App.Services.Rank;

/// <summary>One game that moved the estimate.</summary>
public sealed record RankGame(DateTimeOffset When, string Rival, int Handicap, int Size, bool PlayerWasBlack, double Score, double After);

/// <summary>What is on disk (<c>rank.json</c>): the estimate and the games behind it (the last 200).</summary>
public sealed record RankFile(double Rating, int Games, IReadOnlyList<RankGame> History);

/// <summary>
/// The player's estimated rank from games against KataGo's human-style profiles, kept in <c>rank.json</c> in the
/// data folder (travels in the .hoshi backup). Nothing leaves the computer.
/// </summary>
public sealed class RankStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly string? _file;
    private RankFile _data;

    public RankStore(string? dataDirectory = null)
    {
        _file = dataDirectory is null ? null : Path.Combine(dataDirectory, "rank.json");
        _data = Load(_file);
    }

    public event EventHandler? Changed;

    /// <summary>The estimate; null before the first counted game.</summary>
    public RankEstimate? Current => _data.Games == 0 ? null : new RankEstimate(_data.Rating, _data.Games);

    public IReadOnlyList<RankGame> History => _data.History;

    /// <summary>The estimate, or a start from the rank the player last chose for the rival (10k when none).</summary>
    public RankEstimate CurrentOr(string? chosenLevel) =>
        Current ?? new RankEstimate(RankEstimates.Parse(chosenLevel) ?? RankEstimates.Parse("10k")!.Value, 0);

    /// <summary>Counts a finished game against a rival of rank <paramref name="rival"/> ("7k").</summary>
    public RankEstimate Record(string rival, int handicap, int size, bool playerWasBlack, double score, DateTimeOffset when)
    {
        double rivalValue = RankEstimates.Parse(rival) ?? throw new ArgumentException("Not a rank.", nameof(rival));
        RankEstimate after = CurrentOr(rival).After(RankEstimates.Opponent(rivalValue, handicap, size, playerWasBlack), score);
        var history = _data.History.Append(new RankGame(when, rival, handicap, size, playerWasBlack, score, after.Rating)).TakeLast(200).ToList();
        _data = new RankFile(after.Rating, after.Games, history);
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
        return after;
    }

    private static RankFile Load(string? file)
    {
        try
        {
            if (file is not null && File.Exists(file))
            {
                return JsonSerializer.Deserialize<RankFile>(File.ReadAllText(file), Json) ?? Empty;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged file starts over.
        }

        return Empty;
    }

    private static RankFile Empty => new(0, 0, []);

    private void Save()
    {
        if (_file is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file + ".tmp", JsonSerializer.Serialize(_data, Json));
            File.Move(_file + ".tmp", _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The estimate is a convenience.
        }
    }
}
