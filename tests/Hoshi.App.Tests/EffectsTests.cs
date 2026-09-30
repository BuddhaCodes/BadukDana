using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

internal sealed class FakeSoundService : ISoundService
{
    public List<(SoundEffect Effect, double Volume)> Played { get; } = [];

    public void Play(SoundEffect effect, double volume) => Played.Add((effect, volume));
}

internal sealed class TestSettings : ISettingsService
{
    public AppSettings Current { get; private set; } = new();

    public void Save(AppSettings settings) => Current = settings;
}

/// <summary>Streams one partial result per turn at once, then keeps "searching" until cancelled (like pondering).</summary>
internal sealed class PonderingEngine : IAnalysisEngine
{
    public string? Problem => null;

    public int Visits => 500;

    public Point BestMove { get; set; } = new(15, 3);

    public int PartialVisits { get; set; } = 60;

    public List<AnalysisQuery> Queries { get; } = [];

    public event EventHandler? Changed;

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<IReadOnlyList<TurnAnalysis>>(_ => [], TaskScheduler.Default);

    public async Task<IReadOnlyList<TurnAnalysis>> AnalyzeLiveAsync(AnalysisQuery query, Action<TurnAnalysis> onUpdate, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        foreach (int t in query.Turns.Count == 0 ? [query.Moves.Count] : query.Turns)
        {
            Stone toMove = t % 2 == 0 ? Stone.Black : Stone.White;
            onUpdate(new TurnAnalysis(t, toMove, 0.5, 0.5, PartialVisits, [new MoveCandidate(BestMove, 0, PartialVisits, 0.5, 0.5, 0.4, [])], null));
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return [];
    }
}

public sealed class LiveAnalysisTests
{
    [AvaloniaFact]
    public async Task The_shown_position_is_analysed_live_and_a_prepared_move_is_judged_instantly()
    {
        var game = new GameViewModel();
        var engine = new PonderingEngine();
        var sounds = new FakeSoundService();
        var vm = new AnalysisViewModel(game, engine, new ImmediateDispatcher(), sounds, new TestSettings()) { IsAnalysisOn = true };
        await Task.Delay(300);

        engine.Queries.Should().ContainSingle().Which.Should().Match<AnalysisQuery>(q => q.Priority == 10 && q.ReportDuringSearchEvery == AnalysisViewModel.LiveReportSeconds && q.MaxVisits == 500);
        vm.Suggestions.Should().ContainSingle(s => s.Point == new Point(15, 3), "partial results are shown while KataGo keeps searching");
        vm.StatusText.Should().Be("Analizando en vivo · 60 visitas");

        // The user thinks, then plays KataGo's choice: the verdict is ready before any new analysis.
        game.PlayCommand.Execute(new Point(15, 3));
        vm.Assessment!.Quality.Should().Be(MoveQuality.Best);
        sounds.Played.Should().Equal((SoundEffect.ExplosionBig, 0.7));
        vm.Impact.Should().NotBeNull();
    }

    [AvaloniaFact]
    public async Task A_shallow_partial_result_is_shown_but_not_celebrated()
    {
        var game = new GameViewModel();
        var engine = new PonderingEngine { PartialVisits = 5 };
        var sounds = new FakeSoundService();
        var vm = new AnalysisViewModel(game, engine, new ImmediateDispatcher(), sounds, new TestSettings()) { IsAnalysisOn = true };
        await Task.Delay(300);

        game.PlayCommand.Execute(new Point(15, 3));
        await Task.Delay(300);

        vm.Assessment!.Quality.Should().Be(MoveQuality.Best);
        sounds.Played.Should().BeEmpty("5 visits are too few to trust");
    }
}

public sealed class MoveEffectsTests
{
    private readonly GameViewModel _game = new();
    private readonly FakeAnalysisEngine _engine = new();
    private readonly FakeSoundService _sounds = new();
    private readonly TestSettings _settings = new();
    private readonly AnalysisViewModel _vm;

    public MoveEffectsTests()
    {
        _vm = new AnalysisViewModel(_game, _engine, new ImmediateDispatcher(), _sounds, _settings) { IsAnalysisOn = true };
    }

    private static async Task SettleAsync()
    {
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(20);
        }
    }

    [Theory]
    [InlineData(MoveQuality.Best, 3)]
    [InlineData(MoveQuality.Excellent, 2)]
    [InlineData(MoveQuality.Good, 1)]
    [InlineData(MoveQuality.Inaccuracy, 0)]
    [InlineData(MoveQuality.Mistake, 0)]
    [InlineData(MoveQuality.Blunder, 0)]
    public void Only_good_moves_are_celebrated(MoveQuality quality, int strength) =>
        AnalysisViewModel.ImpactStrength(quality).Should().Be(strength);

    [AvaloniaFact]
    public async Task The_engine_s_best_move_explodes_once_it_is_judged()
    {
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();

        _vm.Impact.Should().Be(new BoardImpact(new Point(15, 3), 3, 1));
        _sounds.Played.Should().Equal((SoundEffect.ExplosionBig, 0.7));
    }

    [AvaloniaFact]
    public async Task Weak_moves_navigation_and_disabled_effects_stay_quiet()
    {
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();
        _game.PlayCommand.Execute(new Point(9, 9)); // white, away from the best point: an inaccuracy
        await SettleAsync();
        _sounds.Played.Should().HaveCount(1);

        _game.GoBackCommand.Execute(null);
        await SettleAsync();
        _game.GoForwardCommand.Execute(null);
        await SettleAsync();
        _sounds.Played.Should().HaveCount(1, "reviewing moves does not replay the effects");

        // Replaying an analysed move celebrates it again, right away…
        _game.GoFirstCommand.Execute(null);
        _game.PlayCommand.Execute(new Point(15, 3));
        _sounds.Played.Should().HaveCount(2);
        _vm.Impact!.Id.Should().Be(2);

        // …unless the effects are turned off in Preferences.
        _settings.Save(_settings.Current with { MoveEffects = false });
        _game.GoFirstCommand.Execute(null);
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();
        _sounds.Played.Should().HaveCount(2);
        _vm.Impact!.Id.Should().Be(2);
    }

    [AvaloniaFact]
    public async Task Without_analysis_there_are_no_effects()
    {
        _vm.IsAnalysisOn = false;
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();

        _vm.Impact.Should().BeNull();
        _sounds.Played.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task The_impact_is_drawn_with_cracks_embers_and_shake_and_saved_as_screenshot()
    {
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board };
        var window = new Window { Width = 700, Height = 700, Content = board };
        window.Show();
        BoardState state = BoardState.Create(19).Setup([(new Point(3, 3), Stone.Black), (new Point(15, 15), Stone.White), (new Point(15, 3), Stone.Black), (new Point(3, 15), Stone.White), (new Point(9, 9), Stone.Black)]);
        board.Board = state;
        board.LastMove = new Point(9, 9);

        board.Impact = new BoardImpact(new Point(9, 9), 3, 7);
        board.IsImpactRunning.Should().BeTrue();
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        foreach ((double t, string name) in new[] { (0.06, "impact-best-1.png"), (0.25, "impact-best-2.png"), (1.3, "impact-best-3.png") })
        {
            board.ImpactTime = t;
            board.InvalidateVisual();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
        }

        board.Impact = new BoardImpact(new Point(9, 9), 2, 9);
        board.ImpactTime = 0.2;
        board.InvalidateVisual();
        using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered"))
        {
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "impact-excellent.png"));
        }

        board.ImpactTime = null;

        board.Animate = false;
        board.Impact = new BoardImpact(new Point(9, 9), 3, 8);
        await Task.Delay(3000);
        window.CaptureRenderedFrame()?.Dispose();
        board.IsImpactRunning.Should().BeFalse("the effect ends, and does not start without animations");
    }

    [AvaloniaFact]
    public void Sounds_come_from_the_assembly_unless_the_user_supplied_their_own()
    {
        string root = Directory.CreateTempSubdirectory("hoshi-sounds").FullName;
        var service = new SystemSoundService(dataDirectory: root);

        string builtIn = service.Resolve(SoundEffect.ExplosionBig);
        builtIn.Should().EndWith(Path.Combine("cache", "sounds", "explosion_big.wav"));
        new FileInfo(builtIn).Length.Should().BeGreaterThan(100_000);
        File.ReadAllBytes(builtIn).Take(4).Should().Equal("RIFF"u8.ToArray());

        Directory.CreateDirectory(service.CustomDirectory);
        string mine = Path.Combine(service.CustomDirectory, "explosion_big.mp3");
        File.WriteAllBytes(mine, [1, 2, 3]);
        service.Resolve(SoundEffect.ExplosionBig).Should().Be(mine);
        service.Resolve(SoundEffect.ImpactSmall).Should().EndWith("impact_small.wav");
        Directory.Delete(root, recursive: true);
    }
}
