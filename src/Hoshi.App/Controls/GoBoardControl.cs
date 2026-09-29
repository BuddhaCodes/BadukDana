using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.Core;
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

    public static readonly StyledProperty<IReadOnlyList<BoardMarker>?> MarkersProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<BoardMarker>?>(nameof(Markers));

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

    private static readonly Typeface CoordinateTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush LineBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xCC, 0x1A, 0x1A, 0x1A));
    private static readonly IBrush CoordinateBrush = new ImmutableSolidColorBrush(Color.FromArgb(0xA6, 0x2A, 0x1E, 0x0C));
    private static readonly IBrush LabelBackground = new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xB9, 0x68));
    private static readonly IBrush Vignette = new RadialGradientBrush
    {
        RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.FromArgb(0x00, 0x3A, 0x22, 0x00), 0.6),
            new GradientStop(Color.FromArgb(0x30, 0x3A, 0x22, 0x00), 1),
        },
    }.ToImmutable();

    static GoBoardControl()
    {
        AffectsRender<GoBoardControl>(
            BoardProperty, LastMoveProperty, MarkersProperty, ShowCoordinatesProperty,
            HoverPointProperty, GhostStoneProperty, IsInteractiveProperty);
        FocusableProperty.OverrideDefaultValue<GoBoardControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<GoBoardControl>(true);
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

    public IReadOnlyList<BoardMarker>? Markers
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
        Rect rect = g.BoardRect;
        context.DrawRectangle(
            Brushes.Transparent, null, rect, 2, 2,
            new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 2, Blur = 12, Color = Color.FromArgb(0x80, 0, 0, 0) }));

        using (context.PushClip(new RoundedRect(rect, 2)))
        {
            context.DrawImage(BoardTextures.Wood, new Rect(0, 0, BoardTextures.WoodSize, BoardTextures.WoodSize), rect);
            context.DrawRectangle(Vignette, null, rect);
        }
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
        double r = g.Cell * 0.48;
        var shadowOffset = new Vector(g.Cell * 0.04, g.Cell * 0.06);

        // Shadows first so no stone's shadow overlaps a neighbouring stone.
        foreach (Point p in board.AllPoints)
        {
            if (board[p] != Stone.Empty)
            {
                context.DrawEllipse(BoardTextures.Shadow, null, g.Center(p) + shadowOffset, r * 1.08, r * 1.08);
            }
        }

        foreach (Point p in board.AllPoints)
        {
            Stone s = board[p];
            if (s != Stone.Empty)
            {
                DrawStone(context, g.Center(p), r, s, p);
            }
        }
    }

    private static void DrawStone(DrawingContext context, AvPoint c, double r, Stone color, Point p)
    {
        if (color == Stone.Black)
        {
            context.DrawEllipse(BoardTextures.BlackStone, null, c, r, r);
            return;
        }

        var edge = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0x40, 0x3A, 0x30)), Math.Max(0.6, r * 0.04));
        context.DrawEllipse(BoardTextures.WhiteStone, null, c, r, r);

        int variant = ((p.X * 7) + (p.Y * 13) + (p.X * p.Y)) % 5;
        if (variant > 0)
        {
            using (context.PushGeometryClip(new EllipseGeometry(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r))))
            {
                var vein = new Pen(BoardTextures.ShellVein.Brush, Math.Max(0.6, r * 0.07));
                double tilt = (variant - 2.5) * 0.18;
                for (int i = -1; i <= 1; i++)
                {
                    double offset = (i * r * 0.42) + (variant * r * 0.05);
                    var geometry = new StreamGeometry();
                    using (StreamGeometryContext ctx = geometry.Open())
                    {
                        ctx.BeginFigure(new AvPoint(c.X - r, c.Y + offset + (tilt * r)), false);
                        ctx.QuadraticBezierTo(
                            new AvPoint(c.X, c.Y + offset - (r * 0.28)),
                            new AvPoint(c.X + r, c.Y + offset - (tilt * r)));
                        ctx.EndFigure(false);
                    }

                    context.DrawGeometry(null, vein, geometry);
                }
            }
        }

        context.DrawEllipse(null, edge, c, r, r);
    }

    private void DrawLastMove(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (LastMove is not { } p || !board.IsOnBoard(p) || board[p] == Stone.Empty)
        {
            return;
        }

        IBrush brush = board[p] == Stone.Black ? Brushes.White : Brushes.Black;
        double radius = g.Cell * 0.2;
        context.DrawEllipse(null, new Pen(brush, Math.Max(1, g.Cell * 0.06)), g.Center(p), radius, radius);
    }

    private void DrawMarkers(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (Markers is not { Count: > 0 } markers)
        {
            return;
        }

        foreach (BoardMarker m in markers)
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
            AvPoint c = g.Center(m.Point);
            double s = g.Cell;

            switch (m.Kind)
            {
                case BoardMarkerKind.Triangle:
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

                case BoardMarkerKind.Square:
                    {
                        double h = s * 0.21;
                        context.DrawRectangle(null, pen, new Rect(c.X - h, c.Y - h, 2 * h, 2 * h));
                        break;
                    }

                case BoardMarkerKind.Circle:
                    context.DrawEllipse(null, pen, c, s * 0.25, s * 0.25);
                    break;

                case BoardMarkerKind.Cross:
                    {
                        double h = s * 0.2;
                        context.DrawLine(pen, new AvPoint(c.X - h, c.Y - h), new AvPoint(c.X + h, c.Y + h));
                        context.DrawLine(pen, new AvPoint(c.X - h, c.Y + h), new AvPoint(c.X + h, c.Y - h));
                        break;
                    }

                case BoardMarkerKind.Label when !string.IsNullOrEmpty(m.Text):
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
            DrawStone(context, g.Center(p), g.Cell * 0.48, GhostStone, p);
        }
    }

    private static void DrawCentredText(DrawingContext context, string text, double size, Typeface typeface, IBrush brush, double cx, double cy)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(ft, new AvPoint(cx - (ft.Width / 2), cy - (ft.Height / 2)));
    }
}
