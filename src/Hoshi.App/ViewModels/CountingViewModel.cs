using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>
/// Counting at the end of a local game (two players at the same computer, or against an engine): after two passes
/// the dead stones are proposed (from KataGo's ownership map when it has read the position), a tap on a group marks
/// it dead or alive again, and the score follows the game's rules. "Accept" writes the result (SGF <c>RE</c>);
/// "Keep playing" takes back the last pass. Online games count on OGS instead.
/// </summary>
public sealed partial class CountingViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly AnalysisViewModel? _analysis;
    private IReadOnlySet<Point> _dead = new HashSet<Point>();
    private GameNode? _countedNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText), nameof(DetailText))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultText), nameof(DetailText))]
    private ScoreResult? _result;

    [ObservableProperty]
    private TerritoryEstimate? _territory;

    public CountingViewModel(GameViewModel game, AnalysisViewModel? analysis = null)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _analysis = analysis;
        _game.PropertyChanged += OnGameChanged;
    }

    /// <summary>The stones marked dead.</summary>
    public IReadOnlySet<Point> Dead => _dead;

    /// <summary>"Black wins by 3.5" (or a draw).</summary>
    public string ResultText => Result is not { } r ? string.Empty
        : r.Winner == Stone.Empty ? Tr.T("Count.Draw")
        : Tr.F("Count.Wins", Tr.T(r.Winner == Stone.Black ? "Common.Black" : "Common.White"), Points(r.Margin));

    /// <summary>The breakdown for both players, following the rules (territory and prisoners, or area).</summary>
    public string DetailText
    {
        get
        {
            if (Result is not { } r)
            {
                return string.Empty;
            }

            string white = r.Method == ScoringMethod.Territory
                ? Tr.F("Count.TerritoryLine", Tr.T("Common.White"), r.WhiteTerritory, r.WhitePrisoners, Points(r.Komi + r.HandicapCompensation), Points(r.WhiteScore))
                : Tr.F("Count.AreaLine", Tr.T("Common.White"), r.WhiteStones, r.WhiteTerritory, Points(r.Komi + r.HandicapCompensation), Points(r.WhiteScore));
            string black = r.Method == ScoringMethod.Territory
                ? Tr.F("Count.TerritoryLine", Tr.T("Common.Black"), r.BlackTerritory, r.BlackPrisoners, "0", Points(r.BlackScore))
                : Tr.F("Count.AreaLine", Tr.T("Common.Black"), r.BlackStones, r.BlackTerritory, "0", Points(r.BlackScore));
            return black + "\n" + white;
        }
    }

    /// <summary>Starts counting the shown position (the game must be over: two passes in a row).</summary>
    [RelayCommand]
    public void Start()
    {
        if (_game.Online is not null)
        {
            return;
        }

        if (!_game.IsGameOver)
        {
            _game.ShowStatus(Tr.T("Count.NeedPasses"));
            return;
        }

        _countedNode = _game.CurrentNode;
        _dead = new HashSet<Point>(Suggestion());
        IsActive = true;
        _game.SetBoardOverride(Toggle, Overlay());
        Recount();
        _game.ShowStatus(Tr.T("Count.Hint"));
    }

    /// <summary>Writes the result into the game and leaves counting.</summary>
    [RelayCommand]
    private void Accept()
    {
        if (Result is { } r)
        {
            _game.SetResult(r.ToSgfResult());
            string text = ResultText;
            Stop();
            _game.ShowStatus(Tr.F("Count.Saved", text));
        }
    }

    /// <summary>Back to the game: the last pass is taken back.</summary>
    [RelayCommand]
    private void KeepPlaying()
    {
        Stop();
        if (_game.UndoCommand.CanExecute(null))
        {
            _game.UndoCommand.Execute(null);
        }
    }

    private bool Toggle(Point point)
    {
        if (!IsActive)
        {
            return false;
        }

        _dead = Scoring.ToggleDead(_game.Board, _dead, point);
        _game.SetBoardOverride(Toggle, Overlay());
        Recount();
        return true;
    }

    private void Stop()
    {
        IsActive = false;
        _countedNode = null;
        Result = null;
        Territory = null;
        _game.SetBoardOverride(null);
    }

    private IReadOnlyList<Point> Suggestion()
    {
        // KataGo's map of the final position, when it has read it; otherwise nothing is marked (tap to mark).
        if (_analysis is { IsEngineActive: true, Analysis.Ownership: { } ownership })
        {
            return Scoring.SuggestDead(_game.Board, ownership);
        }

        return [];
    }

    private IReadOnlyList<Markup> Overlay() => [.. _dead.Select(p => new Markup(p, MarkupKind.Cross))];

    private void Recount()
    {
        BoardState board = _game.Board;
        int handicap = int.TryParse(_game.Tree.Root.GetValue("HA"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
        ScoreResult result = Scoring.Score(board, _dead, _game.Tree.Info.Komi ?? 0, handicap);
        Result = result;

        // The board shows the count with the territory marks: every point to its owner, dead stones to the other side.
        double[] ownership = new double[board.Width * board.Height];
        foreach (Point p in board.AllPoints)
        {
            ownership[(p.Y * board.Width) + p.X] = result.OwnerAt(p) switch
            {
                Stone.Black => 1,
                Stone.White => -1,
                _ => 0,
            };
        }

        Territory = TerritoryEstimate.FromOwnership(board, ownership, result.Komi);
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameViewModel.TreeVersion))
        {
            return;
        }

        if (IsActive && _game.CurrentNode != _countedNode)
        {
            Stop(); // moved away from the final position
        }
        else if (!IsActive && _game.IsGameOver && _game.Online is null && !_game.IsEditMode
            && string.IsNullOrEmpty(_game.Tree.Info.Result) && _game.CurrentNode.Children.Count == 0)
        {
            Start(); // the second pass was just played
        }
    }

    private static string Points(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
