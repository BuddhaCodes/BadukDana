using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Sgf.Study;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

/// <summary>A press-drag-release on the board, from one point to another (study arrows and areas).</summary>
public sealed record BoardDrag(Point From, Point To);

/// <summary>What a drag on the board previews while the mouse button is down.</summary>
public enum DragPreview
{
    None,
    Arrow,
    Area,
}

/// <summary>Study drawings (arrows, areas, marks, labels, "what if" sequences) and press-drag input.</summary>
public sealed partial class GoBoardControl
{
    public static readonly StyledProperty<IReadOnlyList<StudyDrawing>?> StudyDrawingsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<StudyDrawing>?>(nameof(StudyDrawings));

    /// <summary>
    /// With a <see cref="DragPreview"/>, a left-button drag between two different points runs this instead of a click
    /// (without one, every press is a click, so a slightly sliding mouse still plays the move).
    /// </summary>
    public static readonly StyledProperty<ICommand?> PointDraggedCommandProperty =
        AvaloniaProperty.Register<GoBoardControl, ICommand?>(nameof(PointDraggedCommand));

    public static readonly StyledProperty<DragPreview> DragPreviewProperty =
        AvaloniaProperty.Register<GoBoardControl, DragPreview>(nameof(DragPreview));

    public static readonly StyledProperty<StudyColor> DragColorProperty =
        AvaloniaProperty.Register<GoBoardControl, StudyColor>(nameof(DragColor));

    private Point? _dragFrom;
    private Point? _dragTo;

    // A finger drawing: where it is (a bubble above it names the point it hides).
    private AvPoint? _fingerAt;

    /// <summary>Drawings drawn faded: not saved yet (tap and confirm).</summary>
    public static readonly StyledProperty<IReadOnlyCollection<string>?> PendingDrawingIdsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyCollection<string>?>(nameof(PendingDrawingIds));

    public IReadOnlyCollection<string>? PendingDrawingIds
    {
        get => GetValue(PendingDrawingIdsProperty);
        set => SetValue(PendingDrawingIdsProperty, value);
    }

    /// <summary>Pushes the faded look for a drawing that waits for confirmation.</summary>
    private DrawingContext.PushedState? Faded(DrawingContext context, StudyDrawing d) =>
        PendingDrawingIds?.Contains(d.Id) ?? false
            ? context.PushOpacity(0.7)
            : null;

    public IReadOnlyList<StudyDrawing>? StudyDrawings
    {
        get => GetValue(StudyDrawingsProperty);
        set => SetValue(StudyDrawingsProperty, value);
    }

    public ICommand? PointDraggedCommand
    {
        get => GetValue(PointDraggedCommandProperty);
        set => SetValue(PointDraggedCommandProperty, value);
    }

    public DragPreview DragPreview
    {
        get => GetValue(DragPreviewProperty);
        set => SetValue(DragPreviewProperty, value);
    }

    public StudyColor DragColor
    {
        get => GetValue(DragColorProperty);
        set => SetValue(DragColorProperty, value);
    }

    /// <summary>The colour of a study colour on the board (strong enough on light wood and on stones).</summary>
    public static Color StudyColorValue(StudyColor color) => color switch
    {
        StudyColor.Red => Color.FromRgb(214, 64, 52),
        StudyColor.Blue => Color.FromRgb(46, 110, 214),
        StudyColor.Green => Color.FromRgb(40, 150, 80),
        StudyColor.Purple => Color.FromRgb(140, 80, 200),
        _ => Color.FromRgb(214, 150, 30),
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragFrom = IsInteractive && PointDraggedCommand is not null && DragPreview != DragPreview.None && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            ? CurrentGeometry?.HitTest(e.GetPosition(this))
            : null;
        _dragTo = _dragFrom;
        _fingerAt = _dragFrom is not null && e.Pointer.Type == PointerType.Touch ? e.GetPosition(this) : null;
    }

    /// <summary>Called from <see cref="OnPointerMoved"/>: follows a drag for the preview.</summary>
    private void TrackDrag(PointerEventArgs e)
    {
        if (_dragFrom is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point? to = CurrentGeometry?.HitTest(e.GetPosition(this));
        if (_fingerAt is not null)
        {
            _fingerAt = e.GetPosition(this);
        }

        if (to is not null && to != _dragTo)
        {
            _dragTo = to;
            InvalidateVisual();
        }
    }

    /// <summary>Called from <see cref="OnPointerReleased"/>: true when the release ended a drag (no click then).</summary>
    private bool FinishDrag(Point released)
    {
        Point? from = _dragFrom;
        CancelDrag();
        if (from is not { } start || start == released || PointDraggedCommand is not { } command)
        {
            return false;
        }

        var drag = new BoardDrag(start, released);
        if (command.CanExecute(drag))
        {
            command.Execute(drag);
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>Forgets a drag in progress (also when the finger is lifted off the board).</summary>
    private void CancelDrag()
    {
        bool had = _dragFrom is not null;
        _dragFrom = null;
        _dragTo = null;
        _fingerAt = null;
        if (had)
        {
            InvalidateVisual();
        }
    }

    /// <summary>Areas go on the wood, under the stones, so the stones inside keep their colours.</summary>
    private void DrawStudyAreas(DrawingContext context, BoardGeometry g, BoardState board)
    {
        foreach (StudyDrawing d in (StudyDrawings ?? []).Where(d => d.Kind == DrawingKind.Area))
        {
            using DrawingContext.PushedState? faded = Faded(context, d);
            DrawArea(context, g, board, d.AreaPoints(), StudyColorValue(d.Color));
        }

        if (DragPreview == DragPreview.Area && _dragFrom is { } from && _dragTo is { } to && from != to && board.IsOnBoard(from) && board.IsOnBoard(to))
        {
            DrawArea(context, g, board, new StudyDrawing("preview", DrawingKind.Area, string.Empty, DragColor, [from, to]).AreaPoints(), StudyColorValue(DragColor));
        }
    }

    private void DrawStudy(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style)
    {
        IReadOnlyList<StudyDrawing> drawings = StudyDrawings ?? [];
        // Arrows, then sequences, then marks and labels on top (areas are under the stones).
        foreach (StudyDrawing d in drawings.Where(d => d.Kind == DrawingKind.Arrow && d.Points.Count >= 2))
        {
            using DrawingContext.PushedState? faded = Faded(context, d);
            DrawArrow(context, g, Centre(g, board, d.Points[0]), Centre(g, board, d.Points[1]), StudyColorValue(d.Color), 1);
        }

        foreach (StudyDrawing d in drawings.Where(d => d.Kind == DrawingKind.Sequence))
        {
            using DrawingContext.PushedState? faded = Faded(context, d);
            DrawSequence(context, g, board, style, d);
        }

        foreach (StudyDrawing d in drawings.Where(d => d.Kind == DrawingKind.Mark))
        {
            using DrawingContext.PushedState? faded = Faded(context, d);
            AvPoint c = Centre(g, board, d.Points[0]);
            Color col = StudyColorValue(d.Color);
            context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(200, 255, 255, 255)), Math.Max(4, g.Cell * 0.14)), c, g.Cell * 0.36, g.Cell * 0.36);
            context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(col), Math.Max(2.5, g.Cell * 0.08)), c, g.Cell * 0.36, g.Cell * 0.36);
        }

        foreach (StudyDrawing d in drawings.Where(d => d.Kind == DrawingKind.Label && !string.IsNullOrEmpty(d.Text)))
        {
            using DrawingContext.PushedState? faded = Faded(context, d);
            AvPoint c = Centre(g, board, d.Points[0]);
            Color col = StudyColorValue(d.Color);
            double r = g.Cell * 0.36;
            context.DrawEllipse(new ImmutableSolidColorBrush(col), new Pen(Brushes.White, Math.Max(1, g.Cell * 0.05)), c, r, r);
            DrawCentredText(context, d.Text!, Math.Max(7, g.Cell * (d.Text!.Length > 2 ? 0.3 : 0.42)), LabelTypeface, Brushes.White, c.X, c.Y);
        }

        // The drag in progress.
        if (_dragFrom is { } from && _dragTo is { } to && from != to && board.IsOnBoard(from) && board.IsOnBoard(to))
        {
            Color col = StudyColorValue(DragColor);
            if (DragPreview == DragPreview.Arrow)
            {
                DrawArrow(context, g, Centre(g, board, from), Centre(g, board, to), col, 0.6);
            }
        }

        DrawFingerBubble(context, g, board);
    }

    /// <summary>
    /// While a finger drags an arrow or an area, it hides the point it is on: a ring marks that point and a bubble
    /// above the finger names it (e.g. "Q16").
    /// </summary>
    private void DrawFingerBubble(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (_fingerAt is not { } finger || _dragTo is not { } to || !board.IsOnBoard(to))
        {
            return;
        }

        Color col = StudyColorValue(DragColor);
        AvPoint c = Centre(g, board, to);
        context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(Color.FromArgb(220, 255, 255, 255)), Math.Max(2, g.Cell * 0.12)), c, g.Cell * 0.62, g.Cell * 0.62);
        context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(col), Math.Max(1.5, g.Cell * 0.07)), c, g.Cell * 0.62, g.Cell * 0.62);

        string text = to.ToHuman(board.Height);
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, 18, Brushes.White);
        double w = ft.Width + 22, h = ft.Height + 10;
        double y = Math.Max(4, finger.Y - 70 - h);
        double x = Math.Clamp(finger.X - (w / 2), 4, Math.Max(4, Bounds.Width - w - 4));
        var bubble = new Rect(x, y, w, h);
        context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb(230, 20, 22, 30)), new Pen(new ImmutableSolidColorBrush(col), 2), bubble, h / 2, h / 2);
        context.DrawText(ft, new AvPoint(x + 11, y + 5));
    }

    private static AvPoint Centre(BoardGeometry g, BoardState board, Point p) =>
        board.IsOnBoard(p) && board[p] != Stone.Empty ? StoneCenter(g, p) : g.Center(p);

    private static void DrawArea(DrawingContext context, BoardGeometry g, BoardState board, IReadOnlyList<Point> points, Color col)
    {
        if (points.Count == 0)
        {
            return;
        }

        int x0 = points.Min(p => p.X), x1 = points.Max(p => p.X), y0 = points.Min(p => p.Y), y1 = points.Max(p => p.Y);
        AvPoint a = g.Center(new Point(x0, y0));
        AvPoint b = g.Center(new Point(x1, y1));
        double pad = g.Cell * 0.48;
        var rect = new Rect(a.X - pad, a.Y - pad, b.X - a.X + (2 * pad), b.Y - a.Y + (2 * pad));
        context.DrawRectangle(
            new ImmutableSolidColorBrush(Color.FromArgb(70, col.R, col.G, col.B)),
            new Pen(new ImmutableSolidColorBrush(Color.FromArgb(200, col.R, col.G, col.B)), Math.Max(1.2, g.Cell * 0.06), dashStyle: new DashStyle([2, 1.4], 0)),
            rect,
            g.Cell * 0.22,
            g.Cell * 0.22);
    }

    private static void DrawArrow(DrawingContext context, BoardGeometry g, AvPoint from, AvPoint to, Color col, double opacity)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1)
        {
            return;
        }

        double ux = dx / len, uy = dy / len;
        // At least a few pixels wide: on a phone a 19×19 cell is under 20 pixels.
        double width = Math.Max(3, g.Cell * 0.13);
        double head = Math.Max(10, g.Cell * 0.42);
        var start = new AvPoint(from.X + (ux * g.Cell * 0.18), from.Y + (uy * g.Cell * 0.18));
        var tip = new AvPoint(to.X - (ux * g.Cell * 0.12), to.Y - (uy * g.Cell * 0.12));
        var neck = new AvPoint(tip.X - (ux * head), tip.Y - (uy * head));
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(tip, true);
            ctx.LineTo(new AvPoint(neck.X - (uy * head * 0.55), neck.Y + (ux * head * 0.55)));
            ctx.LineTo(new AvPoint(neck.X + (uy * head * 0.55), neck.Y - (ux * head * 0.55)));
            ctx.EndFigure(true);
        }

        using (context.PushOpacity(opacity))
        {
            var halo = new Pen(new ImmutableSolidColorBrush(Color.FromArgb(170, 255, 255, 255)), width + (g.Cell * 0.08), lineCap: PenLineCap.Round);
            context.DrawLine(halo, start, neck);
            context.DrawGeometry(null, new Pen(halo.Brush, g.Cell * 0.08, lineJoin: PenLineJoin.Round), geometry);
            var brush = new ImmutableSolidColorBrush(col);
            context.DrawLine(new Pen(brush, width, lineCap: PenLineCap.Round), start, neck);
            context.DrawGeometry(brush, null, geometry);
        }
    }

    private static void DrawSequence(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style, StudyDrawing d)
    {
        Stone color = d.FirstColor;
        double r = g.Cell * StoneRadius;
        var used = new HashSet<Point>();
        for (int i = 0; i < d.Points.Count; i++)
        {
            Point p = d.Points[i];
            if (board.IsOnBoard(p) && board[p] == Stone.Empty && used.Add(p))
            {
                AvPoint c = g.Center(p);
                using (context.PushOpacity(0.78))
                {
                    DrawStone(context, c, r, color, style, p);
                }

                context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(StudyColorValue(d.Color)), Math.Max(1.2, g.Cell * 0.06)), c, r, r);
                IBrush text = color == Stone.Black ? Brushes.White : Brushes.Black;
                DrawCentredText(context, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Math.Max(7, g.Cell * 0.4), LabelTypeface, text, c.X, c.Y);
            }

            color = color == Stone.Black ? Stone.White : Stone.Black;
        }
    }
}
