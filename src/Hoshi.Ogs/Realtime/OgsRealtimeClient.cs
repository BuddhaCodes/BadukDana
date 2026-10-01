using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Hoshi.Core.Localization;

namespace Hoshi.Ogs.Realtime;

public enum OgsConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,

    /// <summary>Closed with an unrecoverable code (1014 bad gateway, 1015 TLS); no automatic reconnection.</summary>
    Failed,
}

public sealed record OgsRealtimeOptions
{
    /// <summary>Stable per-installation id sent in <c>authenticate</c>.</summary>
    public string DeviceId { get; init; } = Guid.NewGuid().ToString("N");

    public string ClientName { get; init; } = "hoshi";

    public string ClientVersion { get; init; } = "0.1.0";

    public string Language { get; init; } = "es";

    /// <summary>goban default: 10 s.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The web client uses 8 s.</summary>
    public TimeSpan PongTimeout { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Fixed delays (tests). Null uses goban's schedule: 50 ms, 100–300 ms, then 250–750 ms (5–10 s after 10 failures).</summary>
    public IReadOnlyList<TimeSpan>? ReconnectDelays { get; init; }

    /// <summary>Minimum time between reconnects caused by unparseable frames (goban: 60 s).</summary>
    public TimeSpan ParseErrorReconnectInterval { get; init; } = TimeSpan.FromSeconds(60);
}

public sealed class OgsRealtimeException : Exception
{
    public OgsRealtimeException()
    {
    }

    public OgsRealtimeException(string message)
        : base(message)
    {
    }

    public OgsRealtimeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// OGS realtime client: a plain JSON WebSocket (not socket.io), modelled on goban's <c>GobanSocket</c>.
/// <list type="bullet">
/// <item>Frames are <c>[command, data?, id?]</c> out and <c>[event, data]</c> or <c>[id, data, error?]</c> in.</item>
/// <item>On every (re)connection it sends <c>authenticate</c> first, then flushes queued messages and raises
/// <see cref="Connected"/> so subscribers can re-subscribe (games, seek graph).</item>
/// <item><c>net/ping</c> every <see cref="OgsRealtimeOptions.PingInterval"/>; latency = now − client,
/// drift = now − latency/2 − server. A missing pong drops the connection.</item>
/// <item>Reconnects quickly with jitter; never after 1014/1015; unparseable frames close with 4000.</item>
/// </list>
/// Event handlers run on the receive thread; UI code must marshal to its own thread.
/// </summary>
public sealed class OgsRealtimeClient : IAsyncDisposable
{
    private readonly Uri _url;
    private readonly Func<IWebSocketConnection> _factory;
    private readonly Func<string?> _jwtProvider;
    private readonly OgsRealtimeOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Channel<string> _outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, List<Action<JsonElement>>> _handlers = new(StringComparer.Ordinal);
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pending = [];
    private readonly object _gate = new();

    private CancellationTokenSource? _stop;
    private Task? _loop;
    private IWebSocketConnection? _connection;
    private bool _authenticated;
    private volatile bool _stopping;
    private int _lastRequestId;
    private string? _jwtOverride;
    private long _lastPongForPing;
    private long _lastPingSent;
    private DateTimeOffset _lastParseErrorReconnect = DateTimeOffset.MinValue;

    public OgsRealtimeClient(
        Uri url,
        Func<IWebSocketConnection> factory,
        Func<string?> jwtProvider,
        OgsRealtimeOptions options,
        TimeProvider? time,
        ILogger? logger)
    {
        _url = url ?? throw new ArgumentNullException(nameof(url));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _jwtProvider = jwtProvider ?? throw new ArgumentNullException(nameof(jwtProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Raised after <c>authenticate</c> has been sent on a new connection.</summary>
    public event EventHandler? Connected;

    public event EventHandler? Disconnected;

    public event EventHandler<OgsConnectionState>? StateChanged;

    /// <summary>The server pushed a new user JWT (<c>user/jwt</c>).</summary>
    public event EventHandler<string>? JwtUpdated;

    public OgsConnectionState State { get; private set; } = OgsConnectionState.Disconnected;

    public double LatencyMs { get; private set; }

    /// <summary>Positive when the local clock is ahead of the server's.</summary>
    public double ClockDriftMs { get; private set; }

    /// <summary>Best estimate of the server's current time.</summary>
    public DateTimeOffset ServerNow => _time.GetUtcNow().AddMilliseconds(-ClockDriftMs);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loop is { IsCompleted: false })
        {
            return Task.CompletedTask;
        }

        _jwtOverride = null;
        _stopping = false;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationToken token = _stop.Token;
        _loop = Task.Run(() => RunAsync(token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_stop is null || _loop is null)
        {
            return;
        }

        // Close first: once cancelled, the loop disposes the connection.
        _stopping = true;
        if (_connection is { } c)
        {
            try
            {
                await c.CloseAsync(1000, "bye", CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogDebug("Error closing the realtime socket: {Error}", ex.Message);
            }
        }

        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
        }

        _stopping = false;
        SetState(OgsConnectionState.Disconnected);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop?.Dispose();
    }

    /// <summary>Sends a message; while offline it is queued and sent after the next authentication.</summary>
    public void Send(string command, JsonNode? data = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        _outbox.Writer.TryWrite(Frame(command, data, null));
    }

    /// <summary>Sends a request and waits for its response. Fails immediately when not connected.</summary>
    public async Task<JsonElement> RequestAsync(string command, JsonNode? data, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (_gate)
        {
            if (!_authenticated)
            {
                throw new OgsRealtimeException(Tr.T("Ogs.NotConnected"));
            }

            id = ++_lastRequestId;
            _pending[id] = tcs;
        }

        _outbox.Writer.TryWrite(Frame(command, data, id));
        using CancellationTokenRegistration _ = cancellationToken.Register(() =>
        {
            lock (_gate)
            {
                _pending.Remove(id);
            }

            tcs.TrySetCanceled(cancellationToken);
        });
        return await tcs.Task;
    }

    public IDisposable Subscribe(string eventName, Action<JsonElement> handler)
    {
        ArgumentException.ThrowIfNullOrEmpty(eventName);
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            if (!_handlers.TryGetValue(eventName, out List<Action<JsonElement>>? list))
            {
                _handlers[eventName] = list = [];
            }

            list.Add(handler);
        }

        return new Subscription(this, eventName, handler);
    }

    /// <summary>Sends a <c>net/ping</c> now (also done automatically every ping interval).</summary>
    public void PingNow()
    {
        if (!_authenticated)
        {
            return;
        }

        long now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        Interlocked.Exchange(ref _lastPingSent, now);
        _outbox.Writer.TryWrite(Frame("net/ping", new JsonObject
        {
            ["client"] = now,
            ["drift"] = ClockDriftMs,
            ["latency"] = LatencyMs,
        }, null));
    }

    // ------------------------------------------------------------------

    private async Task RunAsync(CancellationToken stop)
    {
        int failures = 0;
        bool first = true;
        while (!stop.IsCancellationRequested)
        {
            SetState(first ? OgsConnectionState.Connecting : OgsConnectionState.Reconnecting);
            first = false;
            IWebSocketConnection conn = _factory();
            try
            {
                await conn.ConnectAsync(_url, stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("Could not connect to {Host}: {Error}", _url.Host, ex.Message);
                await conn.DisposeAsync();
                await DelayAsync(failures++, stop);
                continue;
            }

            failures = 0;
            await RunConnectionAsync(conn, stop);

            int? code = conn.CloseStatus;
            await conn.DisposeAsync();
            Disconnected?.Invoke(this, EventArgs.Empty);

            if (stop.IsCancellationRequested || _stopping)
            {
                break;
            }

            if (code is 1014 or 1015)
            {
                _logger.LogError("Realtime socket closed with unrecoverable code {Code}; not reconnecting", code);
                SetState(OgsConnectionState.Failed);
                return;
            }

            _logger.LogInformation("Realtime socket closed ({Code}); reconnecting", code);
            SetState(OgsConnectionState.Reconnecting);
            await DelayAsync(failures++, stop);
        }
    }

    private async Task RunConnectionAsync(IWebSocketConnection conn, CancellationToken stop)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        CancellationToken token = connectionCts.Token;
        _connection = conn;
        Task sender = Task.CompletedTask;
        Task pinger = Task.CompletedTask;
        try
        {
            await conn.SendAsync(AuthenticateFrame(), token);
            lock (_gate)
            {
                _authenticated = true;
            }

            _logger.LogInformation("Realtime connection to {Host} authenticated", _url.Host);
            sender = SendLoopAsync(conn, token);
            SetState(OgsConnectionState.Connected);
            Connected?.Invoke(this, EventArgs.Empty);
            pinger = PingLoopAsync(conn, token);

            while (!token.IsCancellationRequested)
            {
                string? frame = await conn.ReceiveAsync(token);
                if (frame is null)
                {
                    break;
                }

                if (!Dispatch(frame))
                {
                    DateTimeOffset now = _time.GetUtcNow();
                    if (now - _lastParseErrorReconnect >= _options.ParseErrorReconnectInterval)
                    {
                        _lastParseErrorReconnect = now;
                        _logger.LogWarning("Unparseable realtime frame ({Length} chars); reconnecting for a fresh state", frame.Length);
                        await conn.CloseAsync(4000, "Unparseable message", CancellationToken.None);
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopping or dropping this connection.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogWarning("Realtime connection error: {Error}", ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                _authenticated = false;
            }

            _connection = null;
            await connectionCts.CancelAsync();
            await Task.WhenAll(Ignore(sender), Ignore(pinger));
            FailPending();
        }
    }

    private async Task SendLoopAsync(IWebSocketConnection conn, CancellationToken token)
    {
        ChannelReader<string> reader = _outbox.Reader;
        while (await reader.WaitToReadAsync(token))
        {
            while (reader.TryPeek(out string? frame))
            {
                await conn.SendAsync(frame, token);
                reader.TryRead(out _); // only dequeue once sent, so nothing is lost if the socket drops
            }
        }
    }

    private async Task PingLoopAsync(IWebSocketConnection conn, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            PingNow();
            long sent = Interlocked.Read(ref _lastPingSent);
            await Task.Delay(Min(_options.PongTimeout, _options.PingInterval), token);
            if (Interlocked.Read(ref _lastPongForPing) < sent)
            {
                _logger.LogWarning("No pong from {Host} within {Timeout}; dropping the connection", _url.Host, _options.PongTimeout);
                await conn.CloseAsync(4001, "Ping timeout", CancellationToken.None);
                return;
            }

            TimeSpan rest = _options.PingInterval - Min(_options.PongTimeout, _options.PingInterval);
            if (rest > TimeSpan.Zero)
            {
                await Task.Delay(rest, token);
            }
        }
    }

    private bool Dispatch(string frame)
    {
        JsonElement root;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(frame);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return false;
        }

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
        {
            return false;
        }

        JsonElement head = root[0];
        JsonElement data = root.GetArrayLength() > 1 ? root[1] : default;

        if (head.ValueKind == JsonValueKind.Number)
        {
            int id = head.GetInt32();
            TaskCompletionSource<JsonElement>? tcs;
            lock (_gate)
            {
                _pending.Remove(id, out tcs);
            }

            if (tcs is not null)
            {
                JsonElement error = root.GetArrayLength() > 2 ? root[2] : default;
                if (error.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                {
                    string message = OgsJson.String(error, "message") ?? OgsJson.String(error, "code") ?? error.ToString();
                    tcs.TrySetException(new OgsRealtimeException(message));
                }
                else
                {
                    tcs.TrySetResult(data);
                }
            }

            return true;
        }

        string name = head.GetString() ?? string.Empty;
        switch (name)
        {
            case "net/pong":
                OnPong(data);
                break;
            case "user/jwt" when data.ValueKind == JsonValueKind.String:
                _jwtOverride = data.GetString();
                _logger.LogInformation("Server rotated the user JWT");
                JwtUpdated?.Invoke(this, _jwtOverride!);
                break;
            case "HUP":
                _logger.LogInformation("Server asked clients to reload (HUP); reconnecting");
                _ = _connection?.CloseAsync(4002, "HUP", CancellationToken.None);
                break;
        }

        Action<JsonElement>[] handlers;
        lock (_gate)
        {
            handlers = _handlers.TryGetValue(name, out List<Action<JsonElement>>? list) ? [.. list] : [];
        }

        foreach (Action<JsonElement> h in handlers)
        {
            try
            {
                h(data);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _logger.LogError(ex, "Handler for {Event} failed", name);
            }
        }

        return true;
    }

    private void OnPong(JsonElement data)
    {
        if (OgsJson.Long(data, "client") is not { } client || OgsJson.Double(data, "server") is not { } server)
        {
            return;
        }

        double now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        double latency = now - client;
        LatencyMs = latency;
        ClockDriftMs = now - (latency / 2) - server;
        Interlocked.Exchange(ref _lastPongForPing, client);
    }

    private void FailPending()
    {
        TaskCompletionSource<JsonElement>[] pending;
        lock (_gate)
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (TaskCompletionSource<JsonElement> tcs in pending)
        {
            tcs.TrySetException(new OgsRealtimeException(Tr.T("Ogs.ConnectionLost")));
        }
    }

    private string AuthenticateFrame() => Frame("authenticate", new JsonObject
    {
        ["jwt"] = _jwtOverride ?? _jwtProvider() ?? string.Empty,
        ["device_id"] = _options.DeviceId,
        ["user_agent"] = string.Create(CultureInfo.InvariantCulture, $"Hoshi/{_options.ClientVersion} (.NET {Environment.Version}; {Environment.OSVersion.Platform})"),
        ["language"] = _options.Language,
        ["client"] = _options.ClientName,
        ["client_version"] = _options.ClientVersion,
    }, null);

    private static string Frame(string command, JsonNode? data, int? id)
    {
        var array = new JsonArray { command };
        if (data is not null || id is not null)
        {
            array.Add(data?.DeepClone());
        }

        if (id is { } i)
        {
            array.Add(i);
        }

        return array.ToJsonString();
    }

    private async Task DelayAsync(int attempt, CancellationToken token)
    {
        TimeSpan delay;
        if (_options.ReconnectDelays is { Count: > 0 } fixedDelays)
        {
            delay = fixedDelays[Math.Min(attempt, fixedDelays.Count - 1)];
        }
        else
        {
            (int lo, int hi) = attempt switch
            {
                0 => (50, 50),
                1 => (100, 300),
                < 10 => (250, 750),
                _ => (5000, 10000),
            };
            delay = TimeSpan.FromMilliseconds(Random.Shared.Next(lo, hi + 1));
        }

        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetState(OgsConnectionState state)
    {
        if (State == state)
        {
            return;
        }

        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void Unsubscribe(string eventName, Action<JsonElement> handler)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(eventName, out List<Action<JsonElement>>? list))
            {
                list.Remove(handler);
            }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static async Task Ignore(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or InvalidOperationException
            or IOException or System.Net.WebSockets.WebSocketException)
        {
        }
    }

    private sealed class Subscription(OgsRealtimeClient owner, string eventName, Action<JsonElement> handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unsubscribe(eventName, handler);
            }
        }
    }
}
