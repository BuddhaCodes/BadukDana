using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Sgf;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

public sealed partial class GoBoardControl
{
    /// <summary>
    /// Stone centre with Sabaki-style "fuzzy placement": a small, stable offset per intersection
    /// (one of eight directions or none) so the stones look placed by hand.
    /// </summary>
    public static AvPoint StoneCenter(BoardGeometry g, Point p)
    {
        int h = unchecked((p.X * 73856093) ^ (p.Y * 19349663)) & 0x7fffffff;
        int dir = h % 12;
        AvPoint c = g.Center(p);
        if (dir >= 8)
        {
            return c;
        }

        double angle = dir * Math.PI / 4;
        double d = g.Cell * 0.03;
        return new AvPoint(c.X + (Math.Cos(angle) * d), c.Y + (Math.Sin(angle) * d));
    }

    /// <summary>Draws one stone: one of Hoshi's rendered sprites when the style has a set, else the vector style.</summary>
    internal static void DrawStone(DrawingContext context, AvPoint c, double r, Stone color, BoardStyle board, Point p)
    {
        bool black = color == Stone.Black;
        if (board.StoneSet is { } set
            && Themes.Skins.StoneSprite(set, black, black ? board.BlackVariants : board.WhiteVariants, p.X, p.Y) is { } sprite)
        {
            context.DrawImage(sprite, new Rect(sprite.Size), new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
            return;
        }

        StoneStyle style = board.Stones;
        switch (style)
        {
            case StoneStyle.Pearl:
                context.DrawEllipse(black ? BoardTextures.PearlBlack : BoardTextures.PearlWhite, null, c, r * 0.97, r * 0.97);
                if (!black)
                {
                    context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x50, 0x9A, 0xA0, 0xB8)), Math.Max(0.5, r * 0.035)), c, r * 0.97, r * 0.97);
                }

                return;

            case StoneStyle.SlateShell:
                context.DrawEllipse(black ? BoardTextures.SlateBlack : BoardTextures.ShellWhite, null, c, r * 0.97, r * 0.97);
                if (!black)
                {
                    DrawShellLines(context, c, r * 0.97, p);
                    context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x48, 0x5A, 0x50, 0x3C)), Math.Max(0.5, r * 0.035)), c, r * 0.97, r * 0.97);
                }

                return;

            case StoneStyle.Soft:
                context.DrawEllipse(black ? BoardTextures.SoftBlack : BoardTextures.SoftWhite, null, c, r * 0.96, r * 0.96);
                return;

            default:
                DrawShudanStone(context, c, r, black);
                return;
        }
    }

    /// <summary>Shudan's stone in vector form: 20.5/21.5 body with a 1-unit rim, plus an 18.5/21.5 highlight.</summary>
    private static void DrawShudanStone(DrawingContext context, AvPoint c, double r, bool black)
    {
        double body = r * 20.5 / 21.5;
        double inner = r * 18.5 / 21.5;
        double rim = Math.Max(0.5, r / 21.5);
        context.DrawEllipse(black ? BoardTextures.BlackStone : BoardTextures.WhiteStone, null, c, body, body);
        context.DrawEllipse(black ? BoardTextures.BlackHighlight : BoardTextures.WhiteHighlight, null, c, inner, inner);
        IPen edge = black ? BoardTextures.BlackEdge : BoardTextures.WhiteEdge;
        context.DrawEllipse(null, new Pen(edge.Brush, rim), c, body, body);
    }

    /// <summary>Clam-shell growth lines: a few gentle arcs, orientation stable per intersection.</summary>
    private static void DrawShellLines(DrawingContext context, AvPoint c, double r, Point p)
    {
        int variant = Math.Abs((p.X * 7) + (p.Y * 13) + (p.X * p.Y)) % 6;
        double tilt = (variant - 2.5) * 0.22;
        var pen = new Pen(BoardTextures.ShellLine.Brush, Math.Max(0.5, r * 0.05));
        using (context.PushGeometryClip(new EllipseGeometry(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r))))
        {
            for (int i = -2; i <= 2; i++)
            {
                double offset = (i * r * 0.3) + (variant * r * 0.04);
                var g = new StreamGeometry();
                using (StreamGeometryContext ctx = g.Open())
                {
                    ctx.BeginFigure(new AvPoint(c.X - r, c.Y + offset + (tilt * r)), false);
                    ctx.QuadraticBezierTo(new AvPoint(c.X, c.Y + offset - (r * 0.25)), new AvPoint(c.X + r, c.Y + offset - (tilt * r)));
                    ctx.EndFigure(false);
                }

                context.DrawGeometry(null, pen, g);
            }
        }
    }

    private void DrawLastMove(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style)
    {
        if (LastMove is not { } p || !board.IsOnBoard(p) || board[p] == Stone.Empty
            || (Markers?.Any(m => m.Point == p) ?? false))
        {
            // No last-move ring where SGF markup already marks the point (as in Sabaki).
            return;
        }

        IBrush brush = style.LastMove.A > 0
            ? new ImmutableSolidColorBrush(style.LastMove)
            : board[p] == Stone.Black ? Brushes.White : Brushes.Black;

        // With its number on the stone, the ring goes around the number, near the stone's edge.
        bool numbered = MoveNumbers?.ContainsKey(p) ?? false;
        double radius = g.Cell * (numbered ? 0.39 : 0.2);
        context.DrawEllipse(null, new Pen(brush, Math.Max(1, g.Cell * (numbered ? 0.045 : 0.06))), StoneCenter(g, p), radius, radius);
    }

    /// <summary>Numbers on the stones, in the colour that reads best on each stone.</summary>
    private void DrawMoveNumbers(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (MoveNumbers is not { Count: > 0 } numbers)
        {
            return;
        }

        foreach ((Point p, int n) in numbers)
        {
            if (!board.IsOnBoard(p) || board[p] == Stone.Empty || (Markers?.Any(m => m.Point == p) ?? false))
            {
                continue;
            }

            string text = n.ToString(CultureInfo.InvariantCulture);
            IBrush brush = board[p] == Stone.Black ? Brushes.White : Brushes.Black;
            double size = g.Cell * (text.Length switch { 1 or 2 => 0.46, 3 => 0.36, _ => 0.3 });
            AvPoint c = StoneCenter(g, p);
            DrawCentredText(context, text, Math.Max(6, size), LabelTypeface, brush, c.X, c.Y);
        }
    }

    private void DrawMarkers(DrawingContext context, BoardGeometry g, BoardState board, IBrush LineBrush, BoardStyle style)
    {
        IBrush labelBackground = new ImmutableSolidColorBrush(style.Wood);
        if (Markers is not { Count: > 0 } markers)
        {
            return;
        }

        foreach (Markup m in markers)
        {
            if (!board.IsOnBoard(m.Point))
            {
                continue;
            }

            Stone under = board[m.Point];
            IBrush brush = under switch
            {
                Stone.Black => Brushes.White,
                Stone.White => Brushes.Black,
                _ => LineBrush,
            };
            var pen = new Pen(brush, Math.Max(1, g.Cell * 0.06), lineJoin: PenLineJoin.Round);
            AvPoint c = board[m.Point] != Stone.Empty ? StoneCenter(g, m.Point) : g.Center(m.Point);
            double s = g.Cell;

            switch (m.Kind)
            {
                case MarkupKind.Triangle:
                    {
                        double rr = s * 0.3;
                        var tri = new StreamGeometry();
                        using (StreamGeometryContext ctx = tri.Open())
                        {
                            ctx.BeginFigure(new AvPoint(c.X, c.Y - rr), false);
                            ctx.LineTo(new AvPoint(c.X + (rr * 0.866), c.Y + (rr * 0.5)));
                            ctx.LineTo(new AvPoint(c.X - (rr * 0.866), c.Y + (rr * 0.5)));
                            ctx.EndFigure(true);
                        }

                        context.DrawGeometry(null, pen, tri);
                        break;
                    }

                case MarkupKind.Square:
                    {
                        double h = s * 0.21;
                        context.DrawRectangle(null, pen, new Rect(c.X - h, c.Y - h, 2 * h, 2 * h));
                        break;
                    }

                case MarkupKind.Circle:
                    context.DrawEllipse(null, pen, c, s * 0.25, s * 0.25);
                    break;

                case MarkupKind.Cross:
                    {
                        double h = s * 0.2;
                        context.DrawLine(pen, new AvPoint(c.X - h, c.Y - h), new AvPoint(c.X + h, c.Y + h));
                        context.DrawLine(pen, new AvPoint(c.X - h, c.Y + h), new AvPoint(c.X + h, c.Y - h));
                        break;
                    }

                case MarkupKind.Label when !string.IsNullOrEmpty(m.Text):
                    if (under == Stone.Empty)
                    {
                        context.DrawEllipse(labelBackground, null, c, s * 0.4, s * 0.4);
                    }

                    DrawCentredText(context, m.Text, Math.Max(7, s * (m.Text.Length > 2 ? 0.36 : 0.48)), LabelTypeface, brush, c.X, c.Y);
                    break;
            }
        }
    }

    private void DrawGhost(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (!IsInteractive || HoverPoint is not { } p || GhostStone == Stone.Empty || !board.IsOnBoard(p) || board[p] != Stone.Empty)
        {
            return;
        }

        using (context.PushOpacity(0.4))
        {
            BoardStyle style = BoardStyle ?? DefaultStyle;
            DrawStone(context, g.Center(p), g.Cell * StoneRadius, GhostStone, style, p);
        }
    }

    private static void DrawCentredText(DrawingContext context, string text, double size, Typeface typeface, IBrush brush, double cx, double cy)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(ft, new AvPoint(cx - (ft.Width / 2), cy - (ft.Height / 2)));
    }
}
