using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hoshi.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Engines.KataGo;

/// <summary>
/// Client for KataGo's JSON analysis engine (docs/Analysis_Engine.md, verified 2026-09-29 against KataGo d91ea85):
/// one JSON query per line on stdin, one JSON response per analysed turn on stdout, matched by <c>id</c>.
/// Several queries may be in flight; a cancelled query is terminated with <c>{"action":"terminate"}</c>.
/// The process is started lazily and restarted if it dies.
/// </summary>
public sealed class KataGoAnalysisEngine : IAsyncDisposable
{
    private readonly Func<IEngineProcess> _start;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private IEngineProcess? _process;
    private Task? _reader;
    private int _nextId;

    public KataGoAnalysisEngine(Func<IEngineProcess> start, ILogger? logger = null)
    {
        _start = start ?? throw new ArgumentNullException(nameof(start));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Analyses the requested turns. Results are ordered by turn.</summary>
    /// <param name="onUpdate">
    /// Receives partial results while KataGo searches (needs <see cref="AnalysisQuery.ReportDuringSearchEvery"/>);
    /// called on the reader thread.
    /// </param>
    public async Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken, Action<TurnAnalysis>? onUpdate = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        IEngineProcess process = await EnsureStartedAsync(cancellationToken);
        string id = "hoshi-" + Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        int expected = query.Turns.Count == 0 ? 1 : query.Turns.Count;
        var pending = new Pending(expected, query.Width, query.Height, onUpdate);
        _pending[id] = pending;
        try
        {
            try
            {
                await process.WriteLineAsync(ToJson(query, id), cancellationToken);
            }
            catch (IOException ex)
            {
                throw new EngineException(ExitedMessage(process), ex);
            }

            return await pending.Completion.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop the search on the engine too, so the next query does not wait behind this one.
            await TerminateAsync(process, id);
            throw;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { } p)
        {
            await p.DisposeAsync();
        }

        if (_reader is { } r)
        {
            await r.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        _startLock.Dispose();
    }

    internal static string ToJson(AnalysisQuery q, string id)
    {
        var moves = new JsonArray();
        foreach (EngineMove m in q.Moves)
        {
            moves.Add(new JsonArray(Player(m.Color), Location(m.Point, q.Height)));
        }

        var json = new JsonObject
        {
            ["id"] = id,
            ["moves"] = moves,
            ["rules"] = RulesName(q.Rules),
            ["komi"] = q.Komi,
            ["boardXSize"] = q.Width,
            ["boardYSize"] = q.Height,
            ["maxVisits"] = q.MaxVisits,
            ["includeOwnership"] = q.IncludeOwnership,
        };
        if (q.InitialStones.Count > 0)
        {
            var stones = new JsonArray();
            foreach ((Point p, Stone s) in q.InitialStones)
            {
                stones.Add(new JsonArray(Player(s), Location(p, q.Height)));
            }

            json["initialStones"] = stones;
        }

        if (q.Moves.Count == 0 || q.InitialPlayer == Stone.White)
        {
            json["initialPlayer"] = Player(q.InitialPlayer);
        }

        if (q.Priority != 0)
        {
            json["priority"] = q.Priority;
        }

        if (q.ReportDuringSearchEvery is { } every)
        {
            json["reportDuringSearchEvery"] = every;
        }

        if (q.Turns.Count > 0)
        {
            json["analyzeTurns"] = new JsonArray([.. q.Turns.Select(t => (JsonNode)t)]);
        }

        return json.ToJsonString();
    }

    /// <summary>GTP coordinates, e.g. "Q16" (no letter I), or "pass".</summary>
    internal static string Location(Point? p, int height) => p is { } q ? q.ToHuman(height) : "pass";

    internal static Point? ParseLocation(string? s, int height) =>
        s is null || s.Equals("pass", StringComparison.OrdinalIgnoreCase) ? null
        : Point.TryParseHuman(s, height, out Point p) ? p
        : null;

    internal static string RulesName(RuleSet rules) => rules.Name switch
    {
        "chinese" => "chinese",
        "korean" => "korean",
        "aga" => "aga",
        "nz" => "new-zealand",
        "ing" => "chinese",
        _ => "japanese",
    };

    internal static TurnAnalysis ParseResponse(JsonElement r, int width, int height)
    {
        var candidates = new List<MoveCandidate>();
        if (r.TryGetProperty("moveInfos", out JsonElement infos) && infos.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement m in infos.EnumerateArray())
            {
                var pv = new List<Point?>();
                if (m.TryGetProperty("pv", out JsonElement pvs) && pvs.ValueKind == JsonValueKind.Array)
                {
                    pv.AddRange(pvs.EnumerateArray().Select(v => ParseLocation(v.GetString(), height)));
                }

                candidates.Add(new MoveCandidate(
                    ParseLocation(m.GetProperty("move").GetString(), height),
                    m.TryGetProperty("order", out JsonElement o) ? o.GetInt32() : candidates.Count,
                    m.TryGetProperty("visits", out JsonElement v) ? v.GetInt32() : 0,
                    m.TryGetProperty("winrate", out JsonElement w) ? w.GetDouble() : 0.5,
                    m.TryGetProperty("scoreLead", out JsonElement s) ? s.GetDouble() : 0,
                    m.TryGetProperty("prior", out JsonElement pr) ? pr.GetDouble() : 0,
                    pv));
            }
        }

        candidates.Sort((a, b) => a.Order.CompareTo(b.Order));
        JsonElement root = r.TryGetProperty("rootInfo", out JsonElement ri) ? ri : default;
        double[]? ownership = null;
        if (r.TryGetProperty("ownership", out JsonElement own) && own.ValueKind == JsonValueKind.Array && own.GetArrayLength() == width * height)
        {
            ownership = [.. own.EnumerateArray().Select(e => e.GetDouble())];
        }

        return new TurnAnalysis(
            r.GetProperty("turnNumber").GetInt32(),
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("currentPlayer", out JsonElement cp) && cp.GetString() == "W" ? Stone.White : Stone.Black,
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("winrate", out JsonElement rw) ? rw.GetDouble() : candidates.FirstOrDefault()?.Winrate ?? 0.5,
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("scoreLead", out JsonElement rs) ? rs.GetDouble() : candidates.FirstOrDefault()?.ScoreLead ?? 0,
            root.ValueKind == JsonValueKind.Object && root.TryGetProperty("visits", out JsonElement rv) ? rv.GetInt32() : 0,
            candidates,
            ownership);
    }

    private static string Player(Stone s) => s == Stone.White ? "W" : "B";

    private async Task<IEngineProcess> EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false } alive)
            {
                return alive;
            }

            if (_process is { } dead)
            {
                _logger.LogWarning("KataGo exited; restarting it");
                await dead.DisposeAsync();
            }

            IEngineProcess process = _start();
            _process = process;
            _reader = Task.Run(() => ReadLoopAsync(process));
            return process;
        }
        finally
        {
            _startLock.Release();
        }
    }

    private async Task ReadLoopAsync(IEngineProcess process)
    {
        try
        {
            while (await process.ReadLineAsync(CancellationToken.None) is { } line)
            {
                Dispatch(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogInformation("KataGo output closed: {Error}", ex.Message);
        }

        string message = ExitedMessage(process);
        _logger.LogWarning("KataGo stopped: {Reason}", message);
        foreach (Pending p in _pending.Values)
        {
            p.Completion.TrySetException(new EngineException(message));
        }
    }

    private static string ExitedMessage(IEngineProcess process) =>
        process.ExitReason is { } reason
            ? "KataGo se cerró (" + reason + ")"
            : "KataGo se cerró inesperadamente. Revisa su configuración y el log.";

    private void Dispatch(string line)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            _logger.LogDebug("Ignoring non-JSON KataGo output: {Line}", line);
            return;
        }

        using (doc)
        {
            JsonElement r = doc.RootElement;
            string? id = r.TryGetProperty("id", out JsonElement idElement) ? idElement.GetString() : null;
            if (r.TryGetProperty("error", out JsonElement error))
            {
                string message = error.GetString() ?? "error";
                _logger.LogWarning("KataGo error: {Error}", message);
                if (id is not null && _pending.TryGetValue(id, out Pending? failed))
                {
                    failed.Completion.TrySetException(new EngineException("KataGo: " + message));
                }

                return;
            }

            if (r.TryGetProperty("warning", out JsonElement warning))
            {
                _logger.LogInformation("KataGo warning: {Warning}", warning.GetString());
                return;
            }

            if (id is null || !_pending.TryGetValue(id, out Pending? pending) || !r.TryGetProperty("turnNumber", out _))
            {
                return;
            }

            if (r.TryGetProperty("isDuringSearch", out JsonElement during) && during.GetBoolean())
            {
                if (pending.OnUpdate is { } update && r.TryGetProperty("moveInfos", out _))
                {
                    update(ParseResponse(r, pending.Width, pending.Height));
                }

                return;
            }

            if (r.TryGetProperty("noResults", out JsonElement none) && none.GetBoolean())
            {
                pending.Completion.TrySetCanceled();
                return;
            }

            pending.Add(ParseResponse(r, pending.Width, pending.Height));
        }
    }

    private async Task TerminateAsync(IEngineProcess process, string id)
    {
        try
        {
            string json = new JsonObject { ["id"] = id + "-stop", ["action"] = "terminate", ["terminateId"] = id }.ToJsonString();
            await process.WriteLineAsync(json, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug("Could not terminate KataGo query {Id}: {Error}", id, ex.Message);
        }
    }

    private sealed class Pending(int expected, int width, int height, Action<TurnAnalysis>? onUpdate)
    {
        public Action<TurnAnalysis>? OnUpdate { get; } = onUpdate;

        private readonly List<TurnAnalysis> _results = [];

        public int Width { get; } = width;

        public int Height { get; } = height;

        public TaskCompletionSource<IReadOnlyList<TurnAnalysis>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Add(TurnAnalysis turn)
        {
            lock (_results)
            {
                _results.Add(turn);
                if (_results.Count >= expected)
                {
                    Completion.TrySetResult([.. _results.OrderBy(t => t.Turn)]);
                }
            }
        }
    }
}
