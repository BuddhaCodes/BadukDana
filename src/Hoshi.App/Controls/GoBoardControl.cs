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

    /// <summary>The theme's board look (bind to <c>{DynamicResource Theme.Board}</c>); null = Classic.</summary>
    public static readonly StyledProperty<BoardStyle?> BoardStyleProperty =
        AvaloniaProperty.Register<GoBoardControl, BoardStyle?>(nameof(BoardStyle));

    /// <summary>Animate stone placement (bind to <c>{DynamicResource Theme.Animations}</c>).</summary>
    public static readonly StyledProperty<bool> AnimateProperty =
        AvaloniaProperty.Register<GoBoardControl, bool>(nameof(Animate));

    /// <summary>Territory overlay (current and potential ownership); null hides it.</summary>
    public static readonly StyledProperty<TerritoryEstimate?> TerritoryProperty =
        AvaloniaProperty.Register<GoBoardControl, TerritoryEstimate?>(nameof(Territory));

    /// <summary>Engine suggestions drawn as labelled discs on empty points.</summary>
    /// <summary>Groups in atari: they tremble now and then and sweat a drop (a comic, low-key alert).</summary>
    /// <summary>How strong the board effects are (impacts, captures, atari); see <see cref="Services.EffectsLevel"/>.</summary>
    public static readonly StyledProperty<Services.EffectsLevel> EffectsProperty =
        AvaloniaProperty.Register<GoBoardControl, Services.EffectsLevel>(nameof(Effects), Services.EffectsLevel.Full);

    public static readonly StyledProperty<IReadOnlyList<AtariGroup>?> AtariGroupsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<AtariGroup>?>(nameof(AtariGroups));

    /// <summary>A strong move to celebrate (flash, shockwave, embers, shake, cracks); a new value starts the effect.</summary>
    public static readonly StyledProperty<ViewModels.BoardImpact?> ImpactProperty =
        AvaloniaProperty.Register<GoBoardControl, ViewModels.BoardImpact?>(nameof(Impact));

    /// <summary>Known joseki continuations: small discs coloured by how the explorer rates them.</summary>
    public static readonly StyledProperty<IReadOnlyList<ViewModels.BoardJosekiHint>?> JosekiHintsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<ViewModels.BoardJosekiHint>?>(nameof(JosekiHints));

    public static readonly StyledProperty<IReadOnlyList<ViewModels.BoardSuggestion>?> SuggestionsProperty =
        AvaloniaProperty.Register<GoBoardControl, IReadOnlyList<ViewModels.BoardSuggestion>?>(nameof(Suggestions));

    /// <summary>Duration of the stone settling and of the theme's ring effect.</summary>
    public static readonly TimeSpan SettleDuration = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan EffectDuration = TimeSpan.FromMilliseconds(650);

    private static readonly BoardStyle ClassicStyle = new() { ShudanTexture = true };

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
    private readonly System.Diagnostics.Stopwatch _atariClock = new();
    private Avalonia.Threading.DispatcherTimer? _atariTimer;
    private Dictionary<Point, AvPoint> _tremble = [];

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
            HoverPointProperty, GhostStoneProperty, IsInteractiveProperty, BoardStyleProperty, TerritoryProperty, SuggestionsProperty, JosekiHintsProperty);
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

    public IReadOnlyList<AtariGroup>? AtariGroups
    {
        get => GetValue(AtariGroupsProperty);
        set => SetValue(AtariGroupsProperty, value);
    }

    /// <summary>Freezes the atari animation at this many seconds (tests).</summary>
    internal double? AtariTime { get; set; }

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
        BoardStyle style = BoardStyle ?? ClassicStyle;
        var lines = new ImmutableSolidColorBrush(style.ShudanTexture ? BoardTextures.BoardForeground : style.Lines);

        double t = ImpactTime ?? _impactClock.Elapsed.TotalSeconds;
        ImpactEffect? impact = _impact is { } fx && board.IsOnBoard(fx.Point) ? fx : null;
        AvPoint shake = impact?.Shake(t, g.Cell) ?? default;
        using DrawingContext.PushedState shaken = context.PushTransform(Matrix.CreateTranslation(shake.X, shake.Y));

        DrawWood(context, g, style);
        DrawGrid(context, g, board, scale, lines);
        if (ShowCoordinates)
        {
            DrawCoordinates(context, g, style.ShudanTexture ? CoordinateBrush : new ImmutableSolidColorBrush(style.Coordinates));
        }

        if (impact is not null)
        {
            using (context.PushClip(g.BoardRect))
            {
                impact.DrawCracks(context, StoneCenter(g, impact.Point), g.Cell, t);
            }
        }

        DrawInfluence(context, g, board);
        _tremble = AtariOffsets(g, board);
        DrawStones(context, g, board, style);
        DrawSweat(context, g, board);
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
        DrawMarkers(context, g, board, lines, style);
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

        if (change.Property == AtariGroupsProperty || change.Property == AnimateProperty || change.Property == EffectsProperty)
        {
            UpdateAtariTimer();
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

    private static void DrawWood(DrawingContext context, BoardGeometry g, BoardStyle style)
    {
        Rect outer = g.BoardRect;
        double border = style.ShudanTexture ? Math.Max(2, g.Cell * 0.15) : style.BorderWidth * g.Cell;
        Rect rect = outer.Deflate(border);
        Color frame = style.ShudanTexture ? BoardTextures.BoardBorder : (border > 0 ? style.Border : style.Wood);

        // The goban floats over the background with a deep, soft shadow.
        context.DrawRectangle(
            new ImmutableSolidColorBrush(frame), null, outer, 0, 0,
            new BoxShadows(new BoxShadow { OffsetX = 0, OffsetY = 5, Blur = style.BoardShadowBlur, Color = style.BoardShadow }));

        using (context.PushClip(rect))
        {
            context.DrawRectangle(style.ShudanTexture ? BoardBackgroundBrush : new ImmutableSolidColorBrush(style.Wood), null, rect);
            Bitmap wood = style.ShudanTexture ? BoardTextures.Wood
                : Themes.Skins.Image(style.Texture) ?? BoardTextures.KayaFor(style.Wood);
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
        Color shadowColor = style.ShudanTexture ? BoardTextures.ShadowColor : style.StoneShadow;
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
                AvPoint c = Trembled(g, p);
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
                DrawStone(context, Trembled(g, p), r * (p == _animPoint ? lift : 1), s, style, p);
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

        var whiteEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x50, 0, 0, 0)), Math.Max(0.5, g.Cell * 0.015));
        foreach (Point p in board.AllPoints)
        {
            double o = t.OwnershipAt(p);
            double k = Math.Min(1, Math.Abs(o) / TerritoryEstimate.SecureThreshold);
            if (board[p] != Stone.Empty || k < 0.2 || t.SecureOwner(p) != Stone.Empty)
            {
                continue;
            }

            double h = g.Cell * (0.05 + (0.12 * k));
            AvPoint c = g.Center(p);
            var rect = new Rect(c.X - h, c.Y - h, 2 * h, 2 * h);
            if (o > 0)
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(90 + (110 * k)), 0x14, 0x14, 0x18)), null, rect);
            }
            else
            {
                context.DrawRectangle(new ImmutableSolidColorBrush(Color.FromArgb((byte)(120 + (110 * k)), 0xFA, 0xFA, 0xF8)), whiteEdge, rect);
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

        double h = g.Cell * 0.2;
        var blackFill = new ImmutableSolidColorBrush(Color.FromArgb(0xE6, 0x12, 0x12, 0x14));
        var whiteFill = new ImmutableSolidColorBrush(Color.FromArgb(0xF2, 0xFA, 0xFA, 0xF8));
        var whiteEdge = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x90, 0, 0, 0)), Math.Max(0.6, g.Cell * 0.02));
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
                context.DrawRectangle(blackFill, null, rect);
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

    // ---------- Atari alert ----------

    private const double AtariCycle = 2.4;
    private const double ShiverSeconds = 0.45;

    private AvPoint Trembled(BoardGeometry g, Point p)
    {
        AvPoint c = StoneCenter(g, p);
        return _tremble.TryGetValue(p, out AvPoint d) ? new AvPoint(c.X + d.X, c.Y + d.Y) : c;
    }

    /// <summary>Every <see cref="AtariCycle"/> seconds the group shivers briefly, like a nervous little jiggle.</summary>
    private Dictionary<Point, AvPoint> AtariOffsets(BoardGeometry g, BoardState board)
    {
        var offsets = new Dictionary<Point, AvPoint>();
        if (AtariGroups is not { Count: > 0 } groups || !Animate || Effects != Services.EffectsLevel.Full)
        {
            return offsets;
        }

        double t = AtariTime ?? _atariClock.Elapsed.TotalSeconds;
        foreach (AtariGroup group in groups)
        {
            double phase = (group.Liberty.X * 0.37) + (group.Liberty.Y * 0.61); // groups don't shiver in lockstep
            double u = (t + phase) % AtariCycle;
            if (u >= ShiverSeconds)
            {
                continue;
            }

            double amp = g.Cell * 0.03 * Math.Sin(Math.PI * u / ShiverSeconds);
            var d = new AvPoint(amp * Math.Sin(2 * Math.PI * 14 * u), amp * 0.35 * Math.Sin(2 * Math.PI * 21 * u));
            foreach (Point p in group.Stones)
            {
                if (board.IsOnBoard(p) && board[p] != Stone.Empty)
                {
                    offsets[p] = d;
                }
            }
        }

        return offsets;
    }

    /// <summary>A cartoon sweat drop on the group's top stone, sliding down its side and dripping off.</summary>
    private void DrawSweat(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (AtariGroups is not { Count: > 0 } groups || Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        double t = AtariTime ?? _atariClock.Elapsed.TotalSeconds;
        double r = g.Cell * StoneRadius;
        foreach (AtariGroup group in groups)
        {
            Point top = group.Stones.Where(p => board.IsOnBoard(p) && board[p] != Stone.Empty)
                .OrderBy(p => p.Y).ThenByDescending(p => p.X).FirstOrDefault(new Point(-1, -1));
            if (top.X < 0)
            {
                continue;
            }

            double phase = (group.Liberty.X * 0.37) + (group.Liberty.Y * 0.61);
            double u = Animate ? ((t + phase) % AtariCycle) / AtariCycle : 0.3;
            // 0–0.1 pops in, 0.1–0.7 slides from the upper right down the side, 0.7–0.85 drips off and fades.
            double angle = -Math.PI * 0.32;
            double size = 1;
            double alpha = 1;
            double drop = 0;
            if (u < 0.1)
            {
                size = u / 0.1;
            }
            else if (u < 0.7)
            {
                angle += (u - 0.1) / 0.6 * Math.PI * 0.32;
            }
            else if (u < 0.85)
            {
                angle = 0;
                double k = (u - 0.7) / 0.15;
                drop = k * k * r * 0.9;
                alpha = 1 - k;
            }
            else
            {
                continue;
            }

            AvPoint c = Trembled(g, top);
            var at = new AvPoint(c.X + (Math.Cos(angle) * r * 1.02), c.Y + (Math.Sin(angle) * r * 1.02) + drop);
            DrawDrop(context, at, g.Cell * 0.14 * (0.5 + (0.5 * size)), alpha);
        }
    }

    private static void DrawDrop(DrawingContext context, AvPoint bottom, double radius, double alpha)
    {
        // A teardrop: round belly at the bottom, pointed tip at the top.
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            var tip = new AvPoint(bottom.X, bottom.Y - (radius * 2.6));
            ctx.BeginFigure(tip, true);
            ctx.CubicBezierTo(new AvPoint(bottom.X + (radius * 0.4), bottom.Y - (radius * 1.6)), new AvPoint(bottom.X + (radius * 1.15), bottom.Y - (radius * 0.6)), new AvPoint(bottom.X + radius, bottom.Y));
            ctx.ArcTo(new AvPoint(bottom.X - radius, bottom.Y), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            ctx.CubicBezierTo(new AvPoint(bottom.X - (radius * 1.15), bottom.Y - (radius * 0.6)), new AvPoint(bottom.X - (radius * 0.4), bottom.Y - (radius * 1.6)), tip);
            ctx.EndFigure(true);
        }

        byte a = (byte)(255 * Math.Clamp(alpha, 0, 1));
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.3, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.7, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(a, 210, 236, 255), 0),
                new GradientStop(Color.FromArgb(a, 110, 175, 240), 1),
            },
        };
        context.DrawGeometry(fill, new Pen(new ImmutableSolidColorBrush(Color.FromArgb((byte)(a * 0.8), 50, 110, 190)), Math.Max(0.8, radius * 0.18)), geometry);
        context.DrawEllipse(new ImmutableSolidColorBrush(Color.FromArgb((byte)(a * 0.9), 255, 255, 255)), null, new AvPoint(bottom.X - (radius * 0.35), bottom.Y - (radius * 0.45)), radius * 0.22, radius * 0.3);
    }

    private void UpdateAtariTimer()
    {
        bool needed = AtariGroups is { Count: > 0 } && Animate && Effects != Services.EffectsLevel.Off;
        if (needed)
        {
            if (!_atariClock.IsRunning)
            {
                _atariClock.Restart();
            }

            _atariTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(33), Avalonia.Threading.DispatcherPriority.Render, (_, _) => InvalidateVisual());
            _atariTimer.Start();
        }
        else
        {
            _atariTimer?.Stop();
            _atariClock.Reset();
        }

        InvalidateVisual();
    }

    private void StartCapture(Point origin, List<(Point, Stone)> captured)
    {
        if (Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        _capture = new CaptureEffect(origin, captured, unchecked((origin.X * 31) + (origin.Y * 17) + captured.Count), Effects == Services.EffectsLevel.Subtle);
        _captureClock.Restart();
        _captureTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_capture is null || _capture.IsDone(_captureClock.Elapsed.TotalSeconds))
            {
                _capture = null;
                _captureTimer!.Stop();
            }

            InvalidateVisual();
        });
        _captureTimer.Start();
    }

    /// <summary>The strength the running impact is drawn with (1 when subtle), or null; for tests.</summary>
    internal int? RunningImpactStrength => _impact?.Strength;

    /// <summary>True when the running capture is the subtle fade; for tests.</summary>
    internal bool IsCaptureSubtle => _capture?.IsSubtle == true;

    /// <summary>Stones shivering in the last frame (atari alert); for tests.</summary>
    internal int TremblingStones => _tremble.Count;

    /// <summary>The captured stones being animated, for tests.</summary>
    internal int CapturingStones => _capture?.Count ?? 0;

    /// <summary>Freezes the capture animation at this many seconds (tests).</summary>
    internal double? CaptureTime { get; set; }

    private void StartImpact(ViewModels.BoardImpact impact)
    {
        if (!Animate || impact.Strength <= 0 || Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        _impact = new ImpactEffect(impact.Point, impact.Strength, impact.Id, Effects == Services.EffectsLevel.Subtle);
        _impactClock.Restart();
        _impactTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_impact is null || _impact.IsDone(_impactClock.Elapsed.TotalSeconds))
            {
                _impact = null;
                _impactTimer!.Stop();
            }

            InvalidateVisual();
        });
        _impactTimer.Start();
        InvalidateVisual();
    }

    private void StartPlacementAnimation(Point p)
    {
        _animPoint = p;
        _animClock.Restart();
        _animTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_animClock.Elapsed > EffectDuration && _animClock.Elapsed > SettleDuration)
            {
                _animPoint = null;
                _animTimer!.Stop();
            }

            InvalidateVisual();
        });
        _animTimer.Start();
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

    /// <summary>Draws one stone: one of Hoshi's rendered sprites when the style has a set, else the vector style.</summary>
    internal static void DrawStone(DrawingContext context, AvPoint c, double r, Stone color, BoardStyle board, Point p)
    {
        bool black = color == Stone.Black;
        if (!board.ShudanTexture && board.StoneSet is { } set
            && Themes.Skins.StoneSprite(set, black, black ? board.BlackVariants : board.WhiteVariants, p.X, p.Y) is { } sprite)
        {
            context.DrawImage(sprite, new Rect(sprite.Size), new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
            return;
        }

        StoneStyle style = board.ShudanTexture ? StoneStyle.Shudan : board.Stones;
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
        double radius = g.Cell * 0.2;
        context.DrawEllipse(null, new Pen(brush, Math.Max(1, g.Cell * 0.06)), StoneCenter(g, p), radius, radius);
    }

    private void DrawMarkers(DrawingContext context, BoardGeometry g, BoardState board, IBrush LineBrush, BoardStyle style)
    {
        IBrush labelBackground = style.ShudanTexture ? LabelBackground : new ImmutableSolidColorBrush(style.Wood);
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
            BoardStyle style = BoardStyle ?? ClassicStyle;
            DrawStone(context, g.Center(p), g.Cell * StoneRadius, GhostStone, style, p);
        }
    }

    private static void DrawCentredText(DrawingContext context, string text, double size, Typeface typeface, IBrush brush, double cx, double cy)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(ft, new AvPoint(cx - (ft.Width / 2), cy - (ft.Height / 2)));
    }
}
