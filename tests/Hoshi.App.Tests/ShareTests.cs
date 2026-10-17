using Avalonia.Headless.XUnit;
using Hoshi.App.Services;
using Hoshi.App.Services.Export;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Exporting the board (PNG) and the game (animated GIF).</summary>
public sealed class ShareTests
{
    [Fact]
    public void Lzw_codes_decode_back_to_the_pixels()
    {
        var random = new Random(7);
        foreach (int length in new[] { 1, 2, 300, 20_000 })
        {
            // Long runs and noise, so the table fills and is cleared at 4096 codes.
            byte[] pixels = new byte[length];
            for (int i = 0; i < length; i++)
            {
                pixels[i] = (byte)(i % 97 < 60 ? 3 : random.Next(256));
            }

            Decode(GifEncoder.Lzw(pixels)).Should().Equal(pixels);
        }
    }

    [Fact]
    public void A_gif_has_its_header_one_palette_a_loop_and_a_frame_per_picture()
    {
        byte[] red = Solid(4, 3, 0, 0, 255);
        byte[] blue = Solid(4, 3, 255, 0, 0);
        byte[] gif = GifEncoder.Encode(4, 3, [new GifFrame(red, TimeSpan.FromMilliseconds(500)), new GifFrame(blue, TimeSpan.FromSeconds(2))]);

        System.Text.Encoding.ASCII.GetString(gif, 0, 6).Should().Be("GIF89a");
        BitConverter.ToUInt16(gif, 6).Should().Be(4);
        BitConverter.ToUInt16(gif, 8).Should().Be(3);
        System.Text.Encoding.ASCII.GetString(gif, 13 + 768 + 3, 11).Should().Be("NETSCAPE2.0");
        gif[^1].Should().Be(0x3B);
        Count(gif, [0x21, 0xF9, 0x04]).Should().Be(2);

        // Delays are in hundredths of a second.
        int first = IndexOf(gif, [0x21, 0xF9, 0x04], 0);
        BitConverter.ToUInt16(gif, first + 4).Should().Be(50);
        int second = IndexOf(gif, [0x21, 0xF9, 0x04], first + 1);
        BitConverter.ToUInt16(gif, second + 4).Should().Be(200);
    }

    [Fact]
    public void Later_frames_keep_only_the_rectangle_that_changed()
    {
        byte[] a = Solid(10, 10, 200, 200, 200);
        byte[] b = (byte[])a.Clone();
        Paint(b, 10, 6, 7, 0, 0, 0);
        Paint(b, 10, 7, 8, 0, 0, 0);
        byte[] gif = GifEncoder.Encode(10, 10, [new GifFrame(a, TimeSpan.FromSeconds(1)), new GifFrame(b, TimeSpan.FromSeconds(1))]);

        int second = IndexOf(gif, [0x21, 0xF9, 0x04], IndexOf(gif, [0x21, 0xF9, 0x04], 0) + 1);
        int descriptor = second + 8;
        gif[descriptor].Should().Be(0x2C);
        (BitConverter.ToUInt16(gif, descriptor + 1), BitConverter.ToUInt16(gif, descriptor + 3)).Should().Be(((ushort)6, (ushort)7));
        (BitConverter.ToUInt16(gif, descriptor + 5), BitConverter.ToUInt16(gif, descriptor + 7)).Should().Be(((ushort)2, (ushort)2));
    }

    [Fact]
    public void The_palette_keeps_rare_colours_apart_from_the_common_ones()
    {
        // Mostly wood-like browns, a few red pixels (the last-move mark): red must keep its own entry.
        byte[] pixels = new byte[64 * 64 * 4];
        var random = new Random(3);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = (byte)random.Next(60, 90);
            pixels[i + 1] = (byte)random.Next(120, 160);
            pixels[i + 2] = (byte)random.Next(190, 230);
            pixels[i + 3] = 255;
        }

        Paint(pixels, 64, 10, 10, 0, 0, 255);
        (byte[] palette, byte[] lookup) = GifEncoder.Palette([new GifFrame(pixels, TimeSpan.Zero)]);
        int index = lookup[(31 << 10) | (0 << 5) | 0];
        palette[index * 3].Should().BeGreaterThan(240);
        palette[(index * 3) + 1].Should().BeLessThan(16);
    }

    [AvaloniaFact]
    public void The_board_exports_as_a_png_of_the_shown_position()
    {
        var game = new GameViewModel();
        game.Load(GameTree.Create(9, RuleSet.Japanese), path: null);
        game.PlayCommand.Execute(new Point(4, 4));
        var share = new ShareViewModel(game);

        byte[] png = share.ImagePng();
        png.Take(8).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        share.CurrentShot().LastMove.Should().Be(new Point(4, 4));
    }

    [AvaloniaFact]
    public void The_animation_runs_through_the_line_to_its_end_and_lingers_on_captures_and_blunders()
    {
        var game = new GameViewModel();
        game.Load(GameTree.Create(9, RuleSet.Japanese), path: null);
        // Black captures the white stone at (1,0) with the third black move (move 5).
        foreach ((int x, int y) in new[] { (0, 0), (1, 0), (2, 0), (8, 8), (1, 1), (7, 7) })
        {
            game.PlayCommand.Execute(new Point(x, y));
        }

        GameNode blunder = game.CurrentNode.Parent!.Parent!.Parent!.Parent!; // move 2
        game.Cursor.GoTo(GameCursor.Path(game.CurrentNode)[2]); // shown: move 2; the animation still goes to the end
        var verdicts = new Dictionary<GameNode, MoveQuality> { [blunder] = MoveQuality.Blunder };
        var share = new ShareViewModel(game, verdicts: () => verdicts, coordinates: () => false);

        IReadOnlyList<GifFrame> frames = share.AnimationFrames(pixels: 96);
        frames.Should().HaveCount(7, "the empty board and six moves");
        frames.Select(f => f.Delay).Should().Equal(
            ShareViewModel.MoveDelay, ShareViewModel.MoveDelay, ShareViewModel.BigMoveDelay, ShareViewModel.MoveDelay,
            ShareViewModel.MoveDelay, ShareViewModel.BigMoveDelay, ShareViewModel.LastFrameDelay);
        frames.Should().OnlyContain(f => f.Bgra.Length == 96 * 96 * 4);

        byte[] gif = GifEncoder.Encode(96, 96, frames);
        Count(gif, [0x21, 0xF9, 0x04]).Should().Be(7);
    }

    [AvaloniaFact]
    public async Task Export_writes_the_file_the_user_picked()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hoshi-share-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = new PickingFiles(dir);
            var game = new GameViewModel();
            game.PlayCommand.Execute(new Point(3, 3));
            var share = new ShareViewModel(game, files);

            await share.ExportImageCommand.ExecuteAsync(null);
            await share.ExportAnimationCommand.ExecuteAsync(null);

            files.Asked.Select(t => t.Type).Should().Equal(ExportFileType.Png, ExportFileType.Gif);
            files.Asked[0].Name.Should().EndWith(".png");
            File.ReadAllBytes(Path.Combine(dir, "out.png")).Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(dir, "out.gif")), 0, 6).Should().Be("GIF89a");
            game.StatusText.Should().Contain("out.gif");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task Diagrams_and_the_sgf_go_to_the_clipboard_and_pasting_opens_games_and_links()
    {
        var clipboard = new FakeClipboard();
        string? link = null;
        var game = new GameViewModel();
        game.Load(GameTree.Create(9, RuleSet.Japanese), path: null);
        game.PlayCommand.Execute(new Point(4, 4));
        var share = new ShareViewModel(game, clipboard: clipboard, openLink: l => { link = l; return Task.CompletedTask; });

        await share.CopyDiagramCommand.ExecuteAsync(null);
        clipboard.Text.Should().StartWith("$$Wc9 ").And.Contain("$$ | . . . . B . . . . |");
        await share.CopyCodeBlockCommand.ExecuteAsync(null);
        clipboard.Text.Should().StartWith("```").And.Contain("[X]");
        await share.CopySgfCommand.ExecuteAsync(null);
        clipboard.Text.Should().StartWith("(;").And.Contain("B[ee]");

        clipboard.Text = "(;GM[1]FF[4]SZ[13];B[dd];W[jj])";
        await share.PasteSgfCommand.ExecuteAsync(null);
        game.Board.Width.Should().Be(13);
        game.Tree.Root.Children.Should().ContainSingle();

        clipboard.Text = " https://online-go.com/game/12345 ";
        await share.PasteSgfCommand.ExecuteAsync(null);
        link.Should().Be("https://online-go.com/game/12345");

        clipboard.Text = "hello";
        await share.PasteSgfCommand.ExecuteAsync(null);
        game.StatusText.Should().Be(Hoshi.Core.Localization.Tr.T("Share.NothingToPaste"));
    }

    [AvaloniaFact]
    public void The_web_viewer_holds_every_position_with_the_notes_and_can_hide_the_names()
    {
        var game = new GameViewModel();
        game.Load(GameTree.Create(9, RuleSet.Japanese), path: null);
        game.Tree.Info.BlackPlayer = "Ana";
        game.Tree.Info.WhitePlayer = "Luis</script><b>";
        game.PlayCommand.Execute(new Point(2, 2));
        game.PlayCommand.Execute(new Point(6, 6));
        game.CurrentNode.SetValue("C", "Nice <move>");
        game.Cursor.GoTo(game.CurrentNode.Parent!);
        game.PlayCommand.Execute(new Point(6, 2)); // a variation
        var share = new ShareViewModel(game);

        string html = share.WebViewerHtml();
        html.Should().StartWith("<!doctype html>").And.Contain("Ana").And.Contain("<footer>");
        html.Should().NotContain("Luis</script>", "names and comments cannot end the page's script");
        html.Should().NotContain("<move>");

        string json = html[(html.IndexOf("id=\"game\">", StringComparison.Ordinal) + 10)..html.IndexOf("</script><script>", StringComparison.Ordinal)];
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var nodes = doc.RootElement.GetProperty("nodes");
        nodes.GetArrayLength().Should().Be(4, "the root, the first move and both replies");
        nodes[1].GetProperty("b").GetString()![(2 * 9) + 2].Should().Be('b');
        nodes[1].GetProperty("k").GetArrayLength().Should().Be(2);
        nodes[2].GetProperty("c").GetString().Should().Be("Nice <move>");
        nodes[2].GetProperty("l").GetString().Should().Be("G3");
        doc.RootElement.GetProperty("start").GetInt32().Should().Be(3, "it opens on the shown move");

        share.HideNames = true;
        share.ShowBrand = false;
        html = share.WebViewerHtml();
        html.Should().NotContain("Ana").And.NotContain("Luis").And.NotContain("<footer>");
    }

    [AvaloniaFact]
    public async Task Cards_and_problems_carry_the_moment_and_the_answer_stays_a_spoiler()
    {
        var clipboard = new FakeClipboard();
        string dir = Path.Combine(Path.GetTempPath(), "hoshi-card-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var settings = new TestSettings();
            var game = new GameViewModel();
            game.Load(GameTree.Create(9, RuleSet.Japanese), path: null);
            game.Tree.Info.BlackPlayer = "Ana";
            game.Tree.Info.WhitePlayer = "Luis";
            game.PlayCommand.Execute(new Point(2, 2));
            game.PlayCommand.Execute(new Point(6, 6));
            game.CurrentNode.SetValue("C", "Too slow.");
            var verdicts = new Dictionary<GameNode, MoveQuality> { [game.CurrentNode] = MoveQuality.Mistake };
            var share = new ShareViewModel(game, new PickingFiles(dir), () => verdicts, clipboard: clipboard, settings: settings);

            CardText text = share.MomentText();
            text.Title.Should().Be("Ana vs Luis");
            text.Subtitle.Should().Contain("2").And.Contain("G3");
            text.Verdict.Should().Contain(AnalysisViewModel.QualityName(MoveQuality.Mistake));
            text.Note.Should().Be("Too slow.");

            share.HideNames = true;
            share.ShowBrand = false;
            settings.Current.ShareHideNames.Should().BeTrue();
            settings.Current.ShareShowBrand.Should().BeFalse();
            share.MomentText().Title.Should().NotContain("Ana");
            share.MomentText().Brand.Should().BeFalse();

            foreach (CardFormat format in Enum.GetValues<CardFormat>())
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(new MemoryStream(share.MomentCardPng(format)));
                bitmap.PixelSize.Should().Be(ShareCard.Size(format));
            }

            (byte[] png, string spoiler) = share.Problem()!.Value;
            png.Take(4).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            spoiler.Should().Contain("||G3||").And.Contain(">!G3!<");

            await share.ExportProblemCommand.ExecuteAsync(null);
            File.Exists(Path.Combine(dir, "out.png")).Should().BeTrue();
            clipboard.Text.Should().Be(spoiler);

            game.Cursor.First();
            share.Problem().Should().BeNull("the start of the game has no move to answer");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public string? Text { get; set; }

        public Task SetTextAsync(string text)
        {
            Text = text;
            return Task.CompletedTask;
        }

        public Task<string?> GetTextAsync() => Task.FromResult(Text);
    }

    private sealed class PickingFiles(string dir) : IFileDialogService
    {
        public List<(string Name, ExportFileType Type)> Asked { get; } = [];

        public Task<string?> PickSgfToOpenAsync() => Task.FromResult<string?>(null);

        public Task<string?> PickSgfToSaveAsync(string suggestedName) => Task.FromResult<string?>(null);

        public Task<string?> PickExportToSaveAsync(string suggestedName, ExportFileType type)
        {
            Asked.Add((suggestedName, type));
            return Task.FromResult<string?>(Path.Combine(dir, "out." + type.Extension));
        }
    }

    private static byte[] Solid(int w, int h, byte b, byte g, byte r)
    {
        byte[] p = new byte[w * h * 4];
        for (int i = 0; i < p.Length; i += 4)
        {
            (p[i], p[i + 1], p[i + 2], p[i + 3]) = (b, g, r, 255);
        }

        return p;
    }

    private static void Paint(byte[] p, int width, int x, int y, byte b, byte g, byte r)
    {
        int i = ((y * width) + x) * 4;
        (p[i], p[i + 1], p[i + 2]) = (b, g, r);
    }

    private static int IndexOf(byte[] data, byte[] pattern, int from)
    {
        for (int i = from; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }

    private static int Count(byte[] data, byte[] pattern)
    {
        int n = 0;
        for (int i = IndexOf(data, pattern, 0); i >= 0; i = IndexOf(data, pattern, i + 1))
        {
            n++;
        }

        return n;
    }

    // A plain GIF LZW decoder (8-bit pixels), written from the format description to check the encoder.
    private static byte[] Decode(byte[] codes)
    {
        const int clear = 256, end = 257;
        var output = new List<byte>();
        var table = new List<byte[]>();
        void Reset()
        {
            table.Clear();
            for (int i = 0; i < 256; i++)
            {
                table.Add([(byte)i]);
            }

            table.Add([]);
            table.Add([]);
        }

        Reset();
        int size = 9, bitPos = 0;
        byte[]? previous = null;
        while (true)
        {
            int code = 0;
            for (int b = 0; b < size; b++, bitPos++)
            {
                if (((codes[bitPos >> 3] >> (bitPos & 7)) & 1) != 0)
                {
                    code |= 1 << b;
                }
            }

            if (code == clear)
            {
                Reset();
                size = 9;
                previous = null;
                continue;
            }

            if (code == end)
            {
                return [.. output];
            }

            byte[] entry = code < table.Count ? table[code] : [.. previous!, previous![0]];
            output.AddRange(entry);
            if (previous is not null && table.Count < 4096)
            {
                table.Add([.. previous, entry[0]]);
            }

            previous = entry;
            if (table.Count == (1 << size) && size < 12)
            {
                size++;
            }
        }
    }
}
