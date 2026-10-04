using System.Globalization;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Engines.Gtp;

/// <summary>A position to give a GTP engine: board, komi, rules, setup stones and the moves played since.</summary>
public sealed record GtpPosition
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public double Komi { get; init; } = 6.5;

    public RuleSet Rules { get; init; } = RuleSet.Japanese;

    public IReadOnlyList<(Point Point, Stone Stone)> InitialStones { get; init; } = [];

    public IReadOnlyList<EngineMove> Moves { get; init; } = [];

    /// <summary>Who plays next.</summary>
    public Stone ToMove { get; init; } = Stone.Black;

    internal bool SameSetup(GtpPosition other) =>
        Width == other.Width && Height == other.Height && Komi.Equals(other.Komi) && Rules == other.Rules
        && InitialStones.SequenceEqual(other.InitialStones);
}

/// <summary>What <c>genmove</c> answered: a point, a pass or a resignation.</summary>
public sealed record GtpMove(Point? Point, bool Resign)
{
    public bool IsPass => Point is null && !Resign;
}

/// <summary>
/// One running GTP engine: identifies it (<c>name</c>, <c>version</c>, <c>list_commands</c>), sends the init
/// commands, keeps its board in step with Hoshi's position (only the new moves, or <c>undo</c>, when it can), asks
/// for moves and streams its analysis.
/// </summary>
public sealed class GtpEngine : IAsyncDisposable
{
    private const int MaxUndos = 8;
    private readonly GtpClient _client;
    private readonly ILogger _logger;
    private readonly HashSet<string> _commands = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _operation = new(1, 1);
    private CancellationTokenSource? _analysis;
    private GtpPosition? _position;

    private GtpEngine(GtpClient client, GtpEngineConfig config, ILogger logger)
    {
        _client = client;
        Config = config;
        _logger = logger;
    }

    public GtpEngineConfig Config { get; }

    public GtpClient Client => _client;

    /// <summary>The engine's own name and version (e.g. "KataGo 1.16.3"), or the configured name.</summary>
    public string DisplayName { get; private set; } = string.Empty;

    public IReadOnlySet<string> Commands => _commands;

    public GtpAnalysisKind AnalysisKind =>
        _commands.Contains("kata-analyze") ? GtpAnalysisKind.KataGo
        : _commands.Contains("lz-analyze") ? GtpAnalysisKind.Leela
        : GtpAnalysisKind.None;

    public bool HasExited => _client.HasExited;

    /// <summary>Starts the process and runs the handshake and init commands.</summary>
    public static Task<GtpEngine> StartAsync(GtpEngineConfig config, ILogger? logger = null, EventHandler<GtpTraffic>? traffic = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        GtpClient? client = null;
        IEngineProcess process = GtpProcess.Start(config, logger, line => client?.ReportLog(line));
        client = new GtpClient(process, logger);
        return ConnectAsync(client, config, logger, traffic, cancellationToken);
    }

    /// <summary>Wraps an already running client (tests use a fake process).</summary>
    public static async Task<GtpEngine> ConnectAsync(GtpClient client, GtpEngineConfig config, ILogger? logger = null, EventHandler<GtpTraffic>? traffic = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(config);
        if (traffic is not null)
        {
            client.Traffic += traffic;
        }

        var engine = new GtpEngine(client, config, logger ?? NullLogger.Instance);
        try
        {
            await engine.HandshakeAsync(cancellationToken);
            return engine;
        }
        catch
        {
            await engine.DisposeAsync();
            throw;
        }
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        GtpResponse protocol = await _client.SendAsync("protocol_version", cancellationToken);
        if (protocol.Success && protocol.Text.Trim() is { Length: > 0 } v && v != "2")
        {
            _logger.LogWarning("{Engine} speaks GTP version {Version}", Config.Name, v);
        }

        GtpResponse list = await _client.SendAsync("list_commands", cancellationToken);
        if (list.Success)
        {
            foreach (string c in list.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _commands.Add(c);
            }
        }

        string name = (await _client.SendAsync("name", cancellationToken)) is { Success: true, Text: { Length: > 0 } n } ? n.Trim() : Config.Name;
        string version = (await _client.SendAsync("version", cancellationToken)) is { Success: true, Text: { Length: > 0 } ver } ? ver.Trim() : string.Empty;
        DisplayName = version.Length > 0 && version.Length < 24 ? $"{name} {version}" : name;

        foreach (string command in Config.InitCommandList)
        {
            GtpResponse r = await _client.SendAsync(command, cancellationToken);
            if (!r.Success)
            {
                _logger.LogWarning("{Engine} rejected init command {Command}: {Error}", Config.Name, command, r.Text);
            }
        }
    }

    public bool Supports(string command) => _commands.Count == 0 || _commands.Contains(command);

    /// <summary>
    /// Makes the engine's board match <paramref name="target"/>: plays only the new moves when the engine's game is
    /// a prefix, takes back a few with <c>undo</c>, otherwise starts again from <c>clear_board</c>.
    /// </summary>
    public async Task SetPositionAsync(GtpPosition target, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        await BeginAsync(cancellationToken);
        try
        {
            await SyncAsync(target, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>Stops a running analysis (another operation needs the engine) and waits for the engine's turn.</summary>
    private async Task BeginAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _analysis) is { } running)
        {
            try
            {
                await running.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // It just ended.
            }
        }

        await _operation.WaitAsync(cancellationToken);
    }

    private async Task SyncAsync(GtpPosition target, CancellationToken cancellationToken)
    {
        GtpPosition? now = _position;
        if (now is not null && now.SameSetup(target))
        {
            int common = 0;
            while (common < now.Moves.Count && common < target.Moves.Count && now.Moves[common] == target.Moves[common])
            {
                common++;
            }

            int undos = now.Moves.Count - common;
            if (undos == 0 || (undos <= MaxUndos && _commands.Contains("undo") && await TryUndoAsync(undos, cancellationToken)))
            {
                _position = now with { Moves = [.. target.Moves.Take(common)] };
                await PlayMovesAsync(target, common, cancellationToken);
                _position = target;
                return;
            }
        }

        _position = null;
        await ExpectAsync(target.Width == target.Height
            ? "boardsize " + target.Width.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"rectangular_boardsize {target.Width} {target.Height}"), cancellationToken);
        await ExpectAsync("clear_board", cancellationToken);
        await ExpectAsync("komi " + target.Komi.ToString("0.0##", CultureInfo.InvariantCulture), cancellationToken);
        if (_commands.Contains("kata-set-rules"))
        {
            GtpResponse rules = await _client.SendAsync("kata-set-rules " + KataGoAnalysisEngine.RulesName(target.Rules), cancellationToken);
            if (!rules.Success)
            {
                _logger.LogInformation("{Engine} kept its rules: {Error}", Config.Name, rules.Text);
            }
        }

        foreach ((Point p, Stone s) in target.InitialStones)
        {
            await ExpectAsync($"play {Color(s)} {p.ToHuman(target.Height)}", cancellationToken);
        }

        await PlayMovesAsync(target, 0, cancellationToken);
        _position = target;
    }

    private async Task PlayMovesAsync(GtpPosition target, int from, CancellationToken cancellationToken)
    {
        for (int i = from; i < target.Moves.Count; i++)
        {
            EngineMove m = target.Moves[i];
            await ExpectAsync($"play {Color(m.Color)} {KataGoAnalysisEngine.Location(m.Point, target.Height)}", cancellationToken);
        }
    }

    private async Task<bool> TryUndoAsync(int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            if (!(await _client.SendAsync("undo", cancellationToken)).Success)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Asks the engine for a move for <paramref name="color"/> in the given position; the engine plays it on its board.</summary>
    public async Task<GtpMove> GenMoveAsync(GtpPosition position, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        await BeginAsync(cancellationToken);
        try
        {
            await SyncAsync(position, cancellationToken);
            GtpResponse r = await _client.SendAsync("genmove " + Color(position.ToMove), cancellationToken);
            if (!r.Success)
            {
                _position = null;
                throw new EngineException(Tr.F("Gtp.CommandFailed", DisplayName, "genmove", r.Text));
            }

            GtpMove move = ParseMove(r.Text, position.Height)
                ?? throw new EngineException(Tr.F("Gtp.BadMove", DisplayName, r.Text));
            _position = move.Resign ? null : position with
            {
                Moves = [.. position.Moves, new EngineMove(position.ToMove, move.Point)],
                ToMove = position.ToMove == Stone.Black ? Stone.White : Stone.Black,
            };
            return move;
        }
        finally
        {
            _operation.Release();
        }
    }

    /// <summary>
    /// Streams the engine's analysis of <paramref name="position"/> every <paramref name="interval"/> until cancelled.
    /// </summary>
    public async Task AnalyzeAsync(GtpPosition position, int turn, TimeSpan interval, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(onUpdate);
        GtpAnalysisKind kind = AnalysisKind;
        if (kind == GtpAnalysisKind.None)
        {
            throw new EngineException(Tr.F("Gtp.NoAnalysis", DisplayName));
        }

        using var analysis = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await BeginAsync(cancellationToken);
        Interlocked.Exchange(ref _analysis, analysis);
        try
        {
            await SyncAsync(position, analysis.Token);
            await StreamAsync(position, turn, interval, kind, onUpdate, analysis.Token);
        }
        catch (OperationCanceledException) when (analysis.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Another operation took the engine over.
        }
        finally
        {
            Interlocked.CompareExchange(ref _analysis, null, analysis);
            _operation.Release();
        }
    }

    private async Task StreamAsync(GtpPosition position, int turn, TimeSpan interval, GtpAnalysisKind kind, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken)
    {
        int centiseconds = Math.Max(5, (int)Math.Round(interval.TotalMilliseconds / 10));
        string command = kind == GtpAnalysisKind.KataGo
            ? string.Create(CultureInfo.InvariantCulture, $"kata-analyze {Color(position.ToMove)} {centiseconds} ownership true rootInfo true")
            : string.Create(CultureInfo.InvariantCulture, $"lz-analyze {Color(position.ToMove)} {centiseconds}");
        await _client.StreamAsync(
            command,
            line =>
            {
                if (GtpAnalysisParser.Parse(line, kind, position.Width, position.Height, position.ToMove, turn) is { } analysis)
                {
                    onUpdate(analysis);
                }
            },
            cancellationToken);
    }

    /// <summary>Sends a command typed in the GTP console. The engine's board may change, so the next sync starts afresh.</summary>
    public async Task<GtpResponse> SendRawAsync(string command, CancellationToken cancellationToken = default)
    {
        await BeginAsync(cancellationToken);
        try
        {
            _position = null;
            return await _client.SendAsync(command, cancellationToken);
        }
        finally
        {
            _operation.Release();
        }
    }

    internal static GtpMove? ParseMove(string text, int height)
    {
        string s = text.Trim();
        if (s.Equals("resign", StringComparison.OrdinalIgnoreCase))
        {
            return new GtpMove(null, Resign: true);
        }

        if (s.Equals("pass", StringComparison.OrdinalIgnoreCase))
        {
            return new GtpMove(null, Resign: false);
        }

        return Point.TryParseHuman(s, height, out Point p) ? new GtpMove(p, Resign: false) : null;
    }

    private async Task ExpectAsync(string command, CancellationToken cancellationToken)
    {
        GtpResponse r = await _client.SendAsync(command, cancellationToken);
        if (!r.Success)
        {
            _position = null;
            throw new EngineException(Tr.F("Gtp.CommandFailed", DisplayName.Length > 0 ? DisplayName : Config.Name, command, r.Text));
        }
    }

    private static string Color(Stone s) => s == Stone.White ? "W" : "B";

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _analysis) is { } running)
        {
            try
            {
                await running.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        await _client.DisposeAsync();
    }
}
