using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class GoBoardControlTests
{
    private static Point P(int x, int y) => new(x, y);

    private static (MainWindow Window, GoBoardControl Board, LocalGameViewModel Game) Open(int width, int height, int size = 19)
    {
        var game = new LocalGameViewModel(size, RuleSet.Japanese);
        var window = new MainWindow(new MainWindowViewModel(game)) { Width = width, Height = height };
        window.Show();
        return (window, window.FindControl<GoBoardControl>("Board")!, game);
    }

    private static Avalonia.Point InWindow(MainWindow window, GoBoardControl board, Point p)
    {
        Avalonia.Point local = board.CurrentGeometry!.Value.Center(p);
        return board.TranslatePoint(local, window)!.Value;
    }

    [AvaloniaFact]
    public void Clicking_an_intersection_plays_a_stone()
    {
        (MainWindow window, GoBoardControl board, LocalGameViewModel game) = Open(1000, 800);

        Avalonia.Point at = InWindow(window, board, P(3, 15));
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);

        game.Board[P(3, 15)].Should().Be(Stone.Black);
        game.LastMove.Should().Be(P(3, 15));
    }

    [AvaloniaFact]
    public void Hovering_updates_the_ghost_stone()
    {
        (MainWindow window, GoBoardControl board, LocalGameViewModel game) = Open(1000, 800);

        window.MouseMove(InWindow(window, board, P(10, 4)));

        game.HoverPoint.Should().Be(P(10, 4));
        board.GhostStone.Should().Be(Stone.Black);
    }

    [AvaloniaFact]
    public void Right_click_does_not_play()
    {
        (MainWindow window, GoBoardControl board, LocalGameViewModel game) = Open(1000, 800);
        Point? clicked = null;
        board.PointClicked += (_, e) => clicked = e.Point;

        Avalonia.Point at = InWindow(window, board, P(9, 9));
        window.MouseDown(at, MouseButton.Right);
        window.MouseUp(at, MouseButton.Right);

        clicked.Should().Be(P(9, 9), "the event still reports right clicks for context actions");
        game.Board[P(9, 9)].Should().Be(Stone.Empty);
    }

    [AvaloniaFact]
    public void Pass_key_passes()
    {
        (MainWindow window, _, LocalGameViewModel game) = Open(1000, 800);

        window.KeyPressQwerty(PhysicalKey.P, RawInputModifiers.None);

        game.MoveNumber.Should().Be(1);
        game.Board.ToMove.Should().Be(Stone.White);
    }

    [AvaloniaFact]
    public void Board_renders_wood_and_stones_and_is_saved_for_review()
    {
        (MainWindow window, GoBoardControl board, LocalGameViewModel game) = Open(1100, 800);
        PlaySample(game);
        board.Markers =
        [
            new BoardMarker(P(15, 15), BoardMarkerKind.Triangle),
            new BoardMarker(P(16, 13), BoardMarkerKind.Square),
            new BoardMarker(P(13, 16), BoardMarkerKind.Circle),
            new BoardMarker(P(12, 12), BoardMarkerKind.Cross),
            new BoardMarker(P(14, 12), BoardMarkerKind.Label, "A"),
            new BoardMarker(P(3, 3), BoardMarkerKind.Label, "B"),
        ];
        window.MouseMove(InWindow(window, board, P(9, 9)));

        using WriteableBitmap frame = Capture(window, "phase2-board-1100x800.png");

        BoardGeometry g = board.CurrentGeometry!.Value;
        Color black = PixelAt(frame, window, board, P(15, 3));
        Color white = PixelAt(frame, window, board, P(3, 15));
        Color wood = PixelAt(frame, window, board, P(9, 5), offsetCells: 0.5, diagonal: true);

        Luma(black).Should().BeLessThan(80);
        Luma(white).Should().BeGreaterThan(200);
        wood.R.Should().BeGreaterThan(wood.B, "wood is warm");
        Luma(wood).Should().BeInRange(120, 220);
        g.Cell.Should().BeGreaterThan(20);
    }

    [AvaloniaFact]
    public void Board_stays_square_when_resized_and_is_saved_for_review()
    {
        (MainWindow window, GoBoardControl board, LocalGameViewModel game) = Open(700, 900);
        PlaySample(game);

        using WriteableBitmap tall = Capture(window, "phase2-board-700x900.png");
        Avalonia.Rect r1 = board.CurrentGeometry!.Value.BoardRect;

        window.Width = 1400;
        window.Height = 700;
        using WriteableBitmap wide = Capture(window, "phase2-board-1400x700.png");
        Avalonia.Rect r2 = board.CurrentGeometry!.Value.BoardRect;

        r1.Width.Should().BeApproximately(r1.Height, 0.001);
        r2.Width.Should().BeApproximately(r2.Height, 0.001);
        r2.Height.Should().BeLessThan(r1.Height);
    }

    [AvaloniaFact]
    public void Small_boards_render()
    {
        (MainWindow window, _, LocalGameViewModel game) = Open(800, 800, size: 9);
        game.PlayCommand.Execute(P(4, 4));
        game.PlayCommand.Execute(P(2, 6));

        using WriteableBitmap frame = Capture(window, "phase2-board-9x9.png");
        frame.PixelSize.Width.Should().Be(800);
    }

    private static void PlaySample(LocalGameViewModel game)
    {
        // A short opening with a capture so white stones, black stones and the last-move mark are all visible.
        Point[] moves =
        [
            P(15, 3), P(3, 15), P(16, 15), P(3, 3), P(13, 16), P(15, 16), P(16, 16), P(15, 13),
            P(14, 15), P(16, 12), P(2, 13), P(5, 16), P(2, 5), P(4, 2), P(9, 3), P(13, 2),
        ];
        foreach (Point m in moves)
        {
            game.PlayCommand.Execute(m);
        }
    }

    private static WriteableBitmap Capture(Window window, string fileName)
    {
        WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, fileName));
        return frame;
    }

    private static Color PixelAt(WriteableBitmap frame, Window window, GoBoardControl board, Point p, double offsetCells = 0, bool diagonal = false)
    {
        BoardGeometry g = board.CurrentGeometry!.Value;
        Avalonia.Point local = g.Center(p) + new Avalonia.Vector(offsetCells * g.Cell, diagonal ? offsetCells * g.Cell : 0);
        Avalonia.Point w = board.TranslatePoint(local, window)!.Value;
        return Pixels.Read(frame, (int)w.X, (int)w.Y);
    }

    private static double Luma(Color c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);
}
