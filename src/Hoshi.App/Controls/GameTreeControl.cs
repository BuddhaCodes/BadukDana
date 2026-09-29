using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.Core;
using Hoshi.Sgf;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// Draws the variation tree in Sabaki's style (its game graph: 22 px grid, light nodes, orange for comments, squares
/// for passes, a diamond for non-move nodes, the current track brighter and thicker, everything else dimmed, and
/// variations joined with a diagonal). Clicking a node executes <see cref="NodeClickedCommand"/> with that node.
/// Meant to be hosted in a ScrollViewer.
/// </summary>
public sealed class GameTreeControl : Control
{
    public static readonly StyledProperty<GameNode?> RootProperty =
        AvaloniaProperty.Register<GameTreeControl, GameNode?>(nameof(Root));

    public static readonly StyledProperty<GameNode?> CurrentProperty =
        AvaloniaProperty.Register<GameTreeControl, GameNode?>(nameof(Current));

    /// <summary>Bind to a counter that changes on every edit, so the layout is recomputed.</summary>
    public static readonly StyledProperty<int> VersionProperty =
        AvaloniaProperty.Register<GameTreeControl, int>(nameof(Version));

    public static readonly StyledProperty<int> BoardSizeProperty =
        AvaloniaProperty.Register<GameTreeControl, int>(nameof(BoardSize), 19);

    public static readonly StyledProperty<ICommand?> NodeClickedCommandProperty =
        AvaloniaProperty.Register<GameTreeControl, ICommand?>(nameof(NodeClickedCommand));

    public const double Spacing = 22;
    public const double Padding = 14;
    private const double NodeRadius = 5;

    private static readonly Color NodeColor = Color.FromRgb(0xEE, 0xEE, 0xEE);
    private static readonly Color CommentColor = Color.FromRgb(255, 174, 61);
    private static readonly Color BadMoveColor = Color.FromRgb(240, 35, 17);
    private static readonly Color DoubtfulColor = Color.FromRgb(146, 39, 143);
    private static readonly Color InterestingColor = Color.FromRgb(72, 134, 213);
    private static readonly Color GoodMoveColor = Color.FromRgb(89, 168, 15);
    private static readonly IPen EdgePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)), 1);
    private static readonly IPen TrackPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)), 2);
    private static readonly IPen NodeOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)), 1);
    private static readonly IPen CurrentOutline = new ImmutablePen(new ImmutableSolidColorBrush(NodeColor), 2);

    private GameTreeLayout? _layout;

    static GameTreeControl()
    {
        AffectsMeasure<GameTreeControl>(RootProperty, VersionProperty);
        AffectsRender<GameTreeControl>(RootProperty, CurrentProperty, VersionProperty);
    }

    public GameNode? Root
    {
        get => GetValue(RootProperty);
        set => SetValue(RootProperty, value);
    }

    public GameNode? Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    public int Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    public int BoardSize
    {
        get => GetValue(BoardSizeProperty);
        set => SetValue(BoardSizeProperty, value);
    }

    public ICommand? NodeClickedCommand
    {
        get => GetValue(NodeClickedCommandProperty);
        set => SetValue(NodeClickedCommandProperty, value);
    }

    public GameTreeLayout? Layout => _layout;

    public static AvPoint CenterOf((int Column, int Row) position) =>
        new(Padding + (position.Column * Spacing), Padding + (position.Row * Spacing));

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_layout is null || Root is null)
        {
            return;
        }

        HashSet<GameNode> track = CurrentTrack();
        foreach ((GameNode node, (int Column, int Row) pos) in _layout.Positions)
        {
            if (node.Parent is not { } parent || !_layout.Positions.TryGetValue(parent, out var parentPos))
            {
                continue;
            }

            bool onTrack = track.Contains(node) && track.Contains(parent);
            IPen pen = onTrack ? TrackPen : EdgePen;
            AvPoint from = CenterOf(parentPos);
            AvPoint to = CenterOf(pos);
            if (parentPos.Column == pos.Column)
            {
                context.DrawLine(pen, from, to);
            }
            else
            {
                // Along the parent's row to the column before the child, then diagonally down into it.
                var elbow = new AvPoint(to.X - Spacing, from.Y);
                var geometry = new StreamGeometry();
                using (StreamGeometryContext ctx = geometry.Open())
                {
                    ctx.BeginFigure(from, false);
                    ctx.LineTo(elbow);
                    ctx.LineTo(to);
                    ctx.EndFigure(false);
                }

                context.DrawGeometry(null, pen, geometry);
            }
        }

        foreach ((GameNode node, (int Column, int Row) pos) in _layout.Positions)
        {
            AvPoint c = CenterOf(pos);
            Color color = FillFor(node);
            if (!track.Contains(node))
            {
                color = Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2));
            }

            var fill = new ImmutableSolidColorBrush(color);
            IPen outline = node == Current ? CurrentOutline : NodeOutline;
            SgfMove? move = node.GetMove(BoardSize);
            double r = NodeRadius;
            if (move is null)
            {
                // Non-move node (root, setup): a diamond.
                var diamond = new StreamGeometry();
                using (StreamGeometryContext ctx = diamond.Open())
                {
                    ctx.BeginFigure(new AvPoint(c.X, c.Y - r - 1), true);
                    ctx.LineTo(new AvPoint(c.X + r + 1, c.Y));
                    ctx.LineTo(new AvPoint(c.X, c.Y + r + 1));
                    ctx.LineTo(new AvPoint(c.X - r - 1, c.Y));
                    ctx.EndFigure(true);
                }

                context.DrawGeometry(fill, outline, diamond);
            }
            else if (move.Value.IsPass)
            {
                context.DrawRectangle(fill, outline, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
            }
            else
            {
                context.DrawEllipse(fill, outline, c, r, r);
            }
        }
    }

    /// <summary>Colour of a node as in Sabaki: move annotations, then comments, otherwise light grey.</summary>
    private static Color FillFor(GameNode node) =>
        node.HasProperty("BM") ? BadMoveColor
        : node.HasProperty("DO") ? DoubtfulColor
        : node.HasProperty("IT") ? InterestingColor
        : node.HasProperty("TE") ? GoodMoveColor
        : node.Comment is not null || node.HasProperty("N") ? CommentColor
        : NodeColor;

    /// <summary>Root → current node, then on along the first children (Sabaki's "current track").</summary>
    private HashSet<GameNode> CurrentTrack()
    {
        var track = new HashSet<GameNode>();
        if (Current is not { } current)
        {
            return track;
        }

        for (GameNode? n = current; n is not null; n = n.Parent)
        {
            track.Add(n);
        }

        for (GameNode n = current; n.Children.Count > 0;)
        {
            n = n.Children[0];
            track.Add(n);
        }

        return track;
    }

    /// <summary>The node whose circle contains <paramref name="position"/>, if any.</summary>
    public GameNode? HitTest(AvPoint position)
    {
        if (_layout is null)
        {
            return null;
        }

        int column = (int)Math.Round((position.X - Padding) / Spacing);
        int row = (int)Math.Round((position.Y - Padding) / Spacing);
        foreach ((GameNode node, (int Column, int Row) pos) in _layout.Positions)
        {
            if (pos.Column == column && pos.Row == row)
            {
                AvPoint c = CenterOf(pos);
                double dx = position.X - c.X;
                double dy = position.Y - c.Y;
                return (dx * dx) + (dy * dy) <= (Spacing / 2) * (Spacing / 2) ? node : null;
            }
        }

        return null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _layout = Root is null ? null : GameTreeLayout.Compute(Root);
        if (_layout is null)
        {
            return default;
        }

        return new Size(
            (2 * Padding) + (Math.Max(0, _layout.Columns - 1) * Spacing),
            (2 * Padding) + (Math.Max(0, _layout.Rows - 1) * Spacing));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CurrentProperty && Current is { } node)
        {
            // Keep the current node visible inside the hosting ScrollViewer once layout has run.
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_layout?.Positions.TryGetValue(node, out var pos) == true)
                {
                    AvPoint c = CenterOf(pos);
                    this.BringIntoView(new Rect(c.X - Spacing, c.Y - Spacing, 2 * Spacing, 2 * Spacing));
                }
            });
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left && HitTest(e.GetPosition(this)) is { } node
            && NodeClickedCommand is { } command && command.CanExecute(node))
        {
            command.Execute(node);
            e.Handled = true;
        }
    }
}
