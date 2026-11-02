using FluentAssertions;
using Hoshi.App.Services.Photo;
using Hoshi.Core;

namespace Hoshi.App.Tests;

/// <summary>"Photo of the board → SGF" on synthetic photos: taken at an angle, uneven light, wood grain noise.</summary>
public sealed class BoardPhotoTests
{
    private static readonly (double X, double Y)[] Tilted = [(140, 90), (860, 120), (930, 820), (80, 760)];

    /// <summary>Paints a board seen at an angle: wood with a light gradient and noise, grid lines, stones.</summary>
    private static PhotoPixels Photo(int size, Stone[,] stones, (double X, double Y)[] corners, int seed = 1)
    {
        const int w = 1000, h = 900;
        var bgra = new byte[w * h * 4];
        var random = new Random(seed);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                double light = 0.85 + (0.3 * x / w); // brighter to the right, like a lamp on one side
                double grain = random.Next(-12, 12);
                Put(bgra, w, x, y, Clamp((100 * light) + grain), Clamp((160 * light) + grain), Clamp((205 * light) + grain));
            }
        }

        double[] hom = BoardRecognizer.Homography(corners);
        for (int i = 0; i < size; i++)
        {
            for (int t = 0; t <= 2000; t++)
            {
                double s = t / 2000.0, line = (double)i / (size - 1);
                foreach ((double u, double v) in new[] { (s, line), (line, s) })
                {
                    (double px, double py) = BoardRecognizer.Map(hom, u, v);
                    Put(bgra, w, (int)px, (int)py, 30, 40, 50);
                }
            }
        }

        double spacing = 700.0 / (size - 1);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (stones[x, y] == Stone.Empty)
                {
                    continue;
                }

                (double cx, double cy) = BoardRecognizer.Map(hom, (double)x / (size - 1), (double)y / (size - 1));
                byte c = stones[x, y] == Stone.Black ? (byte)25 : (byte)232;
                int r = (int)(spacing * 0.46);
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if ((dx * dx) + (dy * dy) <= r * r)
                        {
                            byte shade = Clamp(c + random.Next(-8, 8));
                            Put(bgra, w, (int)cx + dx, (int)cy + dy, shade, shade, shade);
                        }
                    }
                }
            }
        }

        return new PhotoPixels(w, h, bgra);
    }

    private static void Put(byte[] bgra, int w, int x, int y, byte b, byte g, byte r)
    {
        if (x < 0 || y < 0 || x >= w || y >= bgra.Length / 4 / w)
        {
            return;
        }

        int i = ((y * w) + x) * 4;
        (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, 255);
    }

    private static byte Clamp(double v) => (byte)Math.Clamp(v, 0, 255);

    private static Stone[,] Position(int size, int seed)
    {
        var stones = new Stone[size, size];
        var random = new Random(seed);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int r = random.Next(10);
                stones[x, y] = r < 2 ? Stone.Black : r < 4 ? Stone.White : Stone.Empty;
            }
        }

        return stones;
    }

    [Theory]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(19)]
    public void Stones_are_read_from_a_photo_taken_at_an_angle(int size)
    {
        Stone[,] truth = Position(size, seed: size);
        RecognizedBoard read = BoardRecognizer.Recognize(Photo(size, truth, Tilted), Tilted, size);

        int wrong = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                wrong += read.Stones[x, y] == truth[x, y] ? 0 : 1;
            }
        }

        wrong.Should().Be(0);
    }

    [Fact]
    public void An_empty_board_and_a_board_with_only_black_stones_find_no_phantom_stones()
    {
        var empty = new Stone[9, 9];
        BoardRecognizer.Recognize(Photo(9, empty, Tilted), Tilted, 9).Count(Stone.Black).Should().Be(0);
        BoardRecognizer.Recognize(Photo(9, empty, Tilted), Tilted, 9).Count(Stone.White).Should().Be(0);

        var black = new Stone[9, 9];
        black[2, 2] = black[6, 6] = black[4, 4] = Stone.Black;
        RecognizedBoard read = BoardRecognizer.Recognize(Photo(9, black, Tilted), Tilted, 9);
        read.Count(Stone.Black).Should().Be(3);
        read.Count(Stone.White).Should().Be(0);
    }

    [Fact]
    public void The_result_opens_as_a_game_with_setup_stones_and_the_player_to_move()
    {
        var stones = new Stone[9, 9];
        stones[2, 2] = Stone.Black;
        stones[6, 6] = Stone.White;
        var board = new RecognizedBoard(9, stones, new double[9, 9]);

        Hoshi.Sgf.GameTree game = BoardRecognizer.ToGame(board, Stone.White);

        game.Root.GetValue("AB").Should().Be("cc");
        game.Root.GetValue("AW").Should().Be("gg");
        game.Root.GetValue("PL").Should().Be("W");
    }

    [Fact]
    public void Tapping_four_corners_reads_the_board_and_opens_it_in_the_editor()
    {
        var truth = new Stone[9, 9];
        truth[2, 2] = Stone.Black;
        truth[6, 2] = Stone.White;
        truth[4, 6] = Stone.Black;
        var game = new Hoshi.App.ViewModels.GameViewModel();
        var photo = new Hoshi.App.ViewModels.BoardPhotoViewModel(game) { Size = 9 };
        photo.Load(null, Photo(9, truth, Tilted));

        foreach ((double x, double y) in Tilted)
        {
            photo.Tap(x, y);
        }

        photo.HasResult.Should().BeTrue();
        photo.Marks.Count(m => m.Label.Length == 0).Should().Be(3, "the stones are drawn on the photo");
        bool opened = false;
        photo.Opened += (_, _) => opened = true;
        photo.OpenCommand.Execute(null);

        opened.Should().BeTrue();
        game.IsEditMode.Should().BeTrue();
        game.Board[new Point(2, 2)].Should().Be(Stone.Black);
        game.Board[new Point(6, 2)].Should().Be(Stone.White);

        photo.Tap(Tilted[2].X + 4, Tilted[2].Y - 3); // a fifth tap moves the nearest corner
        photo.Corners.Should().HaveCount(4);
        photo.Corners[2].Should().Be((Tilted[2].X + 4, Tilted[2].Y - 3));
        photo.HasResult.Should().BeTrue();

        photo.ResetCommand.Execute(null); // "Corners again": all by hand
        photo.Corners.Should().BeEmpty();
        photo.HasResult.Should().BeFalse();
    }

    [Fact]
    public async Task A_photo_finds_its_corners_rotates_and_opens_to_study_kept_in_the_library()
    {
        var truth = new Stone[9, 9];
        truth[2, 2] = Stone.Black;
        truth[6, 2] = Stone.White;
        truth[4, 6] = Stone.Black;
        (PhotoPixels pixels, _) = OnTable(9, truth, 0.06);
        var game = new Hoshi.App.ViewModels.GameViewModel();
        var study = new Hoshi.App.ViewModels.StudyViewModel(game);
        Hoshi.Sgf.GameTree? kept = null;
        var photo = new Hoshi.App.ViewModels.BoardPhotoViewModel(game, study: study, keep: t => (kept = t) is not null) { Size = 9 };
        photo.Load(null, pixels);

        await photo.FindCornersAsync();

        photo.Corners.Should().HaveCount(4);
        photo.HasResult.Should().BeTrue();
        photo.Status.Should().Contain("2").And.Contain("1");

        photo.RotateCommand.Execute(null); // taken from White's side: a quarter turn
        photo.StudyCommand.Execute(null);

        kept.Should().BeSameAs(game.Tree);
        study.IsActive.Should().BeTrue();
        game.IsEditMode.Should().BeFalse();
        // (x, y) → (y, 8 − x) after the quarter turn of the corners.
        game.Board[new Point(2, 6)].Should().Be(Stone.Black);
        game.Board[new Point(2, 2)].Should().Be(Stone.White);
        game.Board[new Point(6, 4)].Should().Be(Stone.Black);
    }

    [Fact]
    public void Positions_from_photos_are_kept_in_the_library_without_moves_and_left_out_of_the_summaries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hoshi-photo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new Hoshi.App.Services.ReplayStore(dataDirectory: dir);
            Hoshi.Sgf.GameTree tree = Hoshi.Sgf.GameTree.Create(9, RuleSet.Japanese);
            store.Save(tree, "local-1", "local").Should().BeNull("a game without moves is not kept");
            store.Save(tree, "photo-1", Hoshi.App.Services.ReplayStore.PhotoSource).Should().NotBeNull();
            new Hoshi.App.ViewModels.ReplayRow(store.List()[0]).Details.Should().EndWith("Foto");
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_photo_window_shows_the_photo_the_corners_and_the_stones()
    {
        Stone[,] truth = Position(9, seed: 3);
        PhotoPixels pixels = Photo(9, truth, Tilted);
        var bitmap = new Avalonia.Media.Imaging.WriteableBitmap(new Avalonia.PixelSize(pixels.Width, pixels.Height), new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormats.Bgra8888, Avalonia.Platform.AlphaFormat.Opaque);
        using (Avalonia.Platform.ILockedFramebuffer fb = bitmap.Lock())
        {
            for (int y = 0; y < pixels.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(pixels.Bgra, y * pixels.Width * 4, fb.Address + (y * fb.RowBytes), pixels.Width * 4);
            }
        }

        var photo = new Hoshi.App.ViewModels.BoardPhotoViewModel(new Hoshi.App.ViewModels.GameViewModel()) { Size = 9 };
        photo.Load(bitmap);
        foreach ((double x, double y) in Tilted)
        {
            photo.Tap(x, y);
        }

        var window = new Hoshi.App.Views.BoardPhotoWindow { DataContext = photo, Width = 900, Height = 760 };
        window.Show();
        window.FindDeep<Avalonia.Controls.Button>("PhotoOpen")!.IsEnabled.Should().BeTrue();
        photo.Status.Should().Contain(photo.Marks.Count(m => m.Stone == Stone.Black).ToString(System.Globalization.CultureInfo.InvariantCulture));
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        using (Avalonia.Media.Imaging.WriteableBitmap? frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window))
        {
            frame!.Save(Path.Combine(dir, "board-photo.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        window.Close();
    }

    /// <summary>A board lying on a dark table, seen at an angle: wood only inside the board's outline.</summary>
    private static (PhotoPixels Photo, (double X, double Y)[] Grid) OnTable(int size, Stone[,] stones, double margin, int seed = 5)
    {
        (double X, double Y)[] outline = [(170, 110), (840, 140), (900, 800), (110, 760)];
        double[] board = BoardRecognizer.Homography(outline);
        (double, double) At(double u, double v) => BoardRecognizer.Map(board, u, v);
        (double X, double Y)[] grid = [At(margin, margin), At(1 - margin, margin), At(1 - margin, 1 - margin), At(margin, 1 - margin)];
        PhotoPixels photo = Photo(size, stones, grid, seed);

        // Repaint everything outside the board's outline as the table.
        double[] inverse = Invert(board);
        for (int y = 0; y < photo.Height; y++)
        {
            for (int x = 0; x < photo.Width; x++)
            {
                (double u, double v) = BoardRecognizer.Map(inverse, x, y);
                if (u < 0 || v < 0 || u > 1 || v > 1)
                {
                    int i = ((y * photo.Width) + x) * 4;
                    (photo.Bgra[i], photo.Bgra[i + 1], photo.Bgra[i + 2]) = ((byte)55, (byte)62, (byte)58);
                }
            }
        }

        return (photo, grid);
    }

    private static double[] Invert(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], k = m[8];
        double det = (a * ((e * k) - (f * h))) - (b * ((d * k) - (f * g))) + (c * ((d * h) - (e * g)));
        return
        [
            ((e * k) - (f * h)) / det, ((c * h) - (b * k)) / det, ((b * f) - (c * e)) / det,
            ((f * g) - (d * k)) / det, ((a * k) - (c * g)) / det, ((c * d) - (a * f)) / det,
            ((d * h) - (e * g)) / det, ((b * g) - (a * h)) / det, ((a * e) - (b * d)) / det,
        ];
    }

    [Theory]
    [InlineData(9, 0.06)]
    [InlineData(13, 0.04)]
    [InlineData(19, 0.03)]
    public void The_board_is_found_by_itself_and_read_from_the_corners_it_finds(int size, double margin)
    {
        Stone[,] truth = Position(size, seed: size + 1);
        (PhotoPixels photo, (double X, double Y)[] grid) = OnTable(size, truth, margin);

        IReadOnlyList<(double X, double Y)> found = BoardFinder.FindCorners(photo, size)!;

        found.Should().NotBeNull();
        double spacing = 650.0 / (size - 1);
        for (int i = 0; i < 4; i++)
        {
            Math.Sqrt(Math.Pow(found[i].X - grid[i].X, 2) + Math.Pow(found[i].Y - grid[i].Y, 2)).Should().BeLessThan(spacing * 0.15, $"corner {i}");
        }

        RecognizedBoard read = BoardRecognizer.Recognize(photo, found, size);
        int wrong = 0;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                wrong += read.Stones[x, y] == truth[x, y] ? 0 : 1;
            }
        }

        wrong.Should().Be(0);
    }

    [Fact]
    public void No_board_in_the_photo_means_no_guess()
    {
        var grey = new byte[200 * 150 * 4];
        Array.Fill(grey, (byte)90);
        BoardFinder.FindCorners(new PhotoPixels(200, 150, grey), 19).Should().BeNull();
    }
}
