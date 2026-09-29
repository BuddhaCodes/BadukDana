using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading.Channels;
using Hoshi.Ogs.Auth;
using Hoshi.Ogs.Realtime;
using Microsoft.Extensions.Logging;

namespace Hoshi.Ogs.Tests;

internal static class Fixtures
{
    public static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}

/// <summary>Routes requests by "METHOD path" to canned responses and records everything that was sent.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpRequestMessage, HttpResponseMessage>>> _routes = [];

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public FakeHttpHandler On(string method, string pathAndQuery, HttpStatusCode status, string body, string? setCookie = null)
    {
        return On(method, pathAndQuery, _ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (setCookie is not null)
            {
                response.Headers.Add("Set-Cookie", setCookie);
            }

            return response;
        });
    }

    public FakeHttpHandler On(string method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        string key = $"{method} {pathAndQuery}";
        if (!_routes.TryGetValue(key, out Queue<Func<HttpRequestMessage, HttpResponseMessage>>? queue))
        {
            _routes[key] = queue = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        }

        queue.Enqueue(respond);
        return this;
    }

    public (HttpRequestMessage Request, string? Body) Single(string method, string pathAndQuery) =>
        Requests.Single(r => r.Request.Method.Method == method && r.Request.RequestUri!.PathAndQuery == pathAndQuery);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        string key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
        if (_routes.TryGetValue(key, out Queue<Func<HttpRequestMessage, HttpResponseMessage>>? queue) && queue.Count > 0)
        {
            Func<HttpRequestMessage, HttpResponseMessage> respond = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            return respond(request);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent($"no route for {key}") };
    }
}

internal sealed class InMemoryTokenStore : ITokenStore
{
    public Dictionary<string, string> Values { get; } = [];

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(Values.TryGetValue(key, out string? v) ? v : null);

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        Values[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Values.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Captures log output so tests can assert that no secret is ever logged.</summary>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    private sealed class Logger(CapturingLoggerFactory owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Lines.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}

/// <summary>
/// In-memory WebSocket: the "server" side of the test reads what the client sent and pushes frames or closes.
/// Every call to <see cref="Factory"/> creates a new connection, like a reconnect would.
/// </summary>
internal sealed class FakeServer
{
    private readonly Channel<FakeConnection> _connections = Channel.CreateUnbounded<FakeConnection>();

    public List<FakeConnection> All { get; } = [];

    /// <summary>When set, the next connection attempts throw (simulating a network failure).</summary>
    public int FailNextConnects { get; set; }

    public IWebSocketConnection Factory()
    {
        var c = new FakeConnection(this);
        lock (All)
        {
            All.Add(c);
        }

        return c;
    }

    internal void Opened(FakeConnection c) => _connections.Writer.TryWrite(c);

    public async Task<FakeConnection> NextConnectionAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        return await _connections.Reader.ReadAsync(cts.Token);
    }
}

internal sealed class FakeConnection(FakeServer server) : IWebSocketConnection
{
    private readonly Channel<string?> _toClient = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> _fromClient = Channel.CreateUnbounded<string>();

    public Uri? Uri { get; private set; }

    public int? CloseStatus { get; private set; }

    public bool ClosedByClient { get; private set; }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (server.FailNextConnects > 0)
        {
            server.FailNextConnects--;
            throw new WebSocketConnectionException("connection refused");
        }

        Uri = uri;
        server.Opened(this);
        return Task.CompletedTask;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        if (CloseStatus is not null)
        {
            throw new InvalidOperationException("closed");
        }

        _fromClient.Writer.TryWrite(message);
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        string? frame = await _toClient.Reader.ReadAsync(cancellationToken);
        return frame;
    }

    public Task CloseAsync(int code, string reason, CancellationToken cancellationToken)
    {
        ClosedByClient = true;
        ServerClose(code);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ----- server side -----

    public void Push(string frame) => _toClient.Writer.TryWrite(frame);

    public void ServerClose(int code)
    {
        CloseStatus ??= code;
        _toClient.Writer.TryWrite(null);
    }

    public async Task<string> ReadAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        return await _fromClient.Reader.ReadAsync(cts.Token);
    }

    /// <summary>Reads frames until one starts with the given command, skipping pings.</summary>
    public async Task<string> ReadCommandAsync(string command, TimeSpan? timeout = null)
    {
        while (true)
        {
            string frame = await ReadAsync(timeout);
            if (frame.StartsWith($"[\"{command}\"", StringComparison.Ordinal))
            {
                return frame;
            }
        }
    }
}
