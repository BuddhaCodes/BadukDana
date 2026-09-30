using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Ogs;
using Hoshi.Ogs.Games;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Answers every query at once: black leads by 1 point per stone black has played more than white…</summary>
internal sealed class FakeAnalysisEngine : IAnalysisEngine
{
    public string? Problem { get; set; }

    public int Visits => 100;

    public List<AnalysisQuery> Queries { get; } = [];

    /// <summary>Best move suggested at every turn.</summary>
    public Point BestMove { get; set; } = new(15, 3);

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken)
    {
        lock (Queries)
        {
            Queries.Add(query);
        }

        IReadOnlyList<int> turns = query.Turns.Count == 0 ? [query.Moves.Count] : query.Turns;
        var results = turns.Select(t =>
        {
            Stone toMove = t % 2 == 0 ? Stone.Black : Stone.White;
            // Lead after t moves: every move at the best point is worth +0; any other black move costs 2, white move gives 3.
            double lead = 0;
            foreach (EngineMove m in query.Moves.Take(t))
            {
                if (m.Point != BestMove)
                {
                    lead += m.Color == Stone.Black ? -2 : 3;
                }
            }

            MoveCandidate best = new(BestMove, 0, 80, 0.55, lead, 0.5, [BestMove]);
            MoveCandidate second = new(new Point(3, 3), 1, 20, 0.5, lead + (toMove == Stone.Black ? -0.4 : 0.4), 0.2, []);
            double[] ownership = [.. Enumerable.Range(0, query.Width * query.Height).Select(i => i < query.Width ? 1.0 : 0)];
            return new TurnAnalysis(t, toMove, 0.55, lead, 100, [best, second], ownership);
        }).ToList();
        return Task.FromResult<IReadOnlyList<TurnAnalysis>>(results);
    }
}

public sealed class AnalysisViewModelTests
{
    private readonly GameViewModel _game = new();
    private readonly FakeAnalysisEngine _engine = new();
    private readonly AnalysisViewModel _vm;

    public AnalysisViewModelTests()
    {
        _vm = new AnalysisViewModel(_game, _engine, new ImmediateDispatcher());
    }

    private async Task SettleAsync()
    {
        // Debounce (120 ms) plus the graph batches.
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(20);
        }
    }

    [AvaloniaFact]
    public void Territory_works_without_an_engine()
    {
        var vm = new AnalysisViewModel(_game, engine: null, new ImmediateDispatcher());
        _game.PlayCommand.Execute(new Point(3, 3));

        vm.ToggleTerritoryCommand.Execute(null);

        vm.Territory.Should().NotBeNull();
        vm.Territory!.Source.Should().Be(EstimateSource.Heuristic);
        vm.TerritoryText.Should().Contain("estimación rápida");
        vm.ToggleTerritoryCommand.Execute(null);
        vm.Territory.Should().BeNull();
    }

    [AvaloniaFact]
    public async Task Analysis_judges_the_last_move_against_the_engine_s_choice()
    {
        _vm.IsAnalysisOn = true;
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();

        _vm.Assessment.Should().NotBeNull();
        _vm.Assessment!.Quality.Should().Be(MoveQuality.Best);
        _vm.AssessmentText.Should().Be("N Q16 · la mejor jugada");
        _vm.AssessmentClass.Should().Be("good");

        _game.PlayCommand.Execute(new Point(9, 9));
        await SettleAsync();

        _vm.Assessment!.Quality.Should().Be(MoveQuality.Inaccuracy, "a white move away from the best point gives black 3 points");
        _vm.Assessment.PointsLost.Should().BeApproximately(3, 0.001);
        _vm.AssessmentText.Should().Be("B K10 · imprecisa (−3.0)");
        _vm.AssessmentClass.Should().Be("warn");
    }

    [AvaloniaFact]
    public async Task Each_position_asks_only_for_the_turns_it_misses()
    {
        _vm.IsAnalysisOn = true;
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();
        int before = _engine.Queries.Count;

        _game.GoBackCommand.Execute(null);
        await SettleAsync();
        _game.GoForwardCommand.Execute(null);
        await SettleAsync();

        _engine.Queries.Count.Should().Be(before, "both positions are cached");
        _engine.Queries[0].Turns.Should().Equal(0, 1);
        _engine.Queries[0].Moves.Should().ContainSingle().Which.Should().Be(new EngineMove(Stone.Black, new Point(15, 3)));
    }

    [AvaloniaFact]
    public async Task Suggestions_winrate_lead_and_graph_are_shown()
    {
        _vm.IsAnalysisOn = true;
        _game.PlayCommand.Execute(new Point(3, 15));
        _game.PlayCommand.Execute(new Point(9, 9));
        await SettleAsync();

        _vm.Suggestions.Should().HaveCount(2);
        _vm.Suggestions[0].Should().Match<BoardSuggestion>(s => s.IsBest && s.Point == new Point(15, 3));
        _vm.WinrateText.Should().Be("Negras 55 %");
        _vm.LeadText.Should().Be("N+1.0", "−2 for black's move, +3 for white's");
        _vm.Graph.Should().HaveCount(3).And.OnlyContain(v => v != null);
        _vm.GraphIndex.Should().Be(2);
        _vm.BestText.Should().Contain("Mejor: B Q16");
    }

    [AvaloniaFact]
    public async Task With_analysis_on_the_territory_uses_the_engine_ownership()
    {
        _vm.IsAnalysisOn = true;
        _vm.IsTerritoryOn = true;
        _game.PlayCommand.Execute(new Point(3, 3));
        await SettleAsync();

        _vm.Territory!.Source.Should().Be(EstimateSource.Engine);
        _vm.Territory.SecureOwner(new Point(5, 0)).Should().Be(Stone.Black);
        _vm.TerritoryText.Should().Contain("(KataGo)");
    }

    [AvaloniaFact]
    public async Task Clicking_the_graph_navigates_to_that_move()
    {
        _vm.IsAnalysisOn = true;
        _game.PlayCommand.Execute(new Point(3, 15));
        _game.PlayCommand.Execute(new Point(9, 9));
        _game.PlayCommand.Execute(new Point(15, 15));
        await SettleAsync();

        _vm.GoToMoveCommand.Execute(1);

        _game.MoveNumber.Should().Be(1);
        _vm.GraphIndex.Should().Be(1);
        _vm.Graph.Should().HaveCount(4, "the graph still shows the whole line");
    }

    [AvaloniaFact]
    public void Without_KataGo_the_panel_explains_how_to_configure_it()
    {
        _engine.Problem = "KataGo no está configurado (☰ → Preferencias → Análisis).";
        _vm.IsAnalysisOn = true;

        _vm.StatusText.Should().Contain("Preferencias");
        _engine.Queries.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Analysis_and_territory_are_disabled_while_playing_on_OGS()
    {
        _vm.IsAnalysisOn = true;
        _vm.IsTerritoryOn = true;
        var online = new OnlineGameViewModel(new FakeOnlineGame(1, 100), _game, new ImmediateDispatcher());
        online.Connect();
        ((FakeOnlineGame)typeof(OnlineGameViewModel).GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(online)!)
            .Gamedata(new OgsGameSnapshot { GameId = 1, Width = 9, Height = 9, Black = new OgsUser(100, "me", 20, false), White = new OgsUser(200, "you", 20, false) });
        await SettleAsync();
        _engine.Queries.Clear();

        _game.IsOnline.Should().BeTrue();
        _vm.IsBlocked.Should().BeTrue();
        _vm.Territory.Should().BeNull();
        _vm.Suggestions.Should().BeEmpty();
        _vm.StatusText.Should().Contain("OGS");
        online.PassCommand.Execute(null);
        await SettleAsync();
        _engine.Queries.Should().BeEmpty();
    }
}

public sealed class AnalysisWindowTests
{
    [AvaloniaFact]
    public async Task Territory_and_suggestions_render_on_the_board_and_are_saved_as_screenshot()
    {
        var engine = new FakeAnalysisEngine { BestMove = new Point(16, 3) };
        var vm = new MainWindowViewModel(new GameViewModel(), engine: engine, ui: new ImmediateDispatcher());
        foreach (var (x, y) in new[] { (3, 3), (15, 15), (15, 3), (3, 15), (2, 5), (16, 13), (5, 2), (13, 16), (9, 3), (9, 15) })
        {
            vm.Game.PlayCommand.Execute(new Point(x, y));
        }

        var window = new MainWindow(vm) { Width = 1400, Height = 860 };
        window.Show();
        vm.Analysis.IsTerritoryOn = true;
        vm.Analysis.IsAnalysisOn = true;
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(20);
        }

        window.FindControl<Border>("AnalysisPanel")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<TextBlock>("AssessmentText")!.Text.Should().NotBeNullOrEmpty();
        window.FindControl<Button>("TerritoryButton")!.Classes.Should().Contain("on");
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "analysis.png"));

        // Territory alone (heuristic) is also saved for review.
        vm.Analysis.IsAnalysisOn = false;
        using WriteableBitmap heuristic = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        heuristic.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "territory.png"));
    }
}
