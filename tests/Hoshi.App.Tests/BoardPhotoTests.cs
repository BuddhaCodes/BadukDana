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

        photo.Tap(10, 10); // a fifth tap starts the corners over
        photo.Corners.Should().ContainSingle();
        photo.HasResult.Should().BeFalse();
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
}
