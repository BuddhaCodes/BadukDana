using System.Net.WebSockets;
using System.Text;

namespace Hoshi.Ogs.Realtime;

/// <summary>Text-frame WebSocket abstraction so the realtime client can be tested without a server.</summary>
public interface IWebSocketConnection : IAsyncDisposable
{
    /// <summary>Close code received or sent, once the connection is closed.</summary>
    int? CloseStatus { get; }

    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

    Task SendAsync(string message, CancellationToken cancellationToken);

    /// <summary>The next text message, or null when the connection has closed.</summary>
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);

    Task CloseAsync(int code, string reason, CancellationToken cancellationToken);
}

public sealed class WebSocketConnectionException : Exception
{
    public WebSocketConnectionException()
    {
    }

    public WebSocketConnectionException(string message)
        : base(message)
    {
    }

    public WebSocketConnectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary><see cref="ClientWebSocket"/> implementation.</summary>
public sealed class ClientWebSocketConnection : IWebSocketConnection
{
    private readonly ClientWebSocket _socket = new();
    private readonly string _userAgent;

    public ClientWebSocketConnection(string userAgent)
    {
        _userAgent = userAgent;
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
    }

    public int? CloseStatus => _socket.CloseStatus is { } s ? (int)s : _closeCode;

    private int? _closeCode;

    public async Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
    {
        _socket.Options.SetRequestHeader("User-Agent", _userAgent);
        try
        {
            await _socket.ConnectAsync(uri, cancellationToken);
        }
        catch (WebSocketException ex)
        {
            throw new WebSocketConnectionException(ex.Message, ex);
        }
    }

    public Task SendAsync(string message, CancellationToken cancellationToken) =>
        _socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (WebSocketException)
            {
                _closeCode ??= 1006;
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }
    }

    public async Task CloseAsync(int code, string reason, CancellationToken cancellationToken)
    {
        _closeCode = code;
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseOutputAsync((WebSocketCloseStatus)code, reason, cancellationToken);
            }
            catch (WebSocketException)
            {
                // Already gone.
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}
