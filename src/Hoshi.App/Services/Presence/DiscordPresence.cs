using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hoshi.Core.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Presence;

/// <summary>What Hoshi tells Discord: only the kind of activity, never the opponent, the game or the position.</summary>
public enum PresenceActivity
{
    None,
    Studying,
    PlayingLocal,
    PlayingEngine,
    PlayingOgs,
    SolvingProblems,
}

/// <summary>Opens the connection to the Discord app on this computer (a named pipe or a Unix socket).</summary>
public interface IPresenceTransport
{
    Task<Stream?> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>Discord's local IPC: <c>discord-ipc-0</c>…<c>9</c> as a pipe (Windows) or a socket in the temp folders.</summary>
public sealed class DiscordIpcTransport : IPresenceTransport
{
    public async Task<Stream?> ConnectAsync(CancellationToken cancellationToken)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                    try
                    {
                        await pipe.ConnectAsync(300, cancellationToken);
                        return pipe;
                    }
                    catch
                    {
                        await pipe.DisposeAsync();
                        throw;
                    }
                }

                foreach (string folder in UnixFolders())
                {
                    string path = Path.Combine(folder, $"discord-ipc-{i}");
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or SocketException or UnauthorizedAccessException)
            {
                // Not this one; try the next number.
            }
        }

        return null;
    }

    private static IEnumerable<string> UnixFolders()
    {
        foreach (string? root in new[] { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"), Environment.GetEnvironmentVariable("TMPDIR"), Environment.GetEnvironmentVariable("TMP"), Environment.GetEnvironmentVariable("TEMP"), "/tmp" })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            yield return root;
            yield return Path.Combine(root, "app", "com.discordapp.Discord"); // Flatpak
            yield return Path.Combine(root, "snap.discord");
        }
    }
}

/// <summary>
/// Discord's "rich presence" (off by default): "Studying a game" or "Playing on OGS" in the player's Discord profile,
/// through the Discord app running on this computer (nothing goes over the network from Hoshi). It needs the id of a
/// Discord application the player creates (Preferences → Online); without it, or without Discord, nothing happens.
/// </summary>
public sealed class DiscordPresence : IAsyncDisposable
{
    /// <summary>After a failed connection, wait this long before trying again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly IPresenceTransport _transport;
    private readonly ISettingsService? _settings;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Stream? _stream;
    private string? _connectedId;
    private DateTimeOffset _retryAt = DateTimeOffset.MinValue;
    private PresenceActivity _shown = PresenceActivity.None;
    private DateTimeOffset _since;

    public DiscordPresence(IPresenceTransport? transport = null, ISettingsService? settings = null, TimeProvider? time = null, ILogger<DiscordPresence>? logger = null)
    {
        _transport = transport ?? new DiscordIpcTransport();
        _settings = settings;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    /// <summary>On, with an application id (digits only).</summary>
    public bool IsEnabled => _settings?.Current is { DiscordPresence: true, DiscordAppId: { Length: > 0 } id } && id.All(char.IsAsciiDigit);

    public PresenceActivity Shown => _shown;

    /// <summary>Shows an activity (or clears it with <see cref="PresenceActivity.None"/>); repeats are ignored.</summary>
    public async Task UpdateAsync(PresenceActivity activity, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            activity = PresenceActivity.None;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (activity == _shown && (_stream is not null || activity == PresenceActivity.None))
            {
                return;
            }

            if (activity == PresenceActivity.None && _stream is null)
            {
                _shown = activity;
                return;
            }

            if (!await EnsureConnectedAsync(cancellationToken))
            {
                return;
            }

            if (activity != _shown)
            {
                _since = _time.GetUtcNow();
            }

            await SendAsync(1, Activity(activity), cancellationToken);
            _shown = activity;
            if (activity == PresenceActivity.None)
            {
                await CloseAsync();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException or OperationCanceledException)
        {
            _logger.LogInformation("Discord status not updated: {Message}", ex.Message);
            await CloseAsync();
            _retryAt = _time.GetUtcNow() + RetryAfter;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _lock.Dispose();
    }

    /// <summary>The text of an activity, in the player's language.</summary>
    public static (string Details, string State) Text(PresenceActivity activity) => activity switch
    {
        PresenceActivity.Studying => (Tr.T("Presence.Studying"), Tr.T("Presence.StudyingState")),
        PresenceActivity.PlayingLocal => (Tr.T("Presence.PlayingLocal"), string.Empty),
        PresenceActivity.PlayingEngine => (Tr.T("Presence.PlayingEngine"), string.Empty),
        PresenceActivity.PlayingOgs => (Tr.T("Presence.PlayingOgs"), string.Empty),
        PresenceActivity.SolvingProblems => (Tr.T("Presence.Problems"), string.Empty),
        _ => (string.Empty, string.Empty),
    };

    private Dictionary<string, object?> Activity(PresenceActivity activity)
    {
        object? body = null;
        if (activity != PresenceActivity.None)
        {
            (string details, string state) = Text(activity);
            var a = new Dictionary<string, object?>
            {
                ["details"] = details,
                ["timestamps"] = new Dictionary<string, long> { ["start"] = _since.ToUnixTimeSeconds() },
            };
            if (state.Length > 0)
            {
                a["state"] = state;
            }

            body = a;
        }

        return new Dictionary<string, object?>
        {
            ["cmd"] = "SET_ACTIVITY",
            ["args"] = new Dictionary<string, object?> { ["pid"] = Environment.ProcessId, ["activity"] = body },
            ["nonce"] = Guid.NewGuid().ToString("N"),
        };
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        string id = _settings?.Current.DiscordAppId ?? string.Empty;
        if (_stream is not null && _connectedId == id)
        {
            return true;
        }

        await CloseAsync();
        if (id.Length == 0 || _time.GetUtcNow() < _retryAt)
        {
            return false;
        }

        _stream = await _transport.ConnectAsync(cancellationToken);
        if (_stream is null)
        {
            _retryAt = _time.GetUtcNow() + RetryAfter;
            return false;
        }

        _connectedId = id;
        await SendAsync(0, new Dictionary<string, object> { ["v"] = 1, ["client_id"] = id }, cancellationToken);
        await ReadFrameAsync(cancellationToken); // READY (or an error frame, then Discord closes the pipe)
        return true;
    }

    private async Task SendAsync(int opcode, object payload, CancellationToken cancellationToken)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload);
        byte[] frame = new byte[8 + json.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), json.Length);
        json.CopyTo(frame, 8);
        await _stream!.WriteAsync(frame, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    private async Task<string> ReadFrameAsync(CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];
        await _stream!.ReadExactlyAsync(header, cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
        if (length is < 0 or > 64 * 1024)
        {
            throw new IOException("Unexpected frame from Discord.");
        }

        byte[] body = new byte[length];
        await _stream.ReadExactlyAsync(body, cancellationToken);
        return Encoding.UTF8.GetString(body);
    }

    private async Task CloseAsync()
    {
        if (_stream is { } s)
        {
            _stream = null;
            _connectedId = null;
            try
            {
                await s.DisposeAsync();
            }
            catch (IOException)
            {
                // Already gone.
            }
        }
    }
}
