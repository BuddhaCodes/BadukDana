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
