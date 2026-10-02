using Hoshi.Engines.KataGo;
using Hoshi.Core.Localization;
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

    /// <summary>Like <see cref="AnalyzeAsync"/>, also reporting partial results while the engine searches.</summary>
    Task<IReadOnlyList<TurnAnalysis>> AnalyzeLiveAsync(AnalysisQuery query, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken) =>
        AnalyzeAsync(query, cancellationToken);
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

    private readonly string _baseDirectory;
    private readonly string _dataDirectory;

    public AnalysisEngineHost(ISettingsService settings, ILoggerFactory? loggers = null, string? baseDirectory = null, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loggers = loggers ?? NullLoggerFactory.Instance;
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        _dataDirectory = dataDirectory ?? AppPaths.DataDirectory;
        Reconfigure(settings.Current);
    }

    public event EventHandler? Changed;

    public event EventHandler? ActivityChanged;

    public string? Activity { get; private set; }

    public string? Problem { get; private set; }

    public int Visits { get; private set; } = 200;

    /// <summary>Where the running KataGo comes from (null when there is none).</summary>
    public KataGo.KataGoSource? Source { get; private set; }

    public void Reconfigure(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        KataGo.ResolvedKataGo? resolved = KataGo.KataGoLocator.Resolve(settings, _baseDirectory, _dataDirectory);
        KataGoOptions options = resolved?.Options ?? new KataGoOptions(string.Empty, string.Empty, string.Empty);
        Visits = Math.Clamp(settings.AnalysisVisits, 10, 100_000);
        if (options == _options && _engine is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        KataGoAnalysisEngine? old = _engine;
        _engine = null;
        _options = options;
        Source = resolved?.Source;
        Problem = resolved is null ? Tr.T("Engine.NotConfigured") : options.Validate();
        ILogger logger = _loggers.CreateLogger("KataGo");
        if (Problem is not null)
        {
            logger.LogWarning("KataGo unavailable ({Source}): {Problem}", resolved?.Source.ToString() ?? "none", Problem);
        }
        else
        {
            logger.LogInformation("Using {Source} KataGo: {Executable}", resolved!.Source, options.Executable);
            _engine = new KataGoAnalysisEngine(() => KataGoProcess.Start(options, logger, SetActivity), logger);
        }

        if (old is not null)
        {
            _ = old.DisposeAsync().AsTask();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        RunAsync(query, null, cancellationToken);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeLiveAsync(AnalysisQuery query, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken) =>
        RunAsync(query, onUpdate, cancellationToken);

    private async Task<IReadOnlyList<TurnAnalysis>> RunAsync(AnalysisQuery query, Action<TurnAnalysis>? onUpdate, CancellationToken cancellationToken)
    {
        KataGoAnalysisEngine engine = _engine ?? throw new EngineException(Problem ?? Tr.T("Engine.Unavailable"));
        IReadOnlyList<TurnAnalysis> result = await engine.AnalyzeAsync(
            query,
            cancellationToken,
            onUpdate is null ? null : t =>
            {
                SetActivity(null); // Partial results mean KataGo is ready, whatever its log said.
                onUpdate(t);
            });
        SetActivity(null);
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
        KataGoAnalysisEngine? e = Interlocked.Exchange(ref _engine, null);
        if (e is not null)
        {
            await e.DisposeAsync();
        }
    }
}
