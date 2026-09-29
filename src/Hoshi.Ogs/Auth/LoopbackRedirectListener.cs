using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace Hoshi.Ogs.Auth;

/// <summary>
/// Minimal HTTP listener on 127.0.0.1 for the OAuth redirect (RFC 8252 §7.3). Uses a raw socket rather than
/// HttpListener so it needs no URL ACL on Windows. Serves one "you can close this tab" page and returns the code.
/// </summary>
public sealed class LoopbackRedirectListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _path;

    private LoopbackRedirectListener(TcpListener listener, string path)
    {
        _listener = listener;
        _path = path;
    }

    public static LoopbackRedirectListener Start(int port, string path)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new OgsAuthException($"El puerto {port} de 127.0.0.1 está ocupado; no se puede recibir la respuesta de OGS.", ex);
        }

        return new LoopbackRedirectListener(listener, path);
    }

    /// <summary>Waits for the browser to hit the redirect path and returns the authorization code.</summary>
    public async Task<string> WaitForCodeAsync(string expectedState, CancellationToken cancellationToken)
    {
        while (true)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using NetworkStream stream = client.GetStream();
            string? target = await ReadRequestTargetAsync(stream, cancellationToken);
            if (target is null)
            {
                continue;
            }

            int q = target.IndexOf('?', StringComparison.Ordinal);
            string path = q < 0 ? target : target[..q];
            if (!string.Equals(path, _path, StringComparison.Ordinal))
            {
                await RespondAsync(stream, 404, "Not found", cancellationToken);
                continue;
            }

            var query = HttpUtility.ParseQueryString(q < 0 ? string.Empty : target[(q + 1)..]);
            if (query["error"] is { } error)
            {
                await RespondAsync(stream, 200, Page("No se inició sesión", "OGS canceló la autorización. Puedes cerrar esta pestaña."), cancellationToken);
                throw new OgsAuthException($"OGS devolvió un error de autorización: {error}");
            }

            if (query["state"] != expectedState)
            {
                await RespondAsync(stream, 400, Page("Solicitud no válida", "La respuesta no corresponde a este inicio de sesión."), cancellationToken);
                throw new OgsAuthException("El parámetro state no coincide; se descartó la respuesta.");
            }

            if (query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(stream, 400, Page("Solicitud no válida", "Falta el código de autorización."), cancellationToken);
                throw new OgsAuthException("La redirección no incluye un código de autorización.");
            }

            await RespondAsync(stream, 200, Page("Sesión iniciada", "Hoshi ya tiene acceso a tu cuenta de OGS. Puedes cerrar esta pestaña."), cancellationToken);
            return code;
        }
    }

    public void Dispose() => _listener.Stop();

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        string head = Encoding.ASCII.GetString(buffer, 0, total);
        string firstLine = head.Split("\r\n", 2)[0];
        string[] parts = firstLine.Split(' ');
        return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string html, CancellationToken cancellationToken)
    {
        byte[] body = Encoding.UTF8.GetBytes(html);
        string reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        string header =
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static string Page(string title, string message) =>
        $$"""
        <!doctype html><html lang="es"><head><meta charset="utf-8"><title>Hoshi · {{title}}</title>
        <style>body{background:#1e1e1e;color:#e6e6e6;font-family:system-ui,sans-serif;display:grid;place-items:center;height:100vh;margin:0}
        div{max-width:28rem;text-align:center}h1{color:#e0a94a;font-weight:500}</style></head>
        <body><div><h1>Hoshi · {{title}}</h1><p>{{message}}</p></div></body></html>
        """;
}
