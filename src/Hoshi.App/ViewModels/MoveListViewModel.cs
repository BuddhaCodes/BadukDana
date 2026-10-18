using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>One move in the review's move list: where, how KataGo judged it (a CSS-like class) and what it cost.</summary>
/// <param name="Quality">"blunder", "mistake", "inaccuracy", "best" or empty (not judged / ordinary).</param>
public sealed record MoveCell(GameNode Node, int Number, bool IsBlack, string Text, string Quality, string? Loss, bool IsCurrent)
{
    public bool IsBlunder => Quality == "blunder";

    public bool IsMistake => Quality == "mistake";

    public bool IsInaccuracy => Quality == "inaccuracy";

    public bool IsBest => Quality == "best";
}

/// <summary>Two moves side by side, as in a game record.</summary>
public sealed record MovePairRow(int Number, MoveCell First, MoveCell? Second)
{
    public bool IsCurrent => First.IsCurrent || (Second?.IsCurrent ?? false);
}

/// <summary>
/// The review's move list (redesign 2026-10, left of the board in Review mode): the moves of the line through the
/// shown move, two per row, coloured by KataGo's verdicts with the points lost; clicking one goes there.
/// </summary>
public sealed partial class MoveListViewModel : ViewModelBase
{
    private readonly GameViewModel _game;
    private readonly AnalysisViewModel _analysis;

    [ObservableProperty]
    private IReadOnlyList<MovePairRow> _rows = [];

    [ObservableProperty]
    private MovePairRow? _currentRow;

    // The line through the shown move: its first node (the position before any move) and its move nodes.
    private GameNode? _lineStart;
    private IReadOnlyList<GameNode> _lineMoves = [];
    private int _position;

    /// <summary>Moves on the line through the shown move (the footer's slider goes from 0 to this).</summary>
    public int LineLength => _lineMoves.Count;

    /// <summary>Where the shown move is on its line (0 = before the first move); setting it goes there.</summary>
    public int Position
    {
        get => _position;
        set
        {
            int target = Math.Clamp(value, 0, _lineMoves.Count);
            if (target == _position)
            {
                return;
            }

            GameNode? node = target == 0 ? _lineStart : _lineMoves[target - 1];
            if (node is not null)
            {
                _game.GoToNodeCommand.Execute(node);
            }
        }
    }

    /// <summary>"73 / 187".</summary>
    public string PositionText => string.Create(CultureInfo.InvariantCulture, $"{_position} / {_lineMoves.Count}");

    public MoveListViewModel(GameViewModel game, AnalysisViewModel analysis)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        game.PropertyChanged += OnGameChanged;
        analysis.PropertyChanged += OnAnalysisChanged;
        Rebuild();
    }

    [RelayCommand]
    private void GoTo(MoveCell? cell)
    {
        if (cell is not null)
        {
            _game.GoToNodeCommand.Execute(cell.Node);
        }
    }

    private void OnGameChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GameViewModel.TreeVersion) or nameof(GameViewModel.CurrentNode) or nameof(GameViewModel.MoveNumber))
        {
            Rebuild();
        }
    }

    private void OnAnalysisChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AnalysisViewModel.NodeVerdicts))
        {
            Rebuild();
        }
    }

    /// <summary>Builds the rows again (the line, the verdicts or the shown move changed).</summary>
    public void Rebuild()
    {
        int size = _game.Cursor.BoardSize;
        int height = _game.Board.Height;
        GameNode current = _game.CurrentNode;
        var line = GameCursor.Path(current).ToList();
        int pathLength = line.Count;
        for (GameNode n = current; n.Children.Count > 0;)
        {
            n = n.Children[0];
            line.Add(n);
        }

        IReadOnlyDictionary<GameNode, MoveQuality> verdicts = _analysis.NodeVerdicts;
        IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict> full = _analysis.LineVerdicts;
        var cells = new List<MoveCell>();
        var moves = new List<GameNode>();
        int position = line.Take(pathLength).Count(n => n.GetMove(size) is not null);
        int number = 0;
        foreach (GameNode node in line)
        {
            if (node.GetMove(size) is not { } move)
            {
                continue;
            }

            number++;
            moves.Add(node);

            string quality = verdicts.TryGetValue(node, out MoveQuality q) ? q switch
            {
                MoveQuality.Blunder => "blunder",
                MoveQuality.Mistake => "mistake",
                MoveQuality.Inaccuracy => "inaccuracy",
                MoveQuality.Best => "best",
                _ => string.Empty,
            } : string.Empty;
            string? loss = quality is "blunder" or "mistake" or "inaccuracy" && full.TryGetValue(node, out Services.Study.StudyVerdict? v) && double.IsFinite(v.PointsLost)
                ? string.Create(CultureInfo.InvariantCulture, $"−{v.PointsLost:0}")
                : null;
            cells.Add(new MoveCell(
                node,
                number,
                move.Color == Stone.Black,
                move.Point is { } p ? p.ToHuman(height) : Tr.T("Game.PassNoun"),
                quality,
                loss,
                node == current));
        }

        var rows = new List<MovePairRow>();
        for (int i = 0; i < cells.Count; i += 2)
        {
            rows.Add(new MovePairRow(cells[i].Number, cells[i], i + 1 < cells.Count ? cells[i + 1] : null));
        }

        if (rows.Count != Rows.Count || rows.Where((r, i) => r != Rows[i]).Any())
        {
            Rows = rows;
        }

        CurrentRow = Rows.FirstOrDefault(r => r.IsCurrent);
        bool lengthChanged = moves.Count != _lineMoves.Count;
        _lineStart = line.Count > 0 ? line[0] : null;
        _lineMoves = moves;
        if (lengthChanged)
        {
            OnPropertyChanged(nameof(LineLength));
        }

        if (position != _position || lengthChanged)
        {
            _position = position;
            OnPropertyChanged(nameof(Position));
            OnPropertyChanged(nameof(PositionText));
        }
    }
}
