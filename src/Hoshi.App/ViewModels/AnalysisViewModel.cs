using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>A move suggestion drawn on the board.</summary>
public sealed record BoardSuggestion(Point Point, string Label, string Detail, bool IsBest, double Strength);

/// <summary>
/// The "AI in the background": while analysis is on, every change of the shown position asks the engine for the
/// position and the one before it, to judge the last move (<see cref="MoveReview"/>), show the best continuations and
/// the engine's ownership map; the game's score graph is filled in batches at lower visits. Territory can be shown
/// with or without an engine (Hoshi's own estimator). Both are disabled while the user is playing on OGS, whose
/// rules forbid engine help during games.
/// </summary>
public sealed partial class AnalysisViewModel : ViewModelBase
{
    public const int GraphBatch = 25;

    private readonly GameViewModel _game;
    private readonly IAnalysisEngine? _engine;
    private readonly IUiDispatcher _ui;
    private readonly Dictionary<string, TurnAnalysis> _cache = [];
    private CancellationTokenSource? _current;
    private CancellationTokenSource? _graphCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnalysisActive), nameof(StatusText))]
    private bool _isAnalysisOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTerritoryActive), nameof(StatusText))]
    private bool _isTerritoryOn;

    [ObservableProperty]
    private TurnAnalysis? _analysis;

    [ObservableProperty]
    private MoveAssessment? _assessment;

    [ObservableProperty]
    private TerritoryEstimate? _territory;

    [ObservableProperty]
    private IReadOnlyList<BoardSuggestion> _suggestions = [];

    [ObservableProperty]
    private IReadOnlyList<double?> _graph = [];

    [ObservableProperty]
    private int _graphIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _error;

    public AnalysisViewModel(GameViewModel game, IAnalysisEngine? engine = null, IUiDispatcher? ui = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _engine = engine;
        _ui = ui ?? new AvaloniaUiDispatcher();
        _game.PropertyChanged += OnGamePropertyChanged;
        if (_engine is not null)
        {
            _engine.Changed += (_, _) => _ui.Post(() =>
            {
                _cache.Clear();
                OnPropertyChanged(nameof(StatusText));
                Refresh();
            });
        }
    }

    /// <summary>True while the user plays an OGS game: engine help is not allowed there.</summary>
    public bool IsBlocked => _game.Online is { IsPlayer: true, IsFinished: false };

    public bool IsAnalysisActive => IsAnalysisOn && !IsBlocked;

    public bool IsTerritoryActive => IsTerritoryOn && !IsBlocked;

    public bool HasEngine => _engine is not null && _engine.Problem is null;

    public string StatusText =>
        IsBlocked && (IsAnalysisOn || IsTerritoryOn) ? "Análisis desactivado mientras juegas en OGS (sus normas no permiten ayuda de IA)."
        : !IsAnalysisOn ? string.Empty
        : _engine?.Problem is { } problem ? problem
        : Error is { } e ? e
        : IsBusy ? "Analizando…"
        : string.Empty;

    /// <summary>"Negras 62 %" from the engine's winrate.</summary>
    public string WinrateText => Analysis is { } a
        ? a.Winrate >= 0.5
            ? string.Create(CultureInfo.InvariantCulture, $"Negras {a.Winrate * 100:0} %")
            : string.Create(CultureInfo.InvariantCulture, $"Blancas {(1 - a.Winrate) * 100:0} %")
        : string.Empty;

    public double BlackWinrate => Analysis?.Winrate ?? 0.5;

    public string LeadText => Analysis is { } a ? FormatLead(a.ScoreLead) : string.Empty;

    public string AssessmentText => Assessment is { } m
        ? $"{Name(m.Played)} · {QualityName(m.Quality)}{(m.PointsLost >= 0.05 && m.Quality != MoveQuality.Best ? string.Create(CultureInfo.InvariantCulture, $" (−{m.PointsLost:0.0})") : string.Empty)}"
        : string.Empty;

    public string AssessmentClass => Assessment?.Quality switch
    {
        MoveQuality.Best or MoveQuality.Excellent => "good",
        MoveQuality.Good => "ok",
        MoveQuality.Inaccuracy => "warn",
        MoveQuality.Mistake or MoveQuality.Blunder => "bad",
        _ => string.Empty,
    };

    public string BestText => Assessment is { Quality: not MoveQuality.Best } m
        ? $"Mejor: {Name(new EngineMove(m.Played.Color, m.Best.Point))} · {FormatLead(m.Best.ScoreLead)}"
        : Analysis?.Best is { } best ? $"Siguiente sugerida: {Name(new EngineMove(Analysis.ToMove, best.Point))}" : string.Empty;

    /// <summary>"Territorio: N 23 (+12) · B 19 (+9) → N+3.5".</summary>
    public string TerritoryText => Territory is { } t
        ? string.Create(CultureInfo.InvariantCulture,
            $"Negras {t.BlackSecure + t.DeadWhite} (+{t.BlackPotential:0}) · Blancas {t.WhiteSecure + t.DeadBlack} (+{t.WhitePotential:0}) · {FormatLead(t.Lead)}{(t.Source == EstimateSource.Heuristic ? " (estimación rápida)" : " (KataGo)")}")
        : string.Empty;

    [RelayCommand]
    private void ToggleAnalysis() => IsAnalysisOn = !IsAnalysisOn;

    /// <summary>Jumps to the position after <paramref name="moveIndex"/> moves on the shown line (graph clicks).</summary>
    [RelayCommand]
    private void GoToMove(int moveIndex)
    {
        GameCursor cursor = _game.Cursor;
        var track = GameCursor.Path(cursor.Current).ToList();
        for (GameNode n = cursor.Current; n.Children.Count > 0;)
        {
            n = n.Children[0];
            track.Add(n);
        }

        int moves = 0;
        GameNode target = track[0];
        foreach (GameNode node in track)
        {
            if (node.HasMove)
            {
                moves++;
            }

            if (moves > moveIndex)
            {
                break;
            }

            target = node;
        }

        _game.GoToNodeCommand.Execute(target);
    }

    [RelayCommand]
    private void ToggleTerritory() => IsTerritoryOn = !IsTerritoryOn;

    partial void OnIsAnalysisOnChanged(bool value)
    {
        if (!value)
        {
            Cancel();
            Analysis = null;
            Assessment = null;
            Suggestions = [];
            Graph = [];
            Error = null;
        }

        Refresh();
    }

    partial void OnIsTerritoryOnChanged(bool value) => UpdateTerritory();

    partial void OnAnalysisChanged(TurnAnalysis? value)
    {
        OnPropertyChanged(nameof(WinrateText));
        OnPropertyChanged(nameof(BlackWinrate));
        OnPropertyChanged(nameof(LeadText));
        OnPropertyChanged(nameof(BestText));
        UpdateTerritory();
    }

    partial void OnAssessmentChanged(MoveAssessment? value)
    {
        OnPropertyChanged(nameof(AssessmentText));
        OnPropertyChanged(nameof(AssessmentClass));
        OnPropertyChanged(nameof(BestText));
    }

    partial void OnTerritoryChanged(TerritoryEstimate? value) => OnPropertyChanged(nameof(TerritoryText));

    /// <summary>Re-evaluates the shown position (called on every navigation or edit).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsAnalysisActive));
        OnPropertyChanged(nameof(IsTerritoryActive));
        OnPropertyChanged(nameof(StatusText));
        UpdateTerritory();
        if (!IsAnalysisActive || !HasEngine)
        {
            Cancel();
            Suggestions = [];
            return;
        }

        Position pos = Position.Of(_game);
        int n = pos.Moves.Count;
        int[] needed = [.. new[] { n - 1, n }.Where(t => t >= 0 && !_cache.ContainsKey(pos.Key(t)))];
        ShowCached(pos);
        _current?.Cancel();
        if (needed.Length > 0)
        {
            var cts = new CancellationTokenSource();
            _current = cts;
            _ = RunAsync(pos, needed, _engine!.Visits, cts, isGraph: false);
        }
        else
        {
            StartGraph(pos);
        }
    }

    private void OnGamePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // TreeVersion changes once per navigation or edit (after CurrentNode, Board…).
        if (e.PropertyName is nameof(GameViewModel.TreeVersion))
        {
            Refresh();
        }
        else if (e.PropertyName is nameof(GameViewModel.Online) or nameof(GameViewModel.IsOnline))
        {
            Refresh();
        }
    }

    private async Task RunAsync(Position pos, int[] turns, int visits, CancellationTokenSource cts, bool isGraph)
    {
        if (!isGraph)
        {
            _ui.Post(() => { IsBusy = true; Error = null; });
            try
            {
                await Task.Delay(120, cts.Token); // debounce fast navigation
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        try
        {
            IReadOnlyList<TurnAnalysis> results = await _engine!.AnalyzeAsync(pos.Query(turns, visits), cts.Token);
            _ui.Post(() =>
            {
                foreach (TurnAnalysis t in results)
                {
                    string key = pos.Key(t.Turn);
                    // Keep the stronger result when a turn is analysed twice.
                    if (!_cache.TryGetValue(key, out TurnAnalysis? old) || old.Visits <= t.Visits)
                    {
                        _cache[key] = t;
                    }
                }

                if (!cts.IsCancellationRequested)
                {
                    Position now = Position.Of(_game);
                    ShowCached(now);
                    if (!isGraph)
                    {
                        IsBusy = false;
                        StartGraph(now);
                    }
                    else
                    {
                        StartGraph(now);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer position.
        }
        catch (EngineException ex)
        {
            _ui.Post(() => { IsBusy = false; Error = ex.Message; });
        }
    }

    private void StartGraph(Position pos)
    {
        UpdateGraph(pos);
        if (_graphCts is { IsCancellationRequested: false })
        {
            return; // a batch is already running; it will continue from the cache
        }

        Position track = Position.Of(_game, followMainLine: true);
        int[] missing = [.. Enumerable.Range(0, track.Moves.Count + 1).Where(t => !_cache.ContainsKey(track.Key(t))).Take(GraphBatch)];
        if (missing.Length == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _graphCts = cts;
        _ = RunGraphAsync(track, missing, cts);
    }

    private async Task RunGraphAsync(Position track, int[] turns, CancellationTokenSource cts)
    {
        await RunAsync(track, turns, Math.Max(20, _engine!.Visits / 4), cts, isGraph: true);
        _ui.Post(() =>
        {
            if (ReferenceEquals(_graphCts, cts))
            {
                _graphCts = null;
                if (!cts.IsCancellationRequested && IsAnalysisActive)
                {
                    StartGraph(Position.Of(_game));
                }
            }
        });
    }

    private void ShowCached(Position pos)
    {
        int n = pos.Moves.Count;
        _cache.TryGetValue(pos.Key(n), out TurnAnalysis? now);
        Analysis = now;
        Assessment = n > 0 && _cache.TryGetValue(pos.Key(n - 1), out TurnAnalysis? before)
            ? MoveReview.Assess(before, now, pos.Moves[n - 1])
            : null;
        Suggestions = now is null ? [] : [.. now.Candidates.Where(c => c.Point is not null).Take(3).Select((c, i) => Suggest(c, i, now))];
        UpdateGraph(pos);
    }

    private void UpdateGraph(Position pos)
    {
        Position track = Position.Of(_game, followMainLine: true);
        Graph = [.. Enumerable.Range(0, track.Moves.Count + 1).Select(t => _cache.TryGetValue(track.Key(t), out TurnAnalysis? a) ? (double?)a.ScoreLead : null)];
        GraphIndex = pos.Moves.Count;
    }

    private static BoardSuggestion Suggest(MoveCandidate c, int i, TurnAnalysis at)
    {
        double winForMover = at.ToMove == Stone.Black ? c.Winrate : 1 - c.Winrate;
        double leadForMover = at.ToMove == Stone.Black ? c.ScoreLead : -c.ScoreLead;
        return new BoardSuggestion(
            c.Point!.Value,
            string.Create(CultureInfo.InvariantCulture, $"{winForMover * 100:0}"),
            string.Create(CultureInfo.InvariantCulture, $"{(leadForMover >= 0 ? "+" : "−")}{Math.Abs(leadForMover):0.0}"),
            i == 0,
            1.0 - (i * 0.3));
    }

    private void UpdateTerritory()
    {
        if (!IsTerritoryActive)
        {
            Territory = null;
            return;
        }

        BoardState board = _game.Board;
        double komi = _game.Tree.Info.Komi ?? 0;
        Territory = Analysis?.Ownership is { } own && own.Count == board.Width * board.Height && IsAnalysisActive
            ? TerritoryEstimate.FromOwnership(board, own, komi)
            : TerritoryEstimator.Estimate(board, komi);
    }

    private void Cancel()
    {
        _current?.Cancel();
        _graphCts?.Cancel();
        _graphCts = null;
        IsBusy = false;
    }

    private string Name(EngineMove m) =>
        (m.Color == Stone.Black ? "N " : "B ") + (m.Point is { } p ? p.ToHuman(_game.Board.Height) : "pase");

    private static string QualityName(MoveQuality q) => q switch
    {
        MoveQuality.Best => "la mejor jugada",
        MoveQuality.Excellent => "excelente",
        MoveQuality.Good => "buena",
        MoveQuality.Inaccuracy => "imprecisa",
        MoveQuality.Mistake => "error",
        _ => "error grave",
    };

    private static string FormatLead(double blackLead) => Math.Abs(blackLead) < 0.05
        ? "igualada"
        : string.Create(CultureInfo.InvariantCulture, $"{(blackLead > 0 ? "N" : "B")}+{Math.Abs(blackLead):0.0}");

    /// <summary>The shown position as the engine sees it: setup at the root plus the moves that led here.</summary>
    private sealed record Position(AnalysisQuery Base, IReadOnlyList<EngineMove> Moves, string Prefix)
    {
        public static Position Of(GameViewModel game, bool followMainLine = false)
        {
            GameCursor cursor = game.Cursor;
            IReadOnlyList<GameNode> path = GameCursor.Path(cursor.Current);
            if (followMainLine)
            {
                var extended = path.ToList();
                GameNode n = cursor.Current;
                while (n.Children.Count > 0)
                {
                    n = n.Children[0];
                    extended.Add(n);
                }

                path = extended;
            }

            GameTree tree = game.Tree;
            RuleSet rules = tree.Info.Rules ?? RuleSet.Japanese;
            double komi = tree.Info.Komi ?? 0;
            BoardState rootBoard = cursor.GetBoard(tree.Root);
            bool setupAfterRoot = path.Skip(1).Any(n => n.HasProperty("AB") || n.HasProperty("AW") || n.HasProperty("AE"));

            // A position edited mid-game has no clean move history: send it as a plain position.
            BoardState start = setupAfterRoot ? cursor.GetBoard(path[^1]) : rootBoard;
            var stones = start.AllPoints.Where(p => start[p] != Stone.Empty).Select(p => (p, start[p])).ToList();
            var moves = new List<EngineMove>();
            if (!setupAfterRoot)
            {
                foreach (GameNode node in path.Skip(1))
                {
                    if (node.GetMove(cursor.BoardSize) is { } m)
                    {
                        moves.Add(new EngineMove(m.Color, m.Point));
                    }
                }
            }

            var prefix = new StringBuilder();
            prefix.Append(CultureInfo.InvariantCulture, $"{start.Width}x{start.Height}|{rules.Name}|{komi}|{start.ToMove}|");
            foreach ((Point p, Stone s) in stones)
            {
                prefix.Append(s == Stone.Black ? 'b' : 'w').Append(p.ToSgf());
            }

            return new Position(
                new AnalysisQuery
                {
                    Width = start.Width,
                    Height = start.Height,
                    Rules = rules,
                    Komi = komi,
                    InitialStones = stones,
                    InitialPlayer = start.ToMove,
                },
                moves,
                prefix.ToString());
        }

        public string Key(int turn)
        {
            var sb = new StringBuilder(Prefix).Append('|');
            foreach (EngineMove m in Moves.Take(turn))
            {
                sb.Append(m.Color == Stone.Black ? 'B' : 'W').Append(m.Point?.ToSgf() ?? "..");
            }

            return sb.ToString();
        }

        public AnalysisQuery Query(IReadOnlyList<int> turns, int visits) => Base with { Moves = Moves, Turns = turns, MaxVisits = visits };
    }
}
