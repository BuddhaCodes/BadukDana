using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;
using Hoshi.Core.Localization;

namespace Hoshi.Ogs.Auth;

/// <summary>
/// Minimal HTTP listener on 127.0.0.1 for the OAuth redirect (RFC 8252 §7.3). Uses a raw socket rather than
/// HttpListener so it needs no URL ACL on Windows. Serves one "you can close this tab" page and returns the code.
/// </summary>
public sealed class LoopbackRedirectListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _path;
    private readonly Uri? _appReturn;

    private LoopbackRedirectListener(TcpListener listener, string path, Uri? appReturn)
    {
        _listener = listener;
        _path = path;
        _appReturn = appReturn;
    }

    /// <param name="appReturn">Phones: a link back to the app, shown on the "signed in" page and followed by itself.</param>
    public static LoopbackRedirectListener Start(int port, string path, Uri? appReturn = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new OgsAuthException(Tr.F("Ogs.PortBusy", port), ex);
        }

        return new LoopbackRedirectListener(listener, path, appReturn);
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
                await RespondAsync(stream, 200, Page(Tr.T("Ogs.Page.NotSignedInTitle"), Tr.T("Ogs.Page.CancelledBody")), cancellationToken);
                throw new OgsAuthException(Tr.F("Ogs.AuthorizationError", error));
            }

            if (query["state"] != expectedState)
            {
                await RespondAsync(stream, 400, Page(Tr.T("Ogs.Page.InvalidTitle"), Tr.T("Ogs.Page.StateMismatchBody")), cancellationToken);
                throw new OgsAuthException(Tr.T("Ogs.StateMismatch"));
            }

            if (query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(stream, 400, Page(Tr.T("Ogs.Page.InvalidTitle"), Tr.T("Ogs.Page.CodeMissingBody")), cancellationToken);
                throw new OgsAuthException(Tr.T("Ogs.CodeMissing"));
            }

            await RespondAsync(stream, 200, Page(Tr.T("Ogs.Page.SignedInTitle"), Tr.T(_appReturn is null ? "Ogs.Page.SignedInBody" : "Ogs.Page.SignedInPhoneBody"), _appReturn), cancellationToken);
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

    private static string Page(string title, string message, Uri? appReturn = null)
    {
        // Phones: a big "Back to Hoshi" button, and the page follows it by itself after a moment.
        string back = appReturn is null ? string.Empty : $$"""
            <p><a class="back" href="{{WebUtility.HtmlEncode(appReturn.OriginalString)}}">{{WebUtility.HtmlEncode(Tr.T("Ogs.Page.BackToApp"))}}</a></p>
            <script>setTimeout(function(){location.href={{System.Text.Json.JsonSerializer.Serialize(appReturn.OriginalString)}};},700);</script>
            """;
        return $$"""
            <!doctype html><html lang="{{Tr.Language}}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <title>Hoshi · {{title}}</title>
            <style>body{background:#f8f1e7;color:#2d2440;font-family:system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;margin:0;padding:0 18px}
            div{max-width:28rem;text-align:center;background:#fff;border-radius:24px;padding:28px 24px;box-shadow:0 6px 0 rgba(120,90,60,.12)}
            h1{color:#c8577a;font-weight:600;font-size:1.5rem}p{line-height:1.5}
            a.back{display:inline-block;margin-top:8px;background:#2d2440;color:#fff;text-decoration:none;font-weight:600;border-radius:999px;padding:14px 26px}</style></head>
            <body><div><h1>Hoshi · {{title}}</h1><p>{{message}}</p>{{back}}</div></body></html>
            """;
    }
}
