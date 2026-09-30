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

        return string.IsNullOrWhiteSpace(Config) || !File.Exists(Config)
            ? "No se encuentra el archivo de configuración de análisis (analysis_example.cfg)."
            : null;
    }
}

/// <summary>
/// Runs <c>katago analysis -config … -model … -override-config reportAnalysisWinratesAs=BLACK</c>, so every value
/// Hoshi receives is from black's point of view. stderr (KataGo's log) is drained to the logger.
/// </summary>
public sealed class KataGoProcess : IEngineProcess
{
    private readonly Process _process;

    private KataGoProcess(Process process) => _process = process;

    public bool HasExited => _process.HasExited;

    public static KataGoProcess Start(KataGoOptions options, ILogger? logger = null)
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

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { Length: > 0 } line)
            {
                log.LogDebug("KataGo: {Line}", line);
            }
        };
        process.BeginErrorReadLine();
        log.LogInformation("KataGo analysis engine started (pid {Pid})", process.Id);
        return new KataGoProcess(process);
    }

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
