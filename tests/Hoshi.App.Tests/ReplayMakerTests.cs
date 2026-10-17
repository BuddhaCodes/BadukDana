using Avalonia.Headless.XUnit;
using Hoshi.App.Services;
using Hoshi.App.Services.Export;
using Hoshi.App.Services.Study;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>The replay maker: the plan of steps, the sound track, the encoders and the window's model.</summary>
public sealed class ReplayMakerTests
{
    private static (GameTree Tree, GameNode Last, GameNode Mistake, GameNode Capture) Game()
    {
        GameTree tree = GameTree.Create(9, RuleSet.Japanese);
        tree.Info.BlackPlayer = "Ana";
        tree.Info.WhitePlayer = "Luis";
        tree.Info.Result = "B+R";
        var cursor = new GameCursor(tree);
        GameNode? mistake = null, capture = null;
        // White (1,0) is captured by black's third move (move 5).
        foreach ((int x, int y) in new[] { (0, 0), (1, 0), (2, 0), (8, 8), (1, 1), (7, 7) })
        {
            cursor.Play(new Point(x, y));
            if (cursor.MoveNumber == 4)
            {
                mistake = cursor.Current;
            }

            if (cursor.MoveNumber == 5)
            {
                capture = cursor.Current;
            }
        }

        mistake!.Comment = "Too slow.\n[KataGo] Blunder";
        return (tree, cursor.Current, mistake, capture!);
    }

    [Fact]
    public void The_plan_has_a_title_every_move_in_the_range_and_the_result()
    {
        (GameTree tree, GameNode last, GameNode mistake, GameNode capture) = Game();
        var verdicts = new Dictionary<GameNode, StudyVerdict> { [mistake] = new(MoveQuality.Blunder, 6.5, new Point(4, 4)) };
        IReadOnlyList<ReplayStep> steps = ReplayPlan.Build(tree, tree.Root, new ReplayOptions { SecondsPerMove = 1 }, verdicts);

        steps.Select(s => s.Kind).Should().Equal(
            ReplayStepKind.Intro, ReplayStepKind.Move, ReplayStepKind.Move, ReplayStepKind.Move, ReplayStepKind.Move,
            ReplayStepKind.Move, ReplayStepKind.Move, ReplayStepKind.Outro);
        steps[0].Title.Should().Be("Ana — Luis");
        steps[^1].Caption.Should().Contain("B+R");

        ReplayStep blunder = steps.Single(s => s.Node == mistake);
        blunder.Highlight.Should().Contain("6.5").And.Contain("E5");
        blunder.Caption.Should().Be("Too slow.", "Hoshi's own KataGo lines are not notes");
        blunder.Duration.Should().BeGreaterThan(TimeSpan.FromSeconds(2.4));
        blunder.Marks.Should().HaveCount(2);

        ReplayStep taken = steps.Single(s => s.Node == capture);
        taken.Sound.Should().Be(ReplaySound.Capture);
        taken.Duration.Should().Be(TimeSpan.FromSeconds(ReplayPlan.CaptureFactor));
        steps[1].Sound.Should().Be(ReplaySound.Stone);
        steps[1].Duration.Should().Be(TimeSpan.FromSeconds(1));

        IReadOnlyList<ReplayStep> cut = ReplayPlan.Build(tree, last, new ReplayOptions { FromMove = 3, ToMove = 4, Intro = false, Highlights = false, Notes = false, Sounds = false });
        cut.Where(s => s.Kind == ReplayStepKind.Move).Select(s => s.MoveNumber).Should().Equal(3, 4);
        cut.Should().OnlyContain(s => s.Sound == ReplaySound.None && s.Highlight == null);
        cut[^1].Caption.Should().NotContain("B+R", "the result only when the replay reaches the end");

        IReadOnlyList<ReplayStep> hidden = ReplayPlan.Build(tree, last, new ReplayOptions { Share = new ShareOptions(HideNames: true) });
        hidden[0].Title.Should().NotContain("Ana");
    }

    [Fact]
    public void The_sound_track_puts_each_click_where_its_picture_starts()
    {
        BoardState board = BoardState.Create(9);
        var steps = new[]
        {
            new ReplayStep(ReplayStepKind.Intro, board, null, 0, "t", "", null, TimeSpan.FromSeconds(1), ReplaySound.None),
            new ReplayStep(ReplayStepKind.Move, board, null, 1, "t", "", null, TimeSpan.FromSeconds(0.5), ReplaySound.Stone),
        };
        var click = new PcmClip([1000, 1000, 2000, 2000]);
        byte[] wav = ReplayAudio.Track(steps, new Dictionary<ReplaySound, PcmClip> { [ReplaySound.Stone] = click }, volume: 1);

        PcmClip read = PcmClip.FromWav(wav)!;
        read.Samples.Length.Should().Be((int)(1.5 * PcmClip.Rate) * 2);
        read.Samples[(PcmClip.Rate * 2) - 1].Should().Be(0);
        read.Samples[PcmClip.Rate * 2].Should().Be(1000, "the click starts after the one-second title");
        read.Samples[(PcmClip.Rate * 2) + 2].Should().Be(2000);
        PcmClip.FromWav([1, 2, 3]).Should().BeNull();
    }

    [Fact]
    public void Ffmpeg_gets_raw_frames_and_writes_h264_with_aac()
    {
        IReadOnlyList<string> args = FfmpegVideoEncoder.Arguments("out.mp4", 1280, 720, "a.wav");
        string line = string.Join(' ', args);
        line.Should().Contain("-f rawvideo -pix_fmt bgra -s 1280x720 -r 30 -i -").And.Contain("-i a.wav")
            .And.Contain("-c:v libx264").And.Contain("-pix_fmt yuv420p").And.Contain("-c:a aac");
        args[^1].Should().Be("out.mp4");
        string.Join(' ', FfmpegVideoEncoder.Arguments("o.mp4", 10, 10, null)).Should().NotContain("aac");
        new FfmpegVideoEncoder(() => "/nonexistent/ffmpeg").Find().Should().NotBe("/nonexistent/ffmpeg");
    }

    [AvaloniaFact]
    public async Task The_maker_previews_and_saves_a_gif_a_web_replay_and_an_mp4()
    {
        string dir = Directory.CreateTempSubdirectory("hoshi-replay").FullName;
        try
        {
            (GameTree tree, _, _, _) = Game();
            var game = new GameViewModel();
            game.Load(tree, path: null);
            var files = new ExportTo(dir);
            var encoder = new FfmpegVideoEncoder();
            var maker = new ReplayMakerViewModel(game, files, video: encoder);
            maker.Reset();
            maker.MaxMove.Should().Be(6);
            (maker.FromMove, maker.ToMove).Should().Be((1, 6));
            maker.Steps.Should().HaveCount(8);
            maker.Preview.Should().NotBeNull();
            maker.Preview!.PixelSize.Should().Be(new Avalonia.PixelSize(640, 360));

            maker.SelectedFormat = maker.Formats[2];
            maker.Preview!.PixelSize.Should().Be(new Avalonia.PixelSize(360, 640));
            maker.FromBox = 5;
            maker.ToMove.Should().Be(6);
            maker.Steps.Count(s => s.Kind == ReplayStepKind.Move).Should().Be(2);
            maker.ToBox = 2;
            maker.FromMove.Should().Be(2, "the range stays in order");
            maker.Reset();

            await maker.ExportGifCommand.ExecuteAsync(null);
            System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "out.gif")), 0, 6).Should().Be("GIF89a");

            await maker.ExportWebCommand.ExecuteAsync(null);
            string html = File.ReadAllText(Path.Combine(dir, "out.html"));
            html.Should().Contain("\"play\":[").And.Contain("\"ms\":");

            if (encoder.IsAvailable)
            {
                maker.SecondsPerMove = 0.2;
                maker.Intro = false;
                await maker.ExportMp4Command.ExecuteAsync(null);
                maker.Status.Should().Contain("out.mp4");
                byte[] mp4 = File.ReadAllBytes(Path.Combine(dir, "out.mp4"));
                System.Text.Encoding.ASCII.GetString(mp4, 4, 4).Should().Be("ftyp");
            }
            else
            {
                maker.CanMakeVideo.Should().BeFalse();
                maker.VideoHint.Should().NotBeNull();
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class ExportTo(string dir) : IFileDialogService
    {
        public Task<string?> PickSgfToOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickSgfToSaveAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickExportToSaveAsync(string suggestedName, ExportFileType type) =>
            Task.FromResult<string?>(Path.Combine(dir, "out." + type.Extension));
    }
}
