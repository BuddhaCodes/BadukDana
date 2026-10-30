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
public sealed partial class GoBoardControl : Control
{
    public static readonly StyledProperty<BoardState?> BoardProperty =
        AvaloniaProperty.Register<GoBoardControl, BoardState?>(nameof(Board));

    public static readonly StyledProperty<Point?> LastMoveProperty =
        AvaloniaProperty.Register<GoBoardControl, Point?>(nameof(LastMove));

    public static readonly StyledProperty<IReadOnlyList<Markup>?> MarkersProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<Markup>?>(nameof(Markers));

    /// <summary>Move numbers to draw on stones (see <c>GameViewModel.MoveNumberLabels</c>); null or empty for none.</summary>
    public static readonly StyledProperty<IReadOnlyDictionary<Point, int>?> MoveNumbersProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyDictionary<Point, int>?>(nameof(MoveNumbers));

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

    /// <summary>The theme's board look (bind to <c>{DynamicResource Theme.Board}</c>); null = the default kaya.</summary>
    public static readonly StyledProperty<BoardStyle?> BoardStyleProperty =
        AvaloniaProperty.Register<GoBoardControl, BoardStyle?>(nameof(BoardStyle));

    /// <summary>Animate stone placement (bind to <c>{DynamicResource Theme.Animations}</c>).</summary>
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<GoBoardControl, bool>(nameof(Animate));

    /// <summary>Territory overlay (current and potential ownership); null hides it.</summary>
    public static readonly StyledProperty<TerritoryEstimate?> TerritoryProperty =
        AvaloniaProperty.Register<GoBoardControl, TerritoryEstimate?>(nameof(Territory));

    /// <summary>How strong the board effects are (impacts, captures, weak-group halos); see <see cref="Services.EffectsLevel"/>.</summary>
    public static readonly StyledProperty<Services.EffectsLevel> EffectsProperty =
        AvaloniaProperty.Register<GoBoardControl, Services.EffectsLevel>(nameof(Effects), Services.EffectsLevel.Full);

    /// <summary>Groups that are not safe: drawn with a quiet halo under their stones.</summary>
    public static readonly StyledProperty<IReadOnlyList<GroupStatus>?> GroupStatusesProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<GroupStatus>?>(nameof(GroupStatuses));

    /// <summary>A strong move to celebrate (flash, shockwave, embers, shake, cracks); a new value starts the effect.</summary>
    public static readonly StyledProperty<ViewModels.BoardImpact?> ImpactProperty =
        AvaloniaProperty.Register<GoBoardControl, ViewModels.BoardImpact?>(nameof(Impact));

    /// <summary>Known joseki continuations: small discs coloured by how the explorer rates them.</summary>
    public static readonly StyledProperty<IReadOnlyList<ViewModels.BoardJosekiHint>?> JosekiHintsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<ViewModels.BoardJosekiHint>?>(nameof(JosekiHints));

    /// <summary>Engine suggestions drawn as labelled discs on empty points.</summary>
    public static readonly StyledProperty<IReadOnlyList<ViewModels.BoardSuggestion>?> SuggestionsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<ViewModels.BoardSuggestion>?>(nameof(Suggestions));

    /// <summary>Duration of the stone settling and of the theme's ring effect.</summary>
    public static readonly TimeSpan SettleDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan EffectDuration = TimeSpan.FromMilliseconds(650);

    private static readonly BoardStyle DefaultStyle = new();

    private readonly System.Diagnostics.Stopwatch _animClock = new();
    private Avalonia.Threading.DispatcherTimer? _animTimer;
    private Point? _animPoint;
    private BoardState? _before;
    private readonly System.Diagnostics.Stopwatch _impactClock = new();
    private Avalonia.Threading.DispatcherTimer? _impactTimer;
    private ImpactEffect? _impact;
    private readonly System.Diagnostics.Stopwatch _captureClock = new();
    private Avalonia.Threading.DispatcherTimer? _captureTimer;
    private CaptureEffect? _capture;
    private readonly System.Diagnostics.Stopwatch _groupClock = new();
    private Avalonia.Threading.DispatcherTimer? _groupTimer;

    private static readonly Typeface CoordinateTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.Medium);
    private static readonly Typeface LabelTypeface = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly IBrush LineBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x5E, 0x2E, 0x0C));

    /// <summary>The board's sheen: a light wash at the top and a faint darkening towards the bottom.</summary>
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
            BoardProperty, LastMoveProperty, MarkersProperty, MoveNumbersProperty, ShowCoordinatesProperty,
            HoverPointProperty, GhostStoneProperty, PendingPointProperty, PendingStoneProperty, IsInteractiveProperty, StudyDrawingsProperty, PendingDrawingIdsProperty, DragPreviewProperty, BoardStyleProperty, TerritoryProperty, SuggestionsProperty, JosekiHintsProperty);
        FocusableProperty.OverrideDefaultValue<GoBoardControl>(true);
        // Not clipped: the board's drop shadow falls on the tatami around it.
        ClipToBoundsProperty.OverrideDefaultValue<GoBoardControl>(false);
    }

    public GoBoardControl()
    {
        // Stone sprites and wood are drawn well below their size: smooth (mipmapped) scaling keeps them crisp.
        RenderOptions.SetBitmapInterpolationMode(this, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
    }

    public event EventHandler<BoardPointEventArgs>? PointClicked;

    public Services.EffectsLevel Effects
    {
        get => GetValue(EffectsProperty);
        set => SetValue(EffectsProperty, value);
    }

    public IReadOnlyList<GroupStatus>? GroupStatuses
    {
        get => GetValue(GroupStatusesProperty);
        set => SetValue(GroupStatusesProperty, value);
    }

    /// <summary>Freezes the atari animation at this many seconds (tests).</summary>
    internal double? GroupPulseTime { get; set; }

    public ViewModels.BoardImpact? Impact
    {
        get => GetValue(ImpactProperty);
        set => SetValue(ImpactProperty, value);
    }

    /// <summary>The impact being animated, for tests.</summary>
    internal bool IsImpactRunning => _impact is not null;

    /// <summary>Freezes the impact at this many seconds (tests take screenshots of chosen frames).</summary>
    internal double? ImpactTime { get; set; }

    public BoardState? Board
    {
        get => GetValue(BoardProperty);
        set => SetValue(BoardProperty, value);
    }

    public BoardStyle? BoardStyle
    {
        get => GetValue(BoardStyleProperty);
        set => SetValue(BoardStyleProperty, value);
    }

    public TerritoryEstimate? Territory
    {
        get => GetValue(TerritoryProperty);
        set => SetValue(TerritoryProperty, value);
    }

    public IReadOnlyList<ViewModels.BoardJosekiHint>? JosekiHints
    {
        get => GetValue(JosekiHintsProperty);
        set => SetValue(JosekiHintsProperty, value);
    }

    public IReadOnlyList<ViewModels.BoardSuggestion>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    public bool Animate
    {
        get => GetValue(AnimateProperty);
        set => SetValue(AnimateProperty, value);
    }

    /// <summary>Point whose placement is being animated (for tests).</summary>
    public Point? AnimatingPoint => _animPoint;

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

    public IReadOnlyDictionary<Point, int>? MoveNumbers
    {
        get => GetValue(MoveNumbersProperty);
        set => SetValue(MoveNumbersProperty, value);
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
        BoardStyle style = BoardStyle ?? DefaultStyle;
        var lines = new ImmutableSolidColorBrush(style.Lines);

        double t = ImpactTime ?? _impactClock.Elapsed.TotalSeconds;
        ImpactEffect? impact = _impact is { } fx && board.IsOnBoard(fx.Point) ? fx : null;
        AvPoint shake = impact?.Shake(t, g.Cell) ?? default;
        using DrawingContext.PushedState shaken = context.PushTransform(Matrix.CreateTranslation(shake.X, shake.Y));

        DrawWood(context, g, style);
        DrawGrid(context, g, board, scale, lines);
        if (ShowCoordinates)
        {
            DrawCoordinates(context, g, new ImmutableSolidColorBrush(style.Coordinates));
        }

        if (impact is not null)
        {
            using (context.PushClip(g.BoardRect))
            {
                impact.DrawCracks(context, StoneCenter(g, impact.Point), g.Cell, t);
            }
        }

        DrawInfluence(context, g, board);
        DrawGroupHalos(context, g, board);
        DrawStudyAreas(context, g, board);
        DrawStones(context, g, board, style);
        if (_capture is { } capture)
        {
            capture.Draw(
                context,
                CaptureTime ?? _captureClock.Elapsed.TotalSeconds,
                g.Cell,
                q => StoneCenter(g, q),
                (ctx, c, r, color, q) => DrawStone(ctx, c, r, color, style, q),
                StoneRadius);
        }
        DrawTerritoryMarks(context, g, board);
        DrawEffect(context, g, board, style);
        impact?.DrawBurst(context, StoneCenter(g, impact.Point), g.Cell, t);
        DrawSuggestions(context, g, board);
        DrawJosekiHints(context, g, board);
        DrawLastMove(context, g, board, style);
        DrawMoveNumbers(context, g, board);
        DrawMarkers(context, g, board, lines, style);
        DrawStudy(context, g, board, style);
        DrawPending(context, g, board, style);
        DrawGhost(context, g, board);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoardProperty)
        {
            _before = change.OldValue as BoardState;
        }

        if (change.Property == BoardProperty || change.Property == LastMoveProperty)
        {
            TryStartPlacementAnimation();
        }

        if (change.Property == GroupStatusesProperty || change.Property == AnimateProperty || change.Property == EffectsProperty)
        {
            UpdateGroupTimer();
        }

        if (change.Property == EffectsProperty)
        {
            // Turned down mid-effect: stop what is running at once.
            Services.EffectsLevel level = Effects;
            if (level == Services.EffectsLevel.Off || (level == Services.EffectsLevel.Subtle && _impact is { Strength: > 1 }))
            {
                _impact = null;
                _impactTimer?.Stop();
            }

            if (level == Services.EffectsLevel.Off)
            {
                _capture = null;
                _captureTimer?.Stop();
            }

            InvalidateVisual();
        }

        if (change.Property == ImpactProperty && change.NewValue is ViewModels.BoardImpact impact)
        {
            StartImpact(impact);
        }

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

        // The see-through "about to play here" stone follows a mouse only: a finger has no hover, and a ghost left
        // where it last touched stayed on the board like a stone about to be placed (bug, 2026-10-18).
        HoverPoint = IsInteractive && e.Pointer.Type == PointerType.Mouse ? CurrentGeometry?.HitTest(e.GetPosition(this)) : null;
        TrackDrag(e);
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

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (e.Pointer.Type != PointerType.Mouse)
        {
            HoverPoint = null;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Type != PointerType.Mouse)
        {
            HoverPoint = null; // a lifted finger leaves no ghost behind
        }

        // A drag ends on the nearest intersection, however fast it was and wherever the pointer was let go (a
        // quick flick often ends between two intersections or just past the edge).
        if (IsInteractive && _dragFrom is not null && e.InitialPressMouseButton == MouseButton.Left
            && (CurrentGeometry?.Nearest(e.GetPosition(this)) ?? _dragTo) is { } dragEnd && FinishDrag(dragEnd))
        {
            Focus();
            e.Handled = true;
            return;
        }

        // Arrows and areas by two taps take the nearest intersection too; a stone needs a tap near its point.
        Point? tapped = DragPreview != DragPreview.None ? CurrentGeometry?.Nearest(e.GetPosition(this)) : CurrentGeometry?.HitTest(e.GetPosition(this));
        if (!IsInteractive || tapped is not { } point)
        {
            CancelDrag(); // let go off the board: no shape
            return;
        }

        Focus();
        MouseButton button = e.InitialPressMouseButton;
        if (button == MouseButton.Left && FinishDrag(point))
        {
            e.Handled = true;
            return;
        }

        PointClicked?.Invoke(this, new BoardPointEventArgs(point, button));
        if (button == MouseButton.Left && PointClickedCommand is { } command && command.CanExecute(point))
        {
            command.Execute(point);
        }

        e.Handled = true;
    }

    private static void DrawWood(DrawingContext context, BoardGeometry g, BoardStyle style)
    {
        Rect outer = g.BoardRect;
        double border = style.BorderWidth * g.Cell;
        Rect rect = outer.Deflate(border);
        Color frame = border > 0 ? style.Border : style.Wood;

        // The goban floats over the background with a deep, soft shadow.
        context.DrawRectangle(
            new ImmutableSolidColorBrush(frame), null, outer, 0, 0,
            new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 5, Blur = style.BoardShadowBlur, Color = style.BoardShadow }));

        using (context.PushClip(rect))
        {
            context.DrawRectangle(new ImmutableSolidColorBrush(style.Wood), null, rect);
            Bitmap wood = Themes.Skins.Image(style.Texture) ?? BoardTextures.KayaFor(style.Wood);
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

    private static void DrawGrid(DrawingContext context, BoardGeometry g, BoardState board, double scale, IBrush LineBrush)
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

    private static void DrawCoordinates(DrawingContext context, BoardGeometry g, IBrush CoordinateBrush)
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

    private void DrawStones(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style)
    {
        double r = g.Cell * StoneRadius;
        Color shadowColor = style.StoneShadow;
        var shadow = new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = g.Cell * 0.1, Blur = g.Cell * 0.2, Color = shadowColor });
        var shadowBrush = new ImmutableSolidColorBrush(shadowColor);

        // A newly placed stone settles: it starts a little large and lifted, then lands.
        double settle = SettleProgress();
        double lift = 1 + (0.14 * (1 - settle) * (1 - settle));

        // Shadows first so no stone's shadow falls on a neighbouring stone.
        foreach (Point p in board.AllPoints)
        {
            if (board[p] != Stone.Empty)
            {
                AvPoint c = StoneCenter(g, p);
                double k = p == _animPoint ? lift : 1;
                var s = new BoxShadows(new BoxShadow
                {
                    OffsetX = 0,
                    OffsetY = g.Cell * 0.1 * k * k,
                    Blur = g.Cell * 0.2 * k * k,
                    Color = shadowColor,
                });
                context.DrawRectangle(shadowBrush, null, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r), r, r, p == _animPoint ? s : shadow);
            }
        }

        foreach (Point p in board.AllPoints)
        {
            Stone s = board[p];
            if (s != Stone.Empty)
            {
                DrawStone(context, StoneCenter(g, p), r * (p == _animPoint ? lift : 1), s, style, p);
            }
        }
    }

    /// <summary>
    /// Potential territory: small squares of the likely owner's colour whose size and opacity grow with the
    /// estimate's confidence (a "score estimator" look that stays readable on any wood).
    /// </summary>
    private void DrawInfluence(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (Territory is not { } t || t.Width != board.Width || t.Height != board.Height)
        {
            return;
        }

        var whiteEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), Math.Max(0.7, g.Cell * 0.025));
        var blackEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)), Math.Max(0.5, g.Cell * 0.015));
        foreach (Point p in board.AllPoints)
        {
            double o = t.OwnershipAt(p);
            double k = Math.Min(1, Math.Abs(o) / TerritoryEstimate.SecureThreshold);
            if (board[p] != Stone.Empty || k < 0.2 || t.SecureOwner(p) != Stone.Empty)
            {
                continue;
            }

            // Bigger and firmer than before (2026-10-18), with a fine edge on both colours, so the squares stand
            // out on light wood and on a small phone board.
            double h = g.Cell * (0.08 + (0.14 * k));
            AvPoint c = g.Center(p);
            var rect = new Rect(c.X - h, c.Y - h, 2 * h, 2 * h);
            if (o > 0)
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(130 + (110 * k)), 0x14, 0x14, 0x18)), blackEdge, rect);
            }
            else
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(160 + (90 * k)), 0xFA, 0xFA, 0xF8)), whiteEdge, rect);
            }
        }
    }

    /// <summary>Secure territory (and dead stones): a small square of the owner's colour, as in scoring.</summary>
    private void DrawTerritoryMarks(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (Territory is not { } t || t.Width != board.Width || t.Height != board.Height)
        {
            return;
        }

        double h = g.Cell * 0.26;
        var blackFill = new ImmutableSolidColorBrush(Color.FromArgb(0xF5, 0x12, 0x12, 0x14));
        var whiteFill = new ImmutableSolidColorBrush(Color.FromArgb(0xFF, 0xFC, 0xFC, 0xFA));
        var whiteEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)), Math.Max(0.9, g.Cell * 0.035));
        var blackEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF)), Math.Max(0.6, g.Cell * 0.02));
        foreach (Point p in board.AllPoints)
        {
            Stone owner = t.SecureOwner(p);
            if (owner == Stone.Empty)
            {
                continue;
            }

            AvPoint c = board[p] != Stone.Empty ? StoneCenter(g, p) : g.Center(p);
            var rect = new Rect(c.X - h, c.Y - h, 2 * h, 2 * h);
            if (owner == Stone.Black)
            {
                context.DrawRectangle(blackFill, blackEdge, rect);
            }
            else
            {
                context.DrawRectangle(whiteFill, whiteEdge, rect);
            }
        }
    }

    /// <summary>Engine suggestions: blue discs with the winrate for the player to move and the score change.</summary>
    private void DrawSuggestions(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (Suggestions is not { Count: > 0 } list)
        {
            return;
        }

        foreach (ViewModels.BoardSuggestion s in list)
        {
            if (!board.IsOnBoard(s.Point) || board[s.Point] != Stone.Empty)
            {
                continue;
            }

            AvPoint c = g.Center(s.Point);
            double r = g.Cell * StoneRadius;
            Color fill = s.IsBest ? Color.FromArgb(0xE0, 0x2F, 0x8F, 0xE8) : Color.FromArgb((byte)(0x70 + (0x50 * s.Strength)), 0x5A, 0x9F, 0xD8);
            context.DrawEllipse(new ImmutableSolidColorBrush(fill), s.IsBest ? new Pen(Brushes.White, Math.Max(1, g.Cell * 0.05)) : null, c, r, r);
            double size = Math.Max(7, g.Cell * 0.34);
            DrawCentredText(context, s.Label, size, LabelTypeface, Brushes.White, c.X, c.Y - (g.Cell * 0.09));
            DrawCentredText(context, s.Detail, Math.Max(6, g.Cell * 0.22), CoordinateTypeface, Brushes.White, c.X, c.Y + (g.Cell * 0.2));
        }
    }

    /// <summary>Joseki continuations: a disc in the rating's colour (ideal green … mistake red) with a fine light ring.</summary>
    private void DrawJosekiHints(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (JosekiHints is not { Count: > 0 } list)
        {
            return;
        }

        foreach (ViewModels.BoardJosekiHint h in list)
        {
            if (!board.IsOnBoard(h.Point) || board[h.Point] != Stone.Empty)
            {
                continue;
            }

            AvPoint c = g.Center(h.Point);
            double r = g.Cell * (h.IsRecommended ? 0.27 : 0.2);
            context.DrawEllipse(new ImmutableSolidColorBrush(ViewModels.BoardJosekiHint.ColorOf(h.Category)),
                new Pen(new ImmutableSolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), Math.Max(1, g.Cell * 0.04)), c, r, r);
        }
    }

    /// <summary>The theme's ring effect around a newly placed stone (halo, ink or sand ripple).</summary>
    private void DrawEffect(DrawingContext context, BoardGeometry g, BoardState board, BoardStyle style)
    {
        if (_animPoint is not { } p || style.Effect is PlacementEffect.None or PlacementEffect.Settle || !board.IsOnBoard(p))
        {
            return;
        }

        double k = Math.Clamp(_animClock.Elapsed / EffectDuration, 0, 1);
        if (k >= 1)
        {
            return;
        }

        double ease = 1 - Math.Pow(1 - k, 3);
        double radius = g.Cell * StoneRadius * (1.05 + (1.5 * ease));
        byte alpha = (byte)(style.EffectColor.A * (1 - k) * (style.Effect == PlacementEffect.InkRipple ? 0.7 : 0.85));
        var color = Color.FromArgb(alpha, style.EffectColor.R, style.EffectColor.G, style.EffectColor.B);
        AvPoint c = StoneCenter(g, p);
        if (style.Effect == PlacementEffect.GoldHalo)
        {
            // A soft glow plus a fine ring.
            var glow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0.55),
                    new GradientStop(Color.FromArgb((byte)(alpha / 2), color.R, color.G, color.B), 0.8),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                },
            };
            context.DrawEllipse(glow, null, c, radius * 1.15, radius * 1.15);
        }

        double width = Math.Max(1, g.Cell * (style.Effect == PlacementEffect.InkRipple ? 0.09 : 0.05) * (1 - (0.6 * k)));
        context.DrawEllipse(null, new Pen(new ImmutableSolidColorBrush(color), width), c, radius, radius);
    }

    private double SettleProgress()
    {
        if (_animPoint is null)
        {
            return 1;
        }

        double k = Math.Clamp(_animClock.Elapsed / SettleDuration, 0, 1);
        return 1 - Math.Pow(1 - k, 3);
    }

    /// <summary>
    /// Animates only when exactly one stone appeared, at the last move (a move played, or one step forward) —
    /// not when jumping through the game or loading a position.
    /// </summary>
    private void TryStartPlacementAnimation()
    {
        if (!Animate || _before is not { } before || Board is not { } after || LastMove is not { } p
            || before.Width != after.Width || before.Height != after.Height || !after.IsOnBoard(p)
            || before[p] != Stone.Empty || after[p] == Stone.Empty)
        {
            return;
        }

        int added = 0;
        var captured = new List<(Point, Stone)>();
        foreach (Point q in after.AllPoints)
        {
            if (before[q] == Stone.Empty && after[q] != Stone.Empty && ++added > 1)
            {
                return;
            }

            if (before[q] != Stone.Empty && after[q] == Stone.Empty)
            {
                captured.Add((q, before[q]));
            }
        }

        _before = null;
        StartPlacementAnimation(p);
        if (captured.Count > 0)
        {
            StartCapture(p, captured);
        }
    }
}
