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
/// Draws the variation tree (DESIGN.md "Árbol de variantes"): small black/white circles per move, thin grey lines,
/// the current node highlighted with the accent colour and a dot on nodes with comments. Clicking a node executes
/// <see cref="NodeClickedCommand"/> with that node. Meant to be hosted in a ScrollViewer.
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
    private const double NodeRadius = 5.5;

    private static readonly IBrush LineBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A));
    private static readonly IBrush MainLineBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80));
    private static readonly IBrush BlackNode = new ImmutableSolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));
    private static readonly IBrush WhiteNode = new ImmutableSolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6));
    private static readonly IBrush SetupNode = new ImmutableSolidColorBrush(Color.FromRgb(0x7A, 0x7A, 0x7A));
    private static readonly IPen NodeOutline = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 1);
    private static readonly IBrush CommentDot = new ImmutableSolidColorBrush(Color.FromRgb(0x6C, 0xA6, 0xE0));

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

        var linePen = new Pen(LineBrush, 1);
        var mainPen = new Pen(MainLineBrush, 1.5);
        foreach ((GameNode node, (int Column, int Row) pos) in _layout.Positions)
        {
            if (node.Parent is { } parent && _layout.Positions.TryGetValue(parent, out var parentPos))
            {
                bool main = parentPos.Column == pos.Column;
                context.DrawLine(main ? mainPen : linePen, CenterOf(parentPos), CenterOf(pos));
            }
        }

        var accent = this.TryFindResource("Accent", ActualThemeVariant, out object? a) && a is IBrush b
            ? b
            : new ImmutableSolidColorBrush(Color.FromRgb(0xE0, 0xA9, 0x4A));

        foreach ((GameNode node, (int Column, int Row) pos) in _layout.Positions)
        {
            AvPoint c = CenterOf(pos);
            SgfMove? move = node.GetMove(BoardSize);
            IBrush fill = move?.Color switch
            {
                Stone.Black => BlackNode,
                Stone.White => WhiteNode,
                _ => SetupNode,
            };

            if (node == Current)
            {
                context.DrawEllipse(null, new Pen(accent, 2.5), c, NodeRadius + 3.5, NodeRadius + 3.5);
            }

            if (move is null)
            {
                // Root and setup-only nodes are drawn as small squares.
                context.DrawRectangle(fill, NodeOutline, new Rect(c.X - NodeRadius + 1, c.Y - NodeRadius + 1, (NodeRadius - 1) * 2, (NodeRadius - 1) * 2));
            }
            else if (move.Value.IsPass)
            {
                context.DrawEllipse(null, new Pen(fill, 2), c, NodeRadius - 1, NodeRadius - 1);
            }
            else
            {
                context.DrawEllipse(fill, NodeOutline, c, NodeRadius, NodeRadius);
            }

            if (node.Comment is not null)
            {
                context.DrawEllipse(CommentDot, null, new AvPoint(c.X + NodeRadius + 2, c.Y - NodeRadius - 1), 2.2, 2.2);
            }
        }
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
