using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
            return "No se encuentra el ejecutable de KataGo.";
        }

        if (string.IsNullOrWhiteSpace(Model) || !File.Exists(Model))
        {
            return "No se encuentra la red neuronal (.bin.gz) de KataGo.";
        }

        if (string.IsNullOrWhiteSpace(Config) || !File.Exists(Config))
        {
            return "No se encuentra el archivo de configuración de análisis (analysis_example.cfg).";
        }

        return IsAnalysisConfig(Config)
            ? null
            : $"«{Path.GetFileName(Config)}» no es una configuración de análisis (es para GTP). Elige analysis_example.cfg, en la misma carpeta de KataGo.";
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
            process = Process.Start(info) ?? throw new EngineException("KataGo no arrancó.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new EngineException($"No se pudo iniciar KataGo: {ex.Message}", ex);
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

    private const string Tuning = "KataGo está calibrando la tarjeta gráfica. Solo pasa la primera vez y puede tardar varios minutos";

    /// <summary>
    /// What KataGo is busy with before it can answer, from a stderr line: loading the network, or the one-time
    /// OpenCL autotuning (which can take minutes). Null once it is ready for queries.
    /// </summary>
    internal static string? ActivityFrom(string line, string? current)
    {
        if (line.Contains("ready to begin handling requests", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (line.Contains("Performing autotuning", StringComparison.Ordinal) || line.StartsWith("Beginning GPU tuning", StringComparison.Ordinal))
        {
            return Tuning + "…";
        }

        if (current?.StartsWith(Tuning, StringComparison.Ordinal) == true)
        {
            if (line.StartsWith("Tuning ", StringComparison.Ordinal) && line.Split(' ', 3) is [_, var step, ..] && step.Contains('/', StringComparison.Ordinal))
            {
                return $"{Tuning} (paso {step})…";
            }

            return current;
        }

        return line.Contains("Initializing neural net", StringComparison.Ordinal) || line.Contains("nnModelFile", StringComparison.Ordinal)
            ? "KataGo está cargando la red neuronal…"
            : current;
    }

    /// <summary>Turns an exit code and KataGo's last stderr lines into a message for the user.</summary>
    internal static string Describe(int exitCode, IReadOnlyList<string> stderr)
    {
        string? hint = unchecked((uint)exitCode) switch
        {
            0xC0000135 => "falta una DLL. Las versiones CUDA y TensorRT de KataGo necesitan CUDA, cuDNN o TensorRT instalados; si no los tienes, usa la versión OpenCL (o Eigen, solo CPU).",
            0xC000001D => "tu procesador no admite las instrucciones de esta versión de KataGo. Usa la versión Eigen sin AVX2.",
            0xC0000005 => "KataGo falló dentro del controlador de la tarjeta gráfica. Actualiza el controlador o usa la versión Eigen (solo CPU).",
            0xC0000409 => "KataGo abortó. Revisa que la red neuronal sea compatible con tu versión de KataGo.",
            _ => null,
        };

        string[] errors = [.. stderr.Where(IsErrorLine).TakeLast(2)];
        string detail = errors.Length > 0 ? string.Join(" · ", errors) : string.Join(" · ", stderr.TakeLast(2));
        string code = exitCode >= 0 && exitCode < 256
            ? exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "0x" + unchecked((uint)exitCode).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return hint is not null
            ? $"código {code}: {hint}" + (detail.Length > 0 ? $" ({detail})" : string.Empty)
            : detail.Length > 0 ? $"código {code}: {detail}" : $"código {code}, sin mensaje de KataGo.";
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
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
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
