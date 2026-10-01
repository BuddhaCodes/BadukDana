using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Hoshi.Core.Localization;

namespace Hoshi.Engines.KataGo;

/// <summary>A line-based engine process (stdin/stdout); faked in tests.</summary>
public interface IEngineProcess : IAsyncDisposable
{
    bool HasExited { get; }

    Task WriteLineAsync(string line, CancellationToken cancellationToken);

    /// <summary>Next stdout line, or null when the process has ended.</summary>
    Task<string?> ReadLineAsync(CancellationToken cancellationToken);

    /// <summary>Once the process has exited: a short, user-facing explanation (exit code, last error lines).</summary>
    string? ExitReason => null;
}

/// <summary>Paths needed to run KataGo's analysis engine.</summary>
public sealed record KataGoOptions(string Executable, string Model, string Config)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Executable) || !File.Exists(Executable))
        {
            return Tr.T("Engine.ExecutableMissing");
        }

        if (string.IsNullOrWhiteSpace(Model) || !File.Exists(Model))
        {
            return Tr.T("Engine.ModelMissing");
        }

        if (string.IsNullOrWhiteSpace(Config) || !File.Exists(Config))
        {
            return Tr.T("Engine.ConfigMissing");
        }

        return IsAnalysisConfig(Config)
            ? null
            : Tr.F("Engine.NotAnalysisConfig", Path.GetFileName(Config));
    }

    /// <summary>KataGo's analysis engine requires <c>numAnalysisThreads</c>; GTP configs (gtp_*.cfg) do not have it.</summary>
    internal static bool IsAnalysisConfig(string path)
    {
        try
        {
            return File.ReadLines(path).Any(l => l.TrimStart().StartsWith("numAnalysisThreads", StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return true; // Let KataGo report it.
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}

/// <summary>
/// Runs <c>katago analysis -config … -model … -override-config reportAnalysisWinratesAs=BLACK</c>, so every value
/// Hoshi receives is from black's point of view. stderr (KataGo's log) is drained to the logger.
/// </summary>
public sealed class KataGoProcess : IEngineProcess
{
    private const int StderrLinesKept = 40;
    private readonly Process _process;
    private readonly Queue<string> _stderr = new();

    private KataGoProcess(Process process) => _process = process;

    /// <summary>
    /// Why the process ended, from its exit code and the last lines KataGo wrote to stderr, or null while it runs.
    /// </summary>
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

                // Let the asynchronous stderr reader drain before looking at the last lines.
                _process.WaitForExit(2000);
                string[] lines;
                lock (_stderr)
                {
                    lines = [.. _stderr];
                }

                return Describe(_process.ExitCode, lines);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    public bool HasExited => _process.HasExited;

    public static KataGoProcess Start(KataGoOptions options, ILogger? logger = null, Action<string?>? onActivity = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ILogger log = logger ?? NullLogger.Instance;
        if (options.Validate() is { } problem)
        {
            throw new EngineException(problem);
        }

        var info = new ProcessStartInfo(options.Executable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(options.Executable) ?? Environment.CurrentDirectory,
        };
        foreach (string a in (string[])["analysis", "-config", options.Config, "-model", options.Model, "-override-config", "reportAnalysisWinratesAs=BLACK"])
        {
            info.ArgumentList.Add(a);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new EngineException(Tr.T("Engine.DidNotStart"));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new EngineException(Tr.F("Engine.CouldNotStart", ex.Message), ex);
        }

        var result = new KataGoProcess(process);
        string? activity = null;
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                onActivity?.Invoke(null);
                return;
            }

            if (e.Data is { Length: > 0 } line)
            {
                log.LogInformation("KataGo stderr: {Line}", line);
                string? next = ActivityFrom(line, activity);
                if (next != activity)
                {
                    activity = next;
                    onActivity?.Invoke(activity);
                }

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
        log.LogInformation("KataGo analysis engine started (pid {Pid}): {Executable}", process.Id, options.Executable);
        return result;
    }

    /// <summary>
    /// What KataGo is busy with before it can answer, from a stderr line: loading the network, or the one-time
    /// OpenCL autotuning (which can take minutes). Null once it is ready for queries. Texts use the current language.
    /// </summary>
    internal static string? ActivityFrom(string line, string? current) => ActivityFrom(line, current, Tr.Language);

    /// <summary>
    /// <see cref="ActivityFrom(string, string?)"/> in a given language. A tuning text in either language counts as
    /// "tuning in progress", so switching language mid-tuning keeps the progress line.
    /// </summary>
    internal static string? ActivityFrom(string line, string? current, string language)
    {
        string tuning = Tr.T("Engine.Tuning", language);
        if (line.Contains("ready to begin handling requests", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Contains("Performing autotuning", StringComparison.Ordinal) || line.StartsWith("Beginning GPU tuning", StringComparison.Ordinal))
        {
            return tuning + "…";
        }

        if (current is not null && Tr.Languages.Any(l => current.StartsWith(Tr.T("Engine.Tuning", l), StringComparison.Ordinal)))
        {
            if (line.StartsWith("Tuning ", StringComparison.Ordinal) && line.Split(' ', 3) is [_, var step, ..] && step.Contains('/', StringComparison.Ordinal))
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture, Tr.T("Engine.TuningStep", language), tuning, step);
            }

            return current;
        }

        return line.Contains("Initializing neural net", StringComparison.Ordinal) || line.Contains("nnModelFile", StringComparison.Ordinal)
            ? Tr.T("Engine.LoadingNetwork", language)
            : current;
    }

    /// <summary>Turns an exit code and KataGo's last stderr lines into a message for the user.</summary>
    internal static string Describe(int exitCode, IReadOnlyList<string> stderr)
    {
        string? hint = unchecked((uint)exitCode) switch
        {
            0xC0000135 => Tr.T("Engine.Hint.MissingDll"),
            0xC000001D => Tr.T("Engine.Hint.IllegalInstruction"),
            0xC0000005 => Tr.T("Engine.Hint.AccessViolation"),
            0xC0000409 => Tr.T("Engine.Hint.Aborted"),
            _ => null,
        };

        string[] errors = [.. stderr.Where(IsErrorLine).TakeLast(2)];
        string detail = errors.Length > 0 ? string.Join(" · ", errors) : string.Join(" · ", stderr.TakeLast(2));
        string code = exitCode >= 0 && exitCode < 256
            ? exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "0x" + unchecked((uint)exitCode).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return hint is not null
            ? Tr.F("Engine.ExitCode", code, hint) + (detail.Length > 0 ? $" ({detail})" : string.Empty)
            : detail.Length > 0 ? Tr.F("Engine.ExitCode", code, detail) : Tr.F("Engine.ExitCodeNoMessage", code);
    }

    private static bool IsErrorLine(string line) =>
        line.Contains("error", StringComparison.OrdinalIgnoreCase)
        || line.Contains("exception", StringComparison.OrdinalIgnoreCase)
        || line.Contains("could not", StringComparison.OrdinalIgnoreCase)
        || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
        || line.Contains("cannot", StringComparison.OrdinalIgnoreCase)
        || line.Contains("unknown", StringComparison.OrdinalIgnoreCase);

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
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
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1.5)); // KataGo may finish searches first
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

public sealed class EngineException : Exception
{
    public EngineException()
    {
    }

    public EngineException(string message)
        : base(message)
    {
    }

    public EngineException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
