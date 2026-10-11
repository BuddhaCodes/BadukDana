using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.App.Themes;
using Hoshi.Core;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

/// <summary>Tap and confirm (phones): the stone waits at the tapped point until it is tapped again.</summary>
public sealed partial class GoBoardControl
{
    public static readonly StyledProperty<Point?> PendingPointProperty =
        AvaloniaProperty.Register<GoBoardControl, Point?>(nameof(PendingPoint));

    public static readonly StyledProperty<Stone> PendingStoneProperty =
        AvaloniaProperty.Register<GoBoardControl, Stone>(nameof(PendingStone));

    /// <summary>Where the stone waits for confirmation, or null.</summary>
    public Point? PendingPoint
    {
        get => GetValue(PendingPointProperty);
        set => SetValue(PendingPointProperty, value);
    }

    public Stone PendingStone
    {
        get => GetValue(PendingStoneProperty);
        set => SetValue(PendingStoneProperty, value);
    }

    /// <summary>The waiting stone, half transparent, with guide lines along its row and column and a pulsing ring.</summary>
    private void DrawPending(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style)
    {
        if (PendingPoint is not { } p || PendingStone == Stone.Empty || !board.IsOnBoard(p) || board[p] != Stone.Empty)
        {
            return;
        }

        AvPoint c = g.Center(p);
        var guide = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(150, 214, 150, 30)), Math.Max(1, g.Cell * 0.06));
        context.DrawLine(guide, g.Center(new Point(0, p.Y)), g.Center(new Point(board.Width - 1, p.Y)));
        context.DrawLine(guide, g.Center(new Point(p.X, 0)), g.Center(new Point(p.X, board.Height - 1)));
        using (context.PushOpacity(0.72))
        {
            DrawStone(context, c, g.Cell * StoneRadius, PendingStone, style, p);
        }

        context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromRgb(214, 150, 30)), Math.Max(1.5, g.Cell * 0.08)), c, g.Cell * 0.58, g.Cell * 0.58);
    }
}
