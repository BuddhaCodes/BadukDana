using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>A judged move: its quality, how big a moment it was (0–3, see <see cref="MoveImportance"/>) and whether it was in the opening.</summary>
public sealed record MoveWeight(MoveQuality Quality, int Importance, bool Opening);

/// <summary>A move suggestion drawn on the board.</summary>
public sealed record BoardSuggestion(Point Point, string Label, string Detail, bool IsBest, double Strength);

/// <summary>A strong move to celebrate on the board: strength 3 = the engine's best, 2 = excellent, 1 = good.</summary>
public sealed record BoardImpact(Point Point, int Strength, int Id);

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

    /// <summary>KataGo reports the shown position this often while it keeps searching (live analysis).</summary>
    public const double LiveReportSeconds = 0.25;

    /// <summary>Visits a partial analysis needs before its verdict on a move is trusted enough to celebrate.</summary>
    public const int JudgeVisits = 40;

    private const int LivePriority = 10;
    private const int GraphPriority = -10;

    private readonly GameViewModel _game;
    private readonly IAnalysisEngine? _engine;
    private readonly IUiDispatcher _ui;
    private readonly Dictionary<string, TurnAnalysis> _cache = [];

    // Positions whose search reached the configured visits; the others in the cache are partial (live or graph).
    private readonly HashSet<string> _complete = [];
    private readonly ISoundService? _sounds;
    private readonly ISettingsService? _settings;
    private GameNode? _awaitingJudgement;
    private bool _engineReady;
    private GameNode? _celebratedNode;
    private readonly FightMeter _fight = new();
    private int _impacts;
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
    private BoardImpact? _impact;

    /// <summary>
    /// Groups that are not safe, shown as a quiet halo on the board: from KataGo's ownership map when it has read
    /// the shown position, otherwise from short liberties in a fight. Empty when switched off, with effects off, or
    /// during the user's live OGS game.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<GroupStatus> _groupStatuses = [];

    /// <summary>Weak groups (and the atari sound) are on and allowed (not while the user plays a live OGS game).</summary>
    public bool IsAtariAlertActive =>
        (_settings?.Current ?? new AppSettings()) is { AtariAlerts: true, Effects: not EffectsLevel.Off } && !IsBlocked;

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

    public AnalysisViewModel(
        GameViewModel game,
        IAnalysisEngine? engine = null,
        IUiDispatcher? ui = null,
        ISoundService? sounds = null,
        ISettingsService? settings = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _engine = engine;
        _ui = ui ?? new AvaloniaUiDispatcher();
        _sounds = sounds;
        _settings = settings;
        _game.PropertyChanged += OnGamePropertyChanged;
        _game.MovePlayed += OnMovePlayed;
        _game.MoveSettled += OnMoveSettled;
        if (_engine is not null)
        {
            _engine.Changed += (_, _) => _ui.Post(() =>
            {
                _cache.Clear();
                _complete.Clear();
                _engineReady = false;
                OnPropertyChanged(nameof(StatusText));
                Refresh();
            });
            _engine.ActivityChanged += (_, _) => _ui.Post(() =>
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(WarmupText));
            });

            // Wake KataGo up as soon as the app opens.
            _ui.Post(Refresh);
        }
    }

    /// <summary>True while the user plays an OGS game: engine help is not allowed there.</summary>
    public bool IsBlocked => _game.Online is { IsPlayer: true, IsFinished: false };

    /// <summary>The analysis panel and the suggestions on the board are shown (button "Análisis", key A).</summary>
    public bool IsAnalysisActive => IsAnalysisOn && !IsBlocked;

    /// <summary>
    /// KataGo runs in the background whenever it is configured and allowed (not in live OGS games), from the moment
    /// the app opens, so verdicts, effects and music work even with the panel hidden.
    /// </summary>
    public bool IsEngineActive => HasEngine && !IsBlocked;

    /// <summary>Suggestions to draw on the board: only while the analysis is shown.</summary>
    public IReadOnlyList<BoardSuggestion> BoardSuggestions => IsAnalysisActive ? Suggestions : [];

    /// <summary>KataGo is starting (loading the network, tuning the GPU) and has not answered yet.</summary>
    public bool IsWarmingUp => IsEngineActive && !_engineReady && Error is null;

    public string WarmupText => _engine?.Activity is { } activity
        ? activity.Replace(Tr.T("Analysis.EnginePrefix"), string.Empty, StringComparison.Ordinal) is var a && a.Length > 0 ? char.ToUpperInvariant(a[0]) + a[1..] : activity
        : Tr.T("Analysis.WakingUp");

    /// <summary>
    /// Territory is allowed everywhere; during live OGS games it is always Hoshi's own quick estimate (no engine),
    /// since KataGo is off there (decision 2026-10-01).
    /// </summary>
    public bool IsTerritoryActive => IsTerritoryOn;

    public bool HasEngine => _engine is not null && _engine.Problem is null;

    public string StatusText =>
        IsBlocked && IsAnalysisOn ? Tr.T("Analysis.BlockedOnline")
        : !IsAnalysisOn ? string.Empty
        : _engine?.Problem is { } problem ? problem
        : Error is { } e ? e
        : IsBusy ? _engine?.Activity ?? (Analysis is { } a
            ? Tr.F("Analysis.LiveVisits", a.Visits)
            : Tr.T("Analysis.Analyzing"))
        : string.Empty;

    /// <summary>"Negras 62 %" from the engine's winrate.</summary>
    public string WinrateText => Analysis is { } a
        ? a.Winrate >= 0.5
            ? Tr.F("Analysis.WinrateBlack", a.Winrate * 100)
            : Tr.F("Analysis.WinrateWhite", (1 - a.Winrate) * 100)
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
        ? Tr.F("Analysis.Best", Name(new EngineMove(m.Played.Color, m.Best.Point)), FormatLead(m.Best.ScoreLead))
        : Analysis?.Best is { } best ? Tr.F("Analysis.NextSuggested", Name(new EngineMove(Analysis.ToMove, best.Point))) : string.Empty;

    /// <summary>"Territorio: N 23 (+12) · B 19 (+9) → N+3.5".</summary>
    public string TerritoryText => Territory is { } t
        ? Tr.F("Analysis.Territory", t.BlackSecure + t.DeadWhite, t.BlackPotential, t.WhiteSecure + t.DeadBlack, t.WhitePotential, FormatLead(t.Lead))
            + (t.Source == EstimateSource.Heuristic ? Tr.T("Analysis.QuickEstimate") : " (KataGo)")
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
        // Only what is shown changes: the engine keeps analysing in the background.
        OnPropertyChanged(nameof(BoardSuggestions));
        if (value && IsEngineActive)
        {
            StartGraph(Position.Of(_game));
        }
        else
        {
            _graphCts?.Cancel();
            _graphCts = null;
        }

        Refresh();
    }

    partial void OnSuggestionsChanged(IReadOnlyList<BoardSuggestion> value) => OnPropertyChanged(nameof(BoardSuggestions));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(IsWarmingUp));

    partial void OnIsTerritoryOnChanged(bool value) => UpdateTerritory();

    partial void OnAnalysisChanged(TurnAnalysis? value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(WinrateText));
        OnPropertyChanged(nameof(BlackWinrate));
        OnPropertyChanged(nameof(LeadText));
        OnPropertyChanged(nameof(BestText));
        UpdateTerritory();
        UpdateGroups();
    }

    private void UpdateGroups()
    {
        if (!IsAtariAlertActive)
        {
            GroupStatuses = [];
            return;
        }

        IReadOnlyList<double>? ownership = IsEngineActive && Analysis is { Ownership: { } own } a && a.Turn == _game.MoveNumber ? own : null;
        GroupStatuses = GroupStrength.Assess(_game.Board, ownership);
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
        UpdateGroups();
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(IsAnalysisActive));
        OnPropertyChanged(nameof(IsEngineActive));
        OnPropertyChanged(nameof(IsWarmingUp));
        OnPropertyChanged(nameof(BoardSuggestions));
        OnPropertyChanged(nameof(IsTerritoryActive));
        OnPropertyChanged(nameof(StatusText));
        UpdateTerritory();
        if (!IsEngineActive)
        {
            Cancel();
            Suggestions = [];
            return;
        }

        Position pos = Position.Of(_game);
        int n = pos.Moves.Count;
        int[] needed = [.. new[] { n - 1, n }.Where(t => t >= 0 && !_complete.Contains(pos.Key(t)))];
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
            if (_awaitingJudgement is not null && _awaitingJudgement != _game.CurrentNode)
            {
                _awaitingJudgement = null; // navigated away before the engine answered
            }

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
            AnalysisQuery query = pos.Query(turns, visits) with
            {
                Priority = isGraph ? GraphPriority : LivePriority,
                ReportDuringSearchEvery = isGraph ? null : LiveReportSeconds,
            };
            IReadOnlyList<TurnAnalysis> results = isGraph
                ? await _engine!.AnalyzeAsync(query, cts.Token)
                : await _engine!.AnalyzeLiveAsync(query, partial => _ui.Post(() => Store(pos, partial, complete: false, cts)), cts.Token);
            _ui.Post(() =>
            {
                foreach (TurnAnalysis t in results)
                {
                    Store(pos, t, complete: !isGraph, cts: null);
                }

                if (!cts.IsCancellationRequested)
                {
                    Position now = Position.Of(_game);
                    ShowCached(now);
                    if (!isGraph)
                    {
                        IsBusy = false;
                    }

                    if (IsAnalysisActive)
                    {
                        StartGraph(now); // the game graph is only filled while it is shown
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer position; its partial results stay in the cache.
        }
        catch (EngineException ex)
        {
            _ui.Post(() => { IsBusy = false; Error = ex.Message; });
        }
    }

    /// <summary>Caches a result (keeping the deeper one) and, for live updates of the shown position, redraws.</summary>
    private void Store(Position pos, TurnAnalysis t, bool complete, CancellationTokenSource? cts)
    {
        if (!_engineReady)
        {
            _engineReady = true;
            OnPropertyChanged(nameof(IsWarmingUp));
        }

        string key = pos.Key(t.Turn);
        if (!_cache.TryGetValue(key, out TurnAnalysis? old) || old.Visits <= t.Visits || complete)
        {
            _cache[key] = t;
        }

        if (complete)
        {
            _complete.Add(key);
        }

        if (cts is { IsCancellationRequested: false })
        {
            ShowCached(Position.Of(_game));
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
                if (!cts.IsCancellationRequested && IsAnalysisActive && IsEngineActive)
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
        TryCelebrate(pos);
    }

    /// <summary>The engine's (trusted) verdict on a move the user just played.</summary>
    public event EventHandler<MoveAssessment>? MoveJudged;

    /// <summary>The verdict with how much the moment mattered; drives the adaptive music.</summary>
    public event EventHandler<MoveWeight>? MoveWeighed;

    /// <summary>The battle heat (0–5) after a move, when effects follow the fight instead of the engine.</summary>
    public event EventHandler<double>? BattleHeat;

    /// <summary>The last fight reading (for tests and a possible indicator).</summary>
    public FightReading? LastFight { get; private set; }

    /// <summary>How big the last celebration was allowed to be (0–3), for tests and the music.</summary>
    public int LastImportance { get; private set; }

    private (GameNode Node, double Intensity)? _fightAt;

    private void OnMoveSettled(object? sender, PlacedMove move)
    {
        // The fight around the move, from the board alone: it decides how big a celebration may be (with the
        // engine) or drives the effects and music by itself (without it).
        FightReading reading = _fight.OnMove(move.Before, move.After, move.Point);
        LastFight = reading;
        _fightAt = (move.Node, reading.Intensity);
        if (IsEngineActive)
        {
            // Reviewing a replay: every step forward is judged by KataGo and celebrated like a move just played.
            if (_game.IsReview && _awaitingJudgement != move.Node)
            {
                _awaitingJudgement = move.Node;
                TryCelebrate(Position.Of(_game));
            }

            return;
        }

        // No engine (live OGS games, or KataGo not set up): effects and music follow the fight on the board —
        // contact, short liberties and captures — never whether a move was good. Opening skirmishes stay quiet.
        BattleHeat?.Invoke(this, reading.Heat);
        AppSettings settings = _settings?.Current ?? new AppSettings();
        int strength = MoveImportance.IsOpening(_game.MoveNumber, move.After.Width, move.After.Height)
            ? Math.Max(0, reading.Strength - 1)
            : reading.Strength;
        if (settings.MoveEffects && strength > 0)
        {
            Impact = new BoardImpact(move.Point, strength, ++_impacts);
            _sounds?.Play(
                strength switch { 3 => SoundEffect.ExplosionBig, 2 => SoundEffect.ExplosionMedium, _ => SoundEffect.ImpactSmall },
                settings.SoundVolume / 100.0);
        }
    }

    private void OnMovePlayed(object? sender, GameNode node)
    {
        if (node == _celebratedNode)
        {
            return; // already judged as it was settled
        }

        // Usually instant: the position before the move was being analysed while the user thought, so the move is
        // already among KataGo's candidates there.
        _awaitingJudgement = IsEngineActive ? node : null;
        TryCelebrate(Position.Of(_game));
    }

    /// <summary>Celebrates the awaited move once its verdict rests on enough visits (partial results are fine).</summary>
    private void TryCelebrate(Position pos)
    {
        int n = pos.Moves.Count;
        if (Assessment is not { } judged || _awaitingJudgement is not { } node || node != _game.CurrentNode || n == 0
            || !_cache.TryGetValue(pos.Key(n - 1), out TurnAnalysis? before))
        {
            return;
        }

        int enough = Math.Min(JudgeVisits, _engine?.Visits ?? JudgeVisits);
        bool beforeSolid = before.Visits >= enough || _complete.Contains(pos.Key(n - 1));
        bool afterSolid = judged.Rank is not null
            || (_cache.TryGetValue(pos.Key(n), out TurnAnalysis? after) && (after.Visits >= enough || _complete.Contains(pos.Key(n))));
        if (beforeSolid && afterSolid)
        {
            _awaitingJudgement = null;
            _celebratedNode = node;
            MoveJudged?.Invoke(this, judged);
            Celebrate(judged, before, node, n);
        }
    }

    /// <summary>
    /// KataGo's verdicts on these moves, for the study report: cached results first, then a quick search for the
    /// rest. Empty when the engine is not set up, or during the player's live OGS games (no AI help there).
    /// </summary>
    public async Task<IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict>> VerdictsAsync(IReadOnlyList<GameNode> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var verdicts = new Dictionary<GameNode, Services.Study.StudyVerdict>();
        if (!IsEngineActive || _engine is null)
        {
            return verdicts;
        }

        GameCursor cursor = _game.Cursor;
        foreach (GameNode node in nodes)
        {
            if (node.GetMove(cursor.BoardSize) is not { } move)
            {
                continue;
            }

            Position pos = Position.At(cursor, node);
            int n = pos.Moves.Count;
            if (n == 0)
            {
                continue;
            }

            var found = new Dictionary<int, TurnAnalysis>();
            foreach (int t in new[] { n - 1, n })
            {
                if (_cache.TryGetValue(pos.Key(t), out TurnAnalysis? cached))
                {
                    found[t] = cached;
                }
            }

            int[] missing = [.. new[] { n - 1, n }.Where(t => !found.ContainsKey(t))];
            if (missing.Length > 0)
            {
                try
                {
                    AnalysisQuery query = pos.Query(missing, Math.Max(30, _engine.Visits / 2)) with { Priority = GraphPriority };
                    foreach (TurnAnalysis t in await _engine.AnalyzeAsync(query, cancellationToken))
                    {
                        found[t.Turn] = t;
                        string key = pos.Key(t.Turn);
                        _ui.Post(() => _cache.TryAdd(key, t));
                    }
                }
                catch (EngineException)
                {
                    break; // the report goes out without the remaining verdicts
                }
            }

            if (found.TryGetValue(n - 1, out TurnAnalysis? before)
                && MoveReview.Assess(before, found.GetValueOrDefault(n), new EngineMove(move.Color, move.Point)) is { } judged)
            {
                verdicts[node] = new Services.Study.StudyVerdict(judged.Quality, judged.PointsLost, judged.Best.Point);
            }
        }

        return verdicts;
    }

    /// <summary>Effect strength for a move quality: only good moves are celebrated.</summary>
    public static int ImpactStrength(MoveQuality quality) => quality switch
    {
        MoveQuality.Best => 3,
        MoveQuality.Excellent => 2,
        MoveQuality.Good => 1,
        _ => 0,
    };

    /// <summary>
    /// Sound and board impact for a move the user just played, once the engine has judged it — as big as the
    /// moment deserves (<see cref="MoveImportance"/>): a routine opening move only gets its stone sound.
    /// </summary>
    private void Celebrate(MoveAssessment judged, TurnAnalysis before, GameNode node, int moveNumber)
    {
        AppSettings settings = _settings?.Current ?? new AppSettings();
        double fight = _fightAt is { } f && f.Node == node ? f.Intensity : 0;
        bool opening = MoveImportance.IsOpening(moveNumber, _game.Board.Width, _game.Board.Height);
        int strength = MoveImportance.Strength(judged.Quality, MoveImportance.Criticality(before, judged.Played.Color), fight, opening);
        LastImportance = strength;
        MoveWeighed?.Invoke(this, new MoveWeight(judged.Quality, strength, opening));
        if (!settings.MoveEffects || strength == 0 || judged.Played.Point is not { } point || !IsEngineActive)
        {
            return;
        }

        Impact = new BoardImpact(point, strength, ++_impacts);
        _sounds?.Play(
            strength switch { 3 => SoundEffect.ExplosionBig, 2 => SoundEffect.ExplosionMedium, _ => SoundEffect.ImpactSmall },
            settings.SoundVolume / 100.0);
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
        Territory = Analysis?.Ownership is { } own && own.Count == board.Width * board.Height && IsEngineActive
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
        (m.Color == Stone.Black ? Tr.T("Game.BlackShort") : Tr.T("Game.WhiteShort")) + " " + (m.Point is { } p ? p.ToHuman(_game.Board.Height) : Tr.T("Game.PassNoun"));

    private static string QualityName(MoveQuality q) => q switch
    {
        MoveQuality.Best => Tr.T("Analysis.BestMove"),
        MoveQuality.Excellent => Tr.T("Analysis.Excellent"),
        MoveQuality.Good => Tr.T("Analysis.Good"),
        MoveQuality.Inaccuracy => Tr.T("Analysis.Inaccuracy"),
        MoveQuality.Mistake => Tr.T("Analysis.Mistake"),
        _ => Tr.T("Analysis.Blunder"),
    };

    private static string FormatLead(double blackLead) => Math.Abs(blackLead) < 0.05
        ? Tr.T("Analysis.Even")
        : string.Create(CultureInfo.InvariantCulture, $"{(blackLead > 0 ? Tr.T("Game.BlackShort") : Tr.T("Game.WhiteShort"))}+{Math.Abs(blackLead):0.0}");

    /// <summary>The shown position as the engine sees it: setup at the root plus the moves that led here.</summary>
    private sealed record Position(AnalysisQuery Base, IReadOnlyList<EngineMove> Moves, string Prefix)
    {
        public static Position Of(GameViewModel game, bool followMainLine = false) => At(game.Cursor, game.Cursor.Current, followMainLine);

        public static Position At(GameCursor cursor, GameNode at, bool followMainLine = false)
        {
            IReadOnlyList<GameNode> path = GameCursor.Path(at);
            if (followMainLine)
            {
                var extended = path.ToList();
                GameNode n = at;
                while (n.Children.Count > 0)
                {
                    n = n.Children[0];
                    extended.Add(n);
                }

                path = extended;
            }

            GameTree tree = cursor.Tree;
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
