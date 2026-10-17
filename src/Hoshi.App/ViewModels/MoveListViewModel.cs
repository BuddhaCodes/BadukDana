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
        for (GameNode n = current; n.Children.Count > 0;)
        {
            n = n.Children[0];
            line.Add(n);
        }

        IReadOnlyDictionary<GameNode, MoveQuality> verdicts = _analysis.NodeVerdicts;
        IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict> full = _analysis.LineVerdicts;
        var cells = new List<MoveCell>();
        int number = 0;
        foreach (GameNode node in line)
        {
            if (node.GetMove(size) is not { } move)
            {
                continue;
            }

            number++;
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
    }
}
