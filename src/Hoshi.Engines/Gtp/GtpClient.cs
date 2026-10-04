using System.Threading.Channels;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Engines.Gtp;

/// <summary>A GTP response: success (<c>=</c>) or failure (<c>?</c>) and its text (several lines joined by '\n').</summary>
public sealed record GtpResponse(bool Success, string Text)
{
    public override string ToString() => (Success ? "= " : "? ") + Text;
}

/// <summary>Which way a line of the GTP conversation went (for the GTP console).</summary>
public enum GtpDirection
{
    /// <summary>A command Hoshi sent.</summary>
    Sent,

    /// <summary>A line the engine wrote to stdout.</summary>
    Received,

    /// <summary>A line the engine wrote to stderr (its log).</summary>
    Log,
}

public sealed record GtpTraffic(GtpDirection Direction, string Text);

/// <summary>
/// Go Text Protocol v2 client (https://www.lysator.liu.se/~gunnar/gtp/gtp2-spec-draft2/gtp2-spec.html): one command
/// per line; the response starts with <c>=</c> or <c>?</c> and ends with an empty line. Commands are sent one at a
/// time, without ids (many engines handle ids badly). Streaming commands (<c>lz-analyze</c>, <c>kata-analyze</c>)
/// keep the line until they are interrupted: any other command, or cancelling the stream, sends a bare newline first,
/// which makes Leela Zero and KataGo end the response.
/// </summary>
public sealed class GtpClient : IAsyncDisposable
{
    private readonly IEngineProcess _process;
    private readonly ILogger _logger;
    private readonly Channel<string?> _lines = Channel.CreateUnbounded<string?>(new UnboundedChannelOptions { SingleReader = true });
    private readonly SemaphoreSlim _turn = new(1, 1);
    private readonly Task _reader;
    private CancellationTokenSource? _stream;
    private int _disposed;

    public GtpClient(IEngineProcess process, ILogger? logger = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _logger = logger ?? NullLogger.Instance;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Every command sent and every line received (raised on background threads).</summary>
    public event EventHandler<GtpTraffic>? Traffic;

    public bool HasExited => _process.HasExited || Volatile.Read(ref _disposed) != 0;

    /// <summary>Lets the owner report the engine's stderr through <see cref="Traffic"/>.</summary>
    public void ReportLog(string line) => Traffic?.Invoke(this, new GtpTraffic(GtpDirection.Log, line));

    /// <summary>Sends one command and waits for its full response. Interrupts a running analysis stream first.</summary>
    public async Task<GtpResponse> SendAsync(string command, CancellationToken cancellationToken = default)
    {
        string clean = Clean(command);
        if (clean.Length == 0)
        {
            throw new ArgumentException("Empty GTP command.", nameof(command));
        }

        await InterruptStreamAsync();
        await _turn.WaitAsync(cancellationToken);
        try
        {
            await WriteAsync(clean);
            // Once sent, the response is read to the end even if the caller gives up: the next command must not
            // receive the tail of this one.
            return await ReadResponseAsync(null);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <summary>
    /// Sends a streaming command and reports each line of its response until <paramref name="cancellationToken"/>
    /// is cancelled or another command is sent (then a newline interrupts it), or the engine ends it on its own
    /// (e.g. <c>kata-genmove_analyze</c>). Returns the full response.
    /// </summary>
    public async Task<GtpResponse> StreamAsync(string command, Action<string> onLine, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onLine);
        string clean = Clean(command);
        await InterruptStreamAsync();
        await _turn.WaitAsync(cancellationToken);
        var stream = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Interlocked.Exchange(ref _stream, stream)?.Dispose();
        try
        {
            await WriteAsync(clean);
            using CancellationTokenRegistration stop = stream.Token.Register(() => _ = InterruptAsync());
            return await ReadResponseAsync(onLine);
        }
        finally
        {
            Interlocked.CompareExchange(ref _stream, null, stream);
            stream.Dispose();
            _turn.Release();
        }
    }

    private async Task InterruptStreamAsync()
    {
        if (Volatile.Read(ref _stream) is { } running)
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
    }

    private async Task InterruptAsync()
    {
        try
        {
            await _process.WriteLineAsync(string.Empty, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            _logger.LogDebug("Could not interrupt the GTP stream: {Error}", ex.Message);
        }
    }

    private async Task WriteAsync(string command)
    {
        if (HasExited)
        {
            throw new EngineException(ExitMessage());
        }

        Traffic?.Invoke(this, new GtpTraffic(GtpDirection.Sent, command));
        try
        {
            await _process.WriteLineAsync(command, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new EngineException(ExitMessage(), ex);
        }
    }

    private async Task<GtpResponse> ReadResponseAsync(Action<string>? onLine)
    {
        bool? success = null;
        var text = new List<string>();
        while (true)
        {
            string? line = await _lines.Reader.ReadAsync();
            if (line is null)
            {
                throw new EngineException(ExitMessage());
            }

            if (success is null)
            {
                // Anything before the response (engines that print banners on stdout) is skipped.
                if (line.Length > 0 && line[0] is '=' or '?')
                {
                    success = line[0] == '=';
                    string rest = StripId(line[1..]);
                    if (rest.Length > 0)
                    {
                        text.Add(rest);
                        onLine?.Invoke(rest);
                    }
                }

                continue;
            }

            if (line.Length == 0)
            {
                return new GtpResponse(success.Value, string.Join('\n', text));
            }

            text.Add(line);
            onLine?.Invoke(line);
        }
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _process.ReadLineAsync(CancellationToken.None) is { } raw)
            {
                string line = Clean(raw);
                Traffic?.Invoke(this, new GtpTraffic(GtpDirection.Received, line));
                _lines.Writer.TryWrite(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            _logger.LogInformation("GTP engine output closed: {Error}", ex.Message);
        }

        _lines.Writer.TryWrite(null);
        _lines.Writer.TryComplete();
    }

    private string ExitMessage() =>
        _process.ExitReason is { } reason ? Tr.F("Gtp.Exited", reason) : Tr.T("Gtp.ExitedUnexpectedly");

    /// <summary>GTP preprocessing: tabs become spaces, other control characters (CR included) are dropped.</summary>
    internal static string Clean(string line)
    {
        var chars = new System.Text.StringBuilder(line.Length);
        foreach (char c in line)
        {
            if (c == '\t')
            {
                chars.Append(' ');
            }
            else if (!char.IsControl(c))
            {
                chars.Append(c);
            }
        }

        return chars.ToString().TrimEnd();
    }

    /// <summary>The text after <c>=</c> or <c>?</c>, without the optional numeric id.</summary>
    private static string StripId(string rest)
    {
        int i = 0;
        while (i < rest.Length && char.IsAsciiDigit(rest[i]))
        {
            i++;
        }

        return rest[i..].Trim();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await InterruptStreamAsync();
        try
        {
            if (!_process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _process.WriteLineAsync("quit", timeout.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException or OperationCanceledException)
        {
            // Already gone.
        }

        await _process.DisposeAsync();
        await _reader.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}
