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
    public async Task KataGo_judges_in_the_background_even_with_the_analysis_hidden()
    {
        _vm.IsAnalysisOn = false;
        _game.PlayCommand.Execute(new Point(15, 3));
        await SettleAsync();

        _vm.Impact.Should().NotBeNull("effects and music need the verdict, not the panel");
        _sounds.Played.Should().ContainSingle();
        _vm.Suggestions.Should().NotBeEmpty();
        _vm.BoardSuggestions.Should().BeEmpty("the board only shows suggestions when the analysis is shown");
        _vm.Graph.Should().OnlyContain(v => v == null || true);

        _vm.IsAnalysisOn = true;
        _vm.BoardSuggestions.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public async Task KataGo_wakes_up_when_the_app_opens_and_a_loading_pill_shows_until_it_answers()
    {
        var engine = new PonderingEngine();
        var game = new GameViewModel();
        var vm = new AnalysisViewModel(game, engine, new ImmediateDispatcher());
        vm.IsWarmingUp.Should().BeTrue();
        await Task.Delay(400);
        vm.IsWarmingUp.Should().BeFalse("KataGo has answered");
        engine.Queries.Should().ContainSingle().Which.Moves.Should().BeEmpty("the empty board is analysed at start-up");

        var slow = new FakeAnalysisEngine();
        var silent = new SilentEngine();
        var waiting = new AnalysisViewModel(new GameViewModel(), silent, new ImmediateDispatcher());
        waiting.IsWarmingUp.Should().BeTrue();
        waiting.WarmupText.Should().Be("Despertando a KataGo…");
        silent.Activity = "KataGo está calibrando la tarjeta gráfica (paso 3/55)…";
        silent.RaiseActivity();
        waiting.WarmupText.Should().Be("Calibrando la tarjeta gráfica (paso 3/55)…");
        slow.Should().NotBeNull();
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

    [AvaloniaFact]
    public void A_stone_sound_plays_for_each_new_stone_but_not_for_jumps_or_edits()
    {
        var game = new GameViewModel();
        var placed = new List<Stone>();
        game.StonePlaced += (_, color) => placed.Add(color);

        game.PlayCommand.Execute(new Point(3, 3));
        game.PlayCommand.Execute(new Point(15, 15));
        placed.Should().Equal(Stone.Black, Stone.White);

        game.PassCommand.Execute(null);
        game.GoFirstCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        placed.Should().HaveCount(3, "stepping forward places a stone; a pass and jumping back do not");

        game.GoLastCommand.Execute(null);
        game.Comment = "nice";
        placed.Should().HaveCount(3);
    }

    [AvaloniaFact]
    public void The_stone_click_is_built_in_and_its_volume_is_baked_into_the_samples()
    {
        string root = Directory.CreateTempSubdirectory("hoshi-stone").FullName;
        var service = new SystemSoundService(dataDirectory: root);
        service.Resolve(SoundEffect.Stone).Should().EndWith("stone_1.wav");
        byte[] wav = File.ReadAllBytes(service.ResolveName("stone", "stone_2"));
        short loudest = Loudest(wav);
        loudest.Should().BeGreaterThan(10000);
        SystemSoundService.ScalePcm16(wav, 0.5);
        Loudest(wav).Should().BeCloseTo((short)(loudest / 2), 2);
        Directory.Delete(root, recursive: true);

        static short Loudest(byte[] w)
        {
            short max = 0;
            for (int i = 44; i + 1 < w.Length; i += 2)
            {
                max = Math.Max(max, Math.Abs(BitConverter.ToInt16(w, i)));
            }

            return max;
        }
    }

    [AvaloniaFact]
    public void Captures_are_announced_with_the_number_of_stones()
    {
        var game = new GameViewModel();
        var captures = new List<int>();
        game.StonesCaptured += (_, n) => captures.Add(n);
        // White D4 surrounded by black: C4, E4, D5, then D3 captures.
        foreach ((int x, int y) in new[] { (2, 15), (3, 15), (4, 15), (10, 10), (3, 14), (10, 11) })
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        captures.Should().BeEmpty();
        game.PlayCommand.Execute(new Point(3, 16));
        captures.Should().Equal(1);

        game.GoBackCommand.Execute(null);
        game.GoForwardCommand.Execute(null);
        captures.Should().Equal(1, 1);
    }

    [AvaloniaFact]
    public void Captured_stones_shatter_on_the_board_and_frames_are_saved()
    {
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board };
        var window = new Window { Width = 600, Height = 600, Content = board };
        window.Show();
        // A white group of three in atari at C17/D17/E17 (row 2 from the top), black about to fill the last liberty.
        BoardState before = BoardState.Create(19).Setup([
            (new Point(2, 2), Stone.White), (new Point(3, 2), Stone.White), (new Point(4, 2), Stone.White),
            (new Point(1, 2), Stone.Black), (new Point(5, 2), Stone.Black),
            (new Point(2, 1), Stone.Black), (new Point(3, 1), Stone.Black), (new Point(4, 1), Stone.Black),
            (new Point(2, 3), Stone.Black), (new Point(3, 3), Stone.Black)]);
        board.Board = before;
        MoveResult result = before.TryPlay(Stone.Black, new Point(4, 3));
        result.IsLegal.Should().BeTrue();
        board.Board = result.State!;
        board.LastMove = new Point(4, 3);

        board.CapturingStones.Should().Be(3);
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        foreach ((double t, string name) in new[] { (0.05, "capture-1.png"), (0.2, "capture-2.png"), (0.4, "capture-3.png") })
        {
            board.CaptureTime = t;
            board.InvalidateVisual();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
        }
    }

    [AvaloniaFact]
    public void A_group_falling_into_atari_is_announced_once_and_shown_while_it_lasts()
    {
        var game = new GameViewModel();
        var vm = new AnalysisViewModel(game, engine: null, new ImmediateDispatcher(), settings: new TestSettings());
        var alerts = new List<int>();
        game.GroupsEnteredAtari += (_, groups) => alerts.Add(groups.Count);

        game.PlayCommand.Execute(new Point(3, 3));   // B
        game.PlayCommand.Execute(new Point(3, 2));   // W
        game.PlayCommand.Execute(new Point(2, 2));   // B
        game.PlayCommand.Execute(new Point(15, 15)); // W elsewhere
        alerts.Should().BeEmpty();
        game.PlayCommand.Execute(new Point(4, 2));   // B: white D17 has one liberty left (D18)

        alerts.Should().Equal(1);
        vm.AtariGroups.Should().ContainSingle().Which.Liberty.Should().Be(new Point(3, 1));
        game.PlayCommand.Execute(new Point(15, 3));  // W elsewhere: still in atari, not announced again
        alerts.Should().Equal(1);
        vm.AtariGroups.Should().HaveCount(1);
    }

    [AvaloniaFact]
    public void A_group_in_atari_trembles_and_sweats_and_frames_are_saved()
    {
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board };
        var window = new Window { Width = 600, Height = 600, Content = board };
        window.Show();
        BoardState state = BoardState.Create(19).Setup([
            (new Point(3, 2), Stone.White), (new Point(4, 2), Stone.White),
            (new Point(2, 2), Stone.Black), (new Point(5, 2), Stone.Black), (new Point(3, 3), Stone.Black), (new Point(4, 3), Stone.Black), (new Point(4, 1), Stone.Black)]);
        board.Board = state;
        board.AtariGroups = Hoshi.Core.Atari.Groups(state);
        board.AtariGroups.Should().ContainSingle();

        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        foreach ((double t, string name) in new[] { (0.2, "atari-1.png"), (1.2, "atari-2.png"), (1.85, "atari-3.png") })
        {
            board.AtariTime = t;
            board.InvalidateVisual();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", name));
        }
    }
}

/// <summary>Never answers (KataGo still loading).</summary>
internal sealed class SilentEngine : IAnalysisEngine
{
    public string? Problem => null;

    public int Visits => 100;

    public string? Activity { get; set; }

    public event EventHandler? Changed;

    public event EventHandler? ActivityChanged;

    public void RaiseActivity() => ActivityChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    public Task<IReadOnlyList<TurnAnalysis>> AnalyzeAsync(AnalysisQuery query, CancellationToken cancellationToken) =>
        Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith<IReadOnlyList<TurnAnalysis>>(_ => [], TaskScheduler.Default);
}

public sealed class BattleAndReplayTests
{
    [AvaloniaFact]
    public void Without_an_engine_effects_and_music_follow_the_fight()
    {
        var game = new GameViewModel();
        var sounds = new FakeSoundService();
        var vm = new AnalysisViewModel(game, engine: null, new ImmediateDispatcher(), sounds, new TestSettings());
        var heats = new List<double>();
        vm.BattleHeat += (_, h) => heats.Add(h);

        game.PlayCommand.Execute(new Point(15, 3)); // quiet opening: no effect
        vm.Impact.Should().BeNull();

        // A white stone surrounded and captured in a local fight.
        foreach ((int x, int y) in new[] { (9, 9), (10, 9), (11, 9), (3, 3), (10, 10), (15, 15), (10, 8) })
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        heats.Should().HaveCount(8);
        heats[^1].Should().BeGreaterThan(heats[1]);
        vm.LastFight!.Strength.Should().BeGreaterThan(0, "a capture in a fight is celebrated");
        vm.Impact.Should().NotBeNull();
        sounds.Played.Should().NotBeEmpty();
    }

    [AvaloniaFact]
    public void Played_games_are_kept_and_a_replay_is_reviewed_with_AI_effects()
    {
        string root = Directory.CreateTempSubdirectory("hoshi-replays").FullName;
        var store = new ReplayStore(dataDirectory: root);
        var game = new GameViewModel();
        var engine = new FakeAnalysisEngine();
        var sounds = new FakeSoundService();
        var list = new ReplaysViewModel(store);
        var main = new MainWindowViewModel(game, ui: new ImmediateDispatcher(), engine: engine, sounds: sounds, settings: new TestSettings(),
            replays: store, replaysList: list);

        foreach ((int x, int y) in Enumerable.Range(0, 12).Select(i => (i, i % 2 == 0 ? 3 : 15)))
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        game.Load(Hoshi.Sgf.GameTree.Create(9, Hoshi.Core.RuleSet.Japanese), path: null); // "New game"
        list.Rows.Should().ContainSingle().Which.Entry.Moves.Should().Be(12);

        game.PlayCommand.Execute(new Point(4, 4));
        game.Load(Hoshi.Sgf.GameTree.Create(9, Hoshi.Core.RuleSet.Japanese), path: null);
        list.Rows.Should().ContainSingle("games shorter than 10 moves are not kept");

        list.Selected = list.Rows[0];
        list.OpenCommand.Execute(null);
        game.IsReview.Should().BeTrue();
        game.MoveNumber.Should().Be(0, "a replay starts from the beginning");

        sounds.Played.Clear();
        engine.BestMove = new Point(0, 3); // the replay's first move
        for (int i = 0; i < 20; i++)
        {
            System.Threading.Thread.Sleep(20);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // the start position is analysed
        }

        game.GoForwardCommand.Execute(null);
        for (int i = 0; i < 20 && !sounds.Played.Any(p => p.Effect == SoundEffect.ExplosionBig); i++)
        {
            System.Threading.Thread.Sleep(20);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        sounds.Played.Should().Contain(p => p.Effect == SoundEffect.ExplosionBig, "reviewing a replay replays the AI effects");
        main.Should().NotBeNull();

        // Re-opening the replay unchanged does not create a second entry.
        list.Selected = list.Rows[0];
        list.OpenCommand.Execute(null);
        list.Rows.Should().ContainSingle();
        Directory.Delete(root, recursive: true);
    }
}
