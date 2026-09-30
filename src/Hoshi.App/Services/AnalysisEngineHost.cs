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

    event EventHandler? Changed;

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
            _engine = new KataGoAnalysisEngine(() => KataGoProcess.Start(options, logger), logger);
        }

        if (old is not null)
        {
            _ = old.DisposeAsync().AsTask();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        _engine is { } engine
            ? engine.AnalyzeAsync(query, cancellationToken)
            : Task.FromException<IReadOnlyList<TurnAnalysis>>(new EngineException(Problem ?? "KataGo no está disponible."));

    public async ValueTask DisposeAsync()
    {
        if (_engine is { } e)
        {
            await e.DisposeAsync();
        }
    }
}
