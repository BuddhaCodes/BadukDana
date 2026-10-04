using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;

namespace Hoshi.App.Services.Engines;

/// <summary>
/// The analysis panel's engine, chosen in Preferences → Engines: Hoshi's KataGo (JSON analysis engine) by default,
/// or any GTP engine with <c>lz-analyze</c> / <c>kata-analyze</c> (Leela Zero, SAI, KataGo…).
/// </summary>
public sealed class AnalysisEngineSwitch : IAnalysisEngine
{
    private readonly AnalysisEngineHost _kataGo;
    private readonly IGtpEngineHost _gtp;
    private readonly ISettingsService _settings;
    private GtpAnalysisEngine? _adapter;

    public AnalysisEngineSwitch(AnalysisEngineHost kataGo, IGtpEngineHost gtp, ISettingsService settings)
    {
        _kataGo = kataGo ?? throw new ArgumentNullException(nameof(kataGo));
        _gtp = gtp ?? throw new ArgumentNullException(nameof(gtp));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _kataGo.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _kataGo.ActivityChanged += (_, _) => ActivityChanged?.Invoke(this, EventArgs.Empty);
        _gtp.EnginesChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public event EventHandler? ActivityChanged;

    /// <summary>The GTP engine in use, or null for Hoshi's KataGo.</summary>
    public EngineChoice? GtpChoice =>
        _settings.Current.AnalysisEngine is { } id && _gtp.Find(id) is { IsBuiltIn: false } choice ? choice : null;

    private IAnalysisEngine Current
    {
        get
        {
            if (GtpChoice is not { } choice)
            {
                return _kataGo;
            }

            if (_adapter is null || _adapter.Choice != choice)
            {
                _adapter = new GtpAnalysisEngine(_gtp, choice);
            }

            return _adapter;
        }
    }

    public string? Problem => Current.Problem;

    public int Visits => _kataGo.Visits;

    public string? Activity => GtpChoice is null ? _kataGo.Activity : null;

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        Current.AnalyzeAsync(query, cancellationToken);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeLiveAsync(AnalysisQuery query, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken) =>
        Current.AnalyzeLiveAsync(query, onUpdate, cancellationToken);

    /// <summary>Preferences changed the choice.</summary>
    public void Reconfigure() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Runs analysis queries on a GTP engine: each turn is streamed until it reaches the visits asked for.</summary>
public sealed class GtpAnalysisEngine(IGtpEngineHost host, EngineChoice choice) : IAnalysisEngine
{
    /// <summary>Longest time spent on one position, whatever the visits (slow engines, CPU builds).</summary>
    public static TimeSpan MaxTimePerTurn { get; set; } = TimeSpan.FromSeconds(20);

    public EngineChoice Choice { get; } = choice;

    public string? Problem => Choice.Config.Validate();

    public int Visits => 200;

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        RunAsync(query, null, cancellationToken);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeLiveAsync(AnalysisQuery query, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken) =>
        RunAsync(query, onUpdate, cancellationToken);

    private async Task<IReadOnlyList<TurnAnalysis>> RunAsync(AnalysisQuery query, Action<TurnAnalysis>? onUpdate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        GtpEngine engine = await host.AcquireAsync(Choice, cancellationToken);
        IReadOnlyList<int> turns = query.Turns.Count > 0 ? query.Turns : [query.Moves.Count];
        var results = new List<TurnAnalysis>();
        foreach (int turn in turns.OrderBy(t => t))
        {
            cancellationToken.ThrowIfCancellationRequested();
            GtpPosition position = Position(query, turn);
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            stop.CancelAfter(MaxTimePerTurn);
            TurnAnalysis? last = null;
            try
            {
                await engine.AnalyzeAsync(position, turn, TimeSpan.FromSeconds(0.25), a =>
                {
                    last = a;
                    onUpdate?.Invoke(a);
                    if (a.Visits >= query.MaxVisits)
                    {
                        stop.Cancel();
                    }
                }, stop.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Enough visits, or out of time: keep what came.
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Add(last ?? throw new EngineException(Tr.F("Engines.NoAnalysisResult", engine.DisplayName)));
        }

        return results;
    }

    internal static GtpPosition Position(AnalysisQuery query, int turn)
    {
        int n = Math.Clamp(turn, 0, query.Moves.Count);
        Stone toMove = n == 0 ? query.InitialPlayer : query.Moves[n - 1].Color == Stone.Black ? Stone.White : Stone.Black;
        return new GtpPosition
        {
            Width = query.Width,
            Height = query.Height,
            Komi = query.Komi,
            Rules = query.Rules,
            InitialStones = query.InitialStones,
            Moves = [.. query.Moves.Take(n)],
            ToMove = toMove,
        };
    }
}
