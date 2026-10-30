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

/// <summary>One of KataGo's candidates for the shown position, for the review inspector.</summary>
/// <param name="Fraction">Its winrate for the player to move relative to the best candidate's (0–1), for the bar.</param>
public sealed record CandidateRow(string Move, string Winrate, string Lead, double Fraction, bool IsBest);

/// <summary>A big mistake on the shown line: where it was, what it cost and what KataGo preferred.</summary>
public sealed record KeyMistake(GameNode Node, int Number, bool IsBlack, string Move, string Better, string LossText, bool IsBlunder)
{
    public string Title => Number.ToString(CultureInfo.InvariantCulture) + " · " + Move;
}

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

    /// <summary>
    /// The same on phones: KataGo runs on the CPU there, a few times slower, and an explosion seconds after the stone
    /// feels broken. A dozen visits already separate good moves from bad ones well enough for the effects.
    /// </summary>
    public const int PhoneJudgeVisits = 12;

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
    private GameNode? _fellBackNode;
    private int _fallbacks;

    /// <summary>Visits a verdict needs before the move is celebrated (fewer on phones).</summary>
    public int JudgeThreshold { get; set; } = PlatformInfo.IsPhone ? PhoneJudgeVisits : JudgeVisits;

    /// <summary>
    /// How long a move waits for KataGo's verdict before its effect follows the fight on the board instead (as without
    /// an engine); the verdict still drives the music when it arrives. Phones only by default; null waits.
    /// </summary>
    public TimeSpan? JudgeFallback { get; set; } = PlatformInfo.IsPhone ? TimeSpan.FromMilliseconds(800) : null;

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

    /// <summary>KataGo's three candidates for the shown position (empty without analysis).</summary>
    [ObservableProperty]
    private IReadOnlyList<CandidateRow> _candidates = [];

    /// <summary>The three costliest mistakes (and blunders) on the shown line, in move order.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMistakes))]
    [NotifyCanExecuteChangedFor(nameof(NextMistakeCommand))]
    private IReadOnlyList<KeyMistake> _keyMistakes = [];

    /// <summary>Move numbers of every mistake and blunder on the shown line (red dots on the graph).</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _mistakeMarks = [];

    public bool HasMistakes => KeyMistakes.Count > 0;

    /// <summary>KataGo's first choice in the shown position ("R16"), or empty.</summary>
    public string BestMoveText => Candidates.Count > 0 ? Candidates[0].Move : string.Empty;

    /// <summary>The engine's reading of the shown position may be shown (it has one, and you are not playing live on OGS).</summary>
    public bool HasQuickRead => IsEngineActive && Analysis is not null && Candidates.Count > 0;

    partial void OnCandidatesChanged(IReadOnlyList<CandidateRow> value)
    {
        OnPropertyChanged(nameof(BestMoveText));
        OnPropertyChanged(nameof(HasQuickRead));
    }

    private IReadOnlyList<(int Number, GameNode Node)> _mistakeNodes = [];

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

    [RelayCommand]
    private void GoToMistake(KeyMistake? mistake)
    {
        if (mistake is not null)
        {
            _game.GoToNodeCommand.Execute(mistake.Node);
        }
    }

    /// <summary>Jumps to the next mistake or blunder after the shown move (back to the first after the last).</summary>
    [RelayCommand(CanExecute = nameof(HasMistakes))]
    private void NextMistake()
    {
        if (_mistakeNodes.Count == 0)
        {
            return;
        }

        int now = _game.MoveNumber;
        GameNode node = _mistakeNodes[0].Node;
        foreach ((int number, GameNode n) in _mistakeNodes)
        {
            if (number > now)
            {
                node = n;
                break;
            }
        }

        _game.GoToNodeCommand.Execute(node);
    }

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
        OnPropertyChanged(nameof(HasQuickRead));
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
        OnPropertyChanged(nameof(HasQuickRead));
        OnPropertyChanged(nameof(IsWarmingUp));
        OnPropertyChanged(nameof(BoardSuggestions));
        OnPropertyChanged(nameof(IsTerritoryActive));
        OnPropertyChanged(nameof(StatusText));
        UpdateTerritory();
        if (!IsEngineActive)
        {
            Cancel();
            Suggestions = [];
            Candidates = [];
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

    private void UpdateGraph(Position pos)
    {
        Position track = Position.Of(_game, followMainLine: true);
        Graph = [.. Enumerable.Range(0, track.Moves.Count + 1).Select(t => _cache.TryGetValue(track.Key(t), out TurnAnalysis? a) ? (double?)a.ScoreLead : null)];
        GraphIndex = pos.Moves.Count;
        UpdateVerdicts(track);
    }

    /// <summary>KataGo's verdict on each move of the shown line that it has read (for the colours in the tree).</summary>
    [ObservableProperty]
    private IReadOnlyDictionary<GameNode, MoveQuality> _nodeVerdicts = new Dictionary<GameNode, MoveQuality>();

    private void UpdateVerdicts(Position track)
    {
        if (!IsEngineActive || track.Moves.Count == 0)
        {
            if (NodeVerdicts.Count > 0)
            {
                NodeVerdicts = new Dictionary<GameNode, MoveQuality>();
            }

            SetMistakes([]);
            LineVerdicts = new Dictionary<GameNode, Services.Study.StudyVerdict>();
            return;
        }

        // The move nodes of the line, in the same order as the track's moves.
        var nodes = new List<GameNode>();
        GameNode? n = _game.CurrentNode;
        var path = GameCursor.Path(n).ToList();
        while (n.Children.Count > 0)
        {
            n = n.Children[0];
            path.Add(n);
        }

        nodes.AddRange(path.Skip(1).Where(x => x.GetMove(_game.Cursor.BoardSize) is not null));
        if (nodes.Count != track.Moves.Count)
        {
            SetMistakes([]);
            LineVerdicts = new Dictionary<GameNode, Services.Study.StudyVerdict>();
            return; // an edited position: no move history to judge
        }

        var verdicts = new Dictionary<GameNode, MoveQuality>();
        var line = new Dictionary<GameNode, Services.Study.StudyVerdict>();
        var mistakes = new List<(int Number, GameNode Node, MoveAssessment Judged)>();
        for (int t = 1; t <= track.Moves.Count; t++)
        {
            if (_cache.TryGetValue(track.Key(t - 1), out TurnAnalysis? before)
                && MoveReview.Assess(before, _cache.GetValueOrDefault(track.Key(t)), track.Moves[t - 1]) is { } judged)
            {
                verdicts[nodes[t - 1]] = judged.Quality;
                line[nodes[t - 1]] = new Services.Study.StudyVerdict(judged.Quality, judged.PointsLost, judged.Best.Point, MoveImportance.Criticality(before, track.Moves[t - 1].Color));
                if (judged.Quality is MoveQuality.Mistake or MoveQuality.Blunder)
                {
                    mistakes.Add((t, nodes[t - 1], judged));
                }
            }
        }

        SetMistakes(mistakes);
        LineVerdicts = line;

        if (verdicts.Count != NodeVerdicts.Count || verdicts.Any(v => !NodeVerdicts.TryGetValue(v.Key, out MoveQuality q) || q != v.Value))
        {
            NodeVerdicts = verdicts;
        }
    }

    /// <summary>The engine's first three candidates, with winrates for the player to move.</summary>
    internal static IReadOnlyList<CandidateRow> Rows(TurnAnalysis at, int boardHeight)
    {
        MoveCandidate[] top = [.. at.Candidates.Where(c => c.Point is not null).Take(3)];
        if (top.Length == 0)
        {
            return [];
        }

        double For(MoveCandidate c) => at.ToMove == Stone.Black ? c.Winrate : 1 - c.Winrate;
        double best = Math.Max(For(top[0]), 0.0001);
        return [.. top.Select((c, i) =>
        {
            double lead = at.ToMove == Stone.Black ? c.ScoreLead : -c.ScoreLead;
            return new CandidateRow(
                c.Point!.Value.ToHuman(boardHeight),
                string.Create(CultureInfo.InvariantCulture, $"{For(c) * 100:0} %"),
                string.Create(CultureInfo.InvariantCulture, $"{(lead >= 0 ? "+" : "−")}{Math.Abs(lead):0.0}"),
                Math.Clamp(For(c) / best, 0.05, 1),
                i == 0);
        })];
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

    internal static string QualityName(MoveQuality q) => q switch
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
