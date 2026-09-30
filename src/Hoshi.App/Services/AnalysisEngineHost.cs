using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services;

/// <summary>The analysis engine as the UI sees it (KataGo in production, a fake in tests).</summary>
public interface IAnalysisEngine
{
    /// <summary>Why analysis is unavailable (not configured, missing files…), or null when it can run.</summary>
    string? Problem { get; }

    int Visits { get; }

    /// <summary>What the engine is busy with before it can answer (loading, GPU tuning), or null.</summary>
    string? Activity => null;

    event EventHandler? Changed;

    /// <summary>Raised (on any thread) when <see cref="Activity"/> changes.</summary>
    event EventHandler? ActivityChanged
    {
        add { }
        remove { }
    }

    Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the KataGo analysis process configured in Preferences. The process starts on the first query and stays
/// alive; changing the paths restarts it.
/// </summary>
public sealed class AnalysisEngineHost : IAnalysisEngine, IAsyncDisposable
{
    private readonly ILoggerFactory _loggers;
    private KataGoAnalysisEngine? _engine;
    private KataGoOptions? _options;

    public AnalysisEngineHost(ISettingsService settings, ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loggers = loggers ?? NullLoggerFactory.Instance;
        Reconfigure(settings.Current);
    }

    public event EventHandler? Changed;

    public event EventHandler? ActivityChanged;

    public string? Activity { get; private set; }

    public string? Problem { get; private set; }

    public int Visits { get; private set; } = 200;

    public void Reconfigure(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var options = new KataGoOptions(settings.KataGoExecutable ?? string.Empty, settings.KataGoModel ?? string.Empty, settings.KataGoConfig ?? string.Empty);
        Visits = Math.Clamp(settings.AnalysisVisits, 10, 100_000);
        if (options == _options && _engine is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        KataGoAnalysisEngine? old = _engine;
        _engine = null;
        _options = options;
        Problem = string.IsNullOrWhiteSpace(settings.KataGoExecutable)
            ? "KataGo no está configurado (☰ → Preferencias → Análisis)."
            : options.Validate();
        if (Problem is null)
        {
            ILogger logger = _loggers.CreateLogger("KataGo");
            _engine = new KataGoAnalysisEngine(() => KataGoProcess.Start(options, logger, SetActivity), logger);
        }

        if (old is not null)
        {
            _ = old.DisposeAsync().AsTask();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken)
    {
        KataGoAnalysisEngine engine = _engine ?? throw new EngineException(Problem ?? "KataGo no está disponible.");
        IReadOnlyList<TurnAnalysis> result = await engine.AnalyzeAsync(query, cancellationToken);
        SetActivity(null); // An answer means KataGo is ready, whatever its log said.
        return result;
    }

    private void SetActivity(string? activity)
    {
        if (Activity != activity)
        {
            Activity = activity;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_engine is { } e)
        {
            await e.DisposeAsync();
        }
    }
}
