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
using Hoshi.Core;
using Hoshi.Sgf;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

public sealed class BoardPointEventArgs(Point point, MouseButton button) : EventArgs
{
    public Point Point { get; } = point;

    public MouseButton Button { get; } = button;
}

/// <summary>
/// Draws a Go board and reports pointer interaction. It holds no game logic: the position, the ghost stone and the
/// markers are all supplied through bindings, and clicks are forwarded through <see cref="PointClicked"/> /
/// <see cref="PointClickedCommand"/>.
/// </summary>
public sealed class GoBoardControl : Control
{
    public static readonly StyledProperty<BoardState?> BoardProperty =
        AvaloniaProperty.Register<GoBoardControl, BoardState?>(nameof(Board));

    public static readonly StyledProperty<Point?> LastMoveProperty =
        AvaloniaProperty.Register<GoBoardControl, Point?>(nameof(LastMove));

    public static readonly StyledProperty<IReadOnlyList<Markup>?> MarkersProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<Markup>?>(nameof(Markers));

    public static readonly StyledProperty<bool> ShowCoordinatesProperty =
        AvaloniaProperty.Register<GoBoardControl, bool>(nameof(ShowCoordinates), defaultValue: true);

    public static readonly StyledProperty<bool> IsInteractiveProperty =
        AvaloniaProperty.Register<GoBoardControl, bool>(nameof(IsInteractive), defaultValue: true);

    /// <summary>Intersection under the pointer (written by the control, typically bound two-way).</summary>
    public static readonly StyledProperty<Point?> HoverPointProperty =
        AvaloniaProperty.Register<GoBoardControl, Point?>(nameof(HoverPoint), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Colour of the translucent stone drawn at <see cref="HoverPoint"/>; Empty hides it (e.g. illegal move).</summary>
    public static readonly StyledProperty<Stone> GhostStoneProperty =
        AvaloniaProperty.Register<GoBoardControl, Stone>(nameof(GhostStone));

    public static readonly StyledProperty<ICommand?> PointClickedCommandProperty =
        AvaloniaProperty.Register<GoBoardControl, ICommand?>(nameof(PointClickedCommand));

    /// <summary>Executed on mouse-wheel over the board with -1 (towards the start) or +1 (forward).</summary>
    public static readonly StyledProperty<ICommand?> ScrollCommandProperty =
        AvaloniaProperty.Register<GoBoardControl, ICommand?>(nameof(ScrollCommand));

    private static readonly Typeface CoordinateTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    // Colours after Shudan's goban.css (MIT, see THIRD_PARTY_NOTICES.md).
    private static readonly IBrush LineBrush = new ImmutableSolidColorBrush(BoardTextures.BoardForeground);
    private static readonly IBrush CoordinateBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xD0, 0x5E, 0x2E, 0x0C));
    private static readonly IBrush LabelBackground = new ImmutableSolidColorBrush(BoardTextures.BoardBackground);
    private static readonly IBrush BoardBackgroundBrush = new ImmutableSolidColorBrush(BoardTextures.BoardBackground);

    /// <summary>Shudan's board sheen: a light wash at the top and a faint darkening towards the bottom.</summary>
    private static readonly IBrush BoardSheen = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x1A, 234, 220, 192), 0),
            new GradientStop(Color.FromArgb(0x00, 128, 110, 90), 0.5),
            new GradientStop(Color.FromArgb(0x0D, 23, 10, 2), 1),
        },
    }.ToImmutable();

    /// <summary>Fraction of a cell covered by a stone (Shudan: vertex minus 0.08 em).</summary>
    public const double StoneRadius = 0.46;

    static GoBoardControl()
    {
        AffectsRender<GoBoardControl>(
            BoardProperty, LastMoveProperty, MarkersProperty, ShowCoordinatesProperty,
            HoverPointProperty, GhostStoneProperty, IsInteractiveProperty);
        FocusableProperty.OverrideDefaultValue<GoBoardControl>(true);
        // Not clipped: the board's drop shadow falls on the tatami around it.
        ClipToBoundsProperty.OverrideDefaultValue<GoBoardControl>(false);
    }

    public event EventHandler<BoardPointEventArgs>? PointClicked;

    public BoardState? Board
    {
        get => GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public Point? LastMove
    {
        get => GetValue(LastMoveProperty);
        set => SetValue(LastMoveProperty, value);
    }

    public IReadOnlyList<Markup>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public bool ShowCoordinates
    {
        get => GetValue(ShowCoordinatesProperty);
        set => SetValue(ShowCoordinatesProperty, value);
    }

    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    public Point? HoverPoint
    {
        get => GetValue(HoverPointProperty);
        set => SetValue(HoverPointProperty, value);
    }

    public Stone GhostStone
    {
        get => GetValue(GhostStoneProperty);
        set => SetValue(GhostStoneProperty, value);
    }

    public ICommand? PointClickedCommand
    {
        get => GetValue(PointClickedCommandProperty);
        set => SetValue(PointClickedCommandProperty, value);
    }

    public ICommand? ScrollCommand
    {
        get => GetValue(ScrollCommandProperty);
        set => SetValue(ScrollCommandProperty, value);
    }

    /// <summary>Geometry for the current bounds and board, or null when there is no board.</summary>
    public BoardGeometry? CurrentGeometry => Board is { } b
        ? BoardGeometry.Create(Bounds.Width, Bounds.Height, b.Width, b.Height, ShowCoordinates)
        : null;

    public override void Render(DrawingContext context)
    {
        if (Board is not { } board || CurrentGeometry is not { Cell: > 2 } g)
        {
            return;
        }

        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;

        DrawWood(context, g);
        DrawGrid(context, g, board, scale);
        if (ShowCoordinates)
        {
            DrawCoordinates(context, g);
        }

        DrawStones(context, g, board);
        DrawLastMove(context, g, board);
        DrawMarkers(context, g, board);
        DrawGhost(context, g, board);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LastMoveProperty || change.Property == BoardProperty)
        {
            string name = Board is { } b && LastMove is { } p
                ? string.Create(CultureInfo.InvariantCulture, $"Go board, last move {p.ToHuman(b.Height)}")
                : "Go board";
            AutomationProperties.SetName(this, name);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        HoverPoint = IsInteractive ? CurrentGeometry?.HitTest(e.GetPosition(this)) : null;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Delta.Y == 0 || ScrollCommand is not { } command)
        {
            return;
        }

        int direction = e.Delta.Y > 0 ? -1 : 1;
        if (command.CanExecute(direction))
        {
            command.Execute(direction);
        }

        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        HoverPoint = null;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!IsInteractive || CurrentGeometry?.HitTest(e.GetPosition(this)) is not { } point)
        {
            return;
        }

        Focus();
        MouseButton button = e.InitialPressMouseButton;
        PointClicked?.Invoke(this, new BoardPointEventArgs(point, button));
        if (button == MouseButton.Left && PointClickedCommand is { } command && command.CanExecute(point))
        {
            command.Execute(point);
        }

        e.Handled = true;
    }

    private static void DrawWood(DrawingContext context, BoardGeometry g)
    {
        Rect outer = g.BoardRect;
        double border = Math.Max(2, g.Cell * 0.15);
        Rect rect = outer.Deflate(border);

        // Sabaki: the goban floats over the tatami with a deep, soft shadow.
        context.DrawRectangle(
            new ImmutableSolidColorBrush(BoardTextures.BoardBorder), null, outer, 0, 0,
            new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 5, Blur = 20, Color = Color.FromArgb(0xCC, 20, 0, 15) }));

        using (context.PushClip(rect))
        {
            context.DrawRectangle(BoardBackgroundBrush, null, rect);
            Bitmap wood = BoardTextures.Wood;
            Size src = wood.Size;

            // Cover the board with the texture, keeping its aspect ratio (the grain runs vertically).
            double scale = Math.Max(rect.Width / src.Width, rect.Height / src.Height);
            var dest = new Rect(
                rect.X + ((rect.Width - (src.Width * scale)) / 2),
                rect.Y + ((rect.Height - (src.Height * scale)) / 2),
                src.Width * scale,
                src.Height * scale);
            context.DrawImage(wood, new Rect(src), dest);
        }

        context.DrawRectangle(BoardSheen, null, outer);
    }

    private static void DrawGrid(DrawingContext context, BoardGeometry g, BoardState board, double scale)
    {
        double thin = Math.Max(1, Math.Round(scale)) / scale;
        double thick = Math.Max(1, Math.Round(1.5 * scale)) / scale;
        var thinPen = new Pen(LineBrush, thin, lineCap: PenLineCap.Square);
        var thickPen = new Pen(LineBrush, thick, lineCap: PenLineCap.Square);

        double left = Snap(g.OriginX, thin, scale);
        double top = Snap(g.OriginY, thin, scale);
        double right = Snap(g.OriginX + ((board.Width - 1) * g.Cell), thin, scale);
        double bottom = Snap(g.OriginY + ((board.Height - 1) * g.Cell), thin, scale);

        for (int x = 0; x < board.Width; x++)
        {
            bool edge = x == 0 || x == board.Width - 1;
            double px = Snap(g.OriginX + (x * g.Cell), edge ? thick : thin, scale);
            context.DrawLine(edge ? thickPen : thinPen, new AvPoint(px, top), new AvPoint(px, bottom));
        }

        for (int y = 0; y < board.Height; y++)
        {
            bool edge = y == 0 || y == board.Height - 1;
            double py = Snap(g.OriginY + (y * g.Cell), edge ? thick : thin, scale);
            context.DrawLine(edge ? thickPen : thinPen, new AvPoint(left, py), new AvPoint(right, py));
        }

        double hoshi = Math.Max(1.5, g.Cell * 0.1);
        foreach (Point p in BoardGeometry.StarPoints(board.Width, board.Height))
        {
            context.DrawEllipse(LineBrush, null, g.Center(p), hoshi, hoshi);
        }
    }

    /// <summary>Aligns a line centre to the device pixel grid so 1-pixel lines stay crisp at any scale.</summary>
    private static double Snap(double value, double thickness, double scale)
    {
        double devicePixels = Math.Round(thickness * scale);
        double offset = devicePixels % 2 == 1 ? 0.5 : 0;
        return (Math.Floor(value * scale) + offset) / scale;
    }

    private static void DrawCoordinates(DrawingContext context, BoardGeometry g)
    {
        double size = Math.Clamp(g.Cell * 0.34, 7, 18);
        double gap = g.Cell * 0.9;
        for (int x = 0; x < g.Columns && x < Point.HumanColumns.Length; x++)
        {
            string label = Point.HumanColumns[x].ToString();
            AvPoint top = g.Center(new Point(x, 0));
            AvPoint bottom = g.Center(new Point(x, g.Rows - 1));
            DrawCentredText(context, label, size, CoordinateTypeface, CoordinateBrush, top.X, top.Y - gap);
            DrawCentredText(context, label, size, CoordinateTypeface, CoordinateBrush, bottom.X, bottom.Y + gap);
        }

        for (int y = 0; y < g.Rows; y++)
        {
            string label = (g.Rows - y).ToString(CultureInfo.InvariantCulture);
            AvPoint left = g.Center(new Point(0, y));
            AvPoint right = g.Center(new Point(g.Columns - 1, y));
            DrawCentredText(context, label, size, CoordinateTypeface, CoordinateBrush, left.X - gap, left.Y);
            DrawCentredText(context, label, size, CoordinateTypeface, CoordinateBrush, right.X + gap, right.Y);
        }
    }

    private static void DrawStones(DrawingContext context, BoardGeometry g, BoardState board)
    {
        double r = g.Cell * StoneRadius;
        var shadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0,
            OffsetY = g.Cell * 0.1,
            Blur = g.Cell * 0.2,
            Color = BoardTextures.ShadowColor,
        });
        var shadowBrush = new ImmutableSolidColorBrush(BoardTextures.ShadowColor);

        // Shadows first so no stone's shadow falls on a neighbouring stone.
        foreach (Point p in board.AllPoints)
        {
            if (board[p] != Stone.Empty)
            {
                AvPoint c = StoneCenter(g, p);
                context.DrawRectangle(shadowBrush, null, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r), r, r, shadow);
            }
        }

        foreach (Point p in board.AllPoints)
        {
            Stone s = board[p];
            if (s != Stone.Empty)
            {
                DrawStone(context, StoneCenter(g, p), r, s);
            }
        }
    }

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

    /// <summary>Shudan's stone in vector form: 20.5/21.5 body with a 1-unit rim, plus an 18.5/21.5 highlight.</summary>
    private static void DrawStone(DrawingContext context, AvPoint c, double r, Stone color)
    {
        double body = r * 20.5 / 21.5;
        double inner = r * 18.5 / 21.5;
        double rim = Math.Max(0.5, r / 21.5);
        bool black = color == Stone.Black;

        context.DrawEllipse(black ? BoardTextures.BlackStone : BoardTextures.WhiteStone, null, c, body, body);
        context.DrawEllipse(black ? BoardTextures.BlackHighlight : BoardTextures.WhiteHighlight, null, c, inner, inner);
        IPen edge = black ? BoardTextures.BlackEdge : BoardTextures.WhiteEdge;
        context.DrawEllipse(null, new Pen(edge.Brush, rim), c, body, body);
    }

    private void DrawLastMove(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (LastMove is not { } p || !board.IsOnBoard(p) || board[p] == Stone.Empty
            || (Markers?.Any(m => m.Point == p) ?? false))
        {
            // No last-move ring where SGF markup already marks the point (as in Sabaki).
            return;
        }

        IBrush brush = board[p] == Stone.Black ? Brushes.White : Brushes.Black;
        double radius = g.Cell * 0.2;
        context.DrawEllipse(null, new Pen(brush, Math.Max(1, g.Cell * 0.06)), StoneCenter(g, p), radius, radius);
    }

    private void DrawMarkers(DrawingContext context, BoardGeometry g, BoardState board)
    {
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
                        context.DrawEllipse(LabelBackground, null, c, s * 0.4, s * 0.4);
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
            DrawStone(context, g.Center(p), g.Cell * StoneRadius, GhostStone);
        }
    }

    private static void DrawCentredText(DrawingContext context, string text, double size, Typeface typeface, IBrush brush, double cx, double cy)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(ft, new AvPoint(cx - (ft.Width / 2), cy - (ft.Height / 2)));
    }
}
