using System.Diagnostics;
using System.Text;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.Engines.Gtp;

/// <summary>
/// A GTP engine the user added in Preferences → Engines: a name, the executable, its arguments (one line, quotes for
/// spaces) and GTP commands sent after it starts (one per line, e.g. <c>time_settings 0 5 1</c>).
/// </summary>
public sealed record GtpEngineConfig(string Name, string Executable, string Arguments = "", string InitCommands = "")
{
    public string? Validate() =>
        string.IsNullOrWhiteSpace(Executable) || !File.Exists(Executable)
            ? Tr.F("Gtp.ExecutableMissing", string.IsNullOrWhiteSpace(Executable) ? "?" : Path.GetFileName(Executable))
            : null;

    /// <summary>The init commands, one per line, without blanks and <c>#</c> comments.</summary>
    public IReadOnlyList<string> InitCommandList =>
    [
        .. InitCommands.Split('\n')
            .Select(l => (l.Contains('#', StringComparison.Ordinal) ? l[..l.IndexOf('#', StringComparison.Ordinal)] : l).Trim())
            .Where(l => l.Length > 0),
    ];
}

/// <summary>Splits an argument line like a shell would: spaces separate, double quotes group, <c>\"</c> is a quote.</summary>
public static class CommandLine
{
    public static IReadOnlyList<string> Split(string? arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        var current = new StringBuilder();
        bool quoted = false;
        bool any = false;
        for (int i = 0; i < arguments.Length; i++)
        {
            char c = arguments[i];
            if (c == '\\' && i + 1 < arguments.Length && arguments[i + 1] == '"')
            {
                current.Append('"');
                any = true;
                i++;
            }
            else if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}

/// <summary>Runs any GTP engine (stdin/stdout); stderr lines go to the logger and to <c>onLog</c> (the GTP console).</summary>
public sealed class GtpProcess : IEngineProcess
{
    private const int StderrLinesKept = 40;
    private readonly Process _process;
    private readonly Queue<string> _stderr = new();

    private GtpProcess(Process process) => _process = process;

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public string? ExitReason
    {
        get
        {
            try
            {
                if (!_process.HasExited)
                {
                    return null;
                }

                _process.WaitForExit(2000);
                string[] lines;
                lock (_stderr)
                {
                    lines = [.. _stderr];
                }

                return KataGoProcess.Describe(_process.ExitCode, lines);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public static GtpProcess Start(GtpEngineConfig config, ILogger? logger = null, Action<string>? onLog = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ILogger log = logger ?? NullLogger.Instance;
        if (config.Validate() is { } problem)
        {
            throw new EngineException(problem);
        }

        var info = new ProcessStartInfo(config.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(config.Executable) ?? Environment.CurrentDirectory,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string a in CommandLine.Split(config.Arguments))
        {
            info.ArgumentList.Add(a);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new EngineException(Tr.F("Gtp.CouldNotStart", config.Name, "?"));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new EngineException(Tr.F("Gtp.CouldNotStart", config.Name, ex.Message), ex);
        }

        var result = new GtpProcess(process);
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                log.LogDebug("{Engine} stderr: {Line}", config.Name, line);
                onLog?.Invoke(line);
                lock (result._stderr)
                {
                    result._stderr.Enqueue(line);
                    if (result._stderr.Count > StderrLinesKept)
                    {
                        result._stderr.Dequeue();
                    }
                }
            }
        };
        process.BeginErrorReadLine();
        log.LogInformation("GTP engine {Engine} started (pid {Pid})", config.Name, process.Id);
        return result;
    }

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteAsync((line + "\n").AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    public Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        _process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // after "quit"
                try
                {
                    await _process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }

        _process.Dispose();
    }
}
