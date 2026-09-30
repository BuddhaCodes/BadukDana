using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// The game at a glance: black's estimated lead per move (above the line = black ahead). Unanalysed moves are
/// gaps. The current move is marked; clicking a move executes <see cref="MoveClickedCommand"/> with its index.
/// </summary>
public sealed class ScoreGraph : Control
{
    public static readonly StyledProperty<IReadOnlyList<double?>?> ValuesProperty =
        AvaloniaProperty.Register<ScoreGraph, IReadOnlyList<double?>?>(nameof(Values));

    public static readonly StyledProperty<int> CurrentIndexProperty =
        AvaloniaProperty.Register<ScoreGraph, int>(nameof(CurrentIndex));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<ScoreGraph, IBrush?>(nameof(LineBrush), Brushes.White);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<ScoreGraph, IBrush?>(nameof(Accent), Brushes.Orange);

    public static readonly StyledProperty<ICommand?> MoveClickedCommandProperty =
        AvaloniaProperty.Register<ScoreGraph, ICommand?>(nameof(MoveClickedCommand));

    static ScoreGraph()
    {
        AffectsRender<ScoreGraph>(ValuesProperty, CurrentIndexProperty, LineBrushProperty, AccentProperty);
    }

    public IReadOnlyList<double?>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public int CurrentIndex
    {
        get => GetValue(CurrentIndexProperty);
        set => SetValue(CurrentIndexProperty, value);
    }

    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public ICommand? MoveClickedCommand
    {
        get => GetValue(MoveClickedCommandProperty);
        set => SetValue(MoveClickedCommandProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, r);
        if (Values is not { Count: > 1 } values || r.Width < 10 || r.Height < 10)
        {
            return;
        }

        double max = Math.Max(5, values.Where(v => v is not null).Select(v => Math.Abs(v!.Value)).DefaultIfEmpty(0).Max());
        double mid = r.Height / 2;
        double X(int i) => 4 + ((r.Width - 8) * i / (values.Count - 1));
        double Y(double v) => mid - (v / max * (mid - 4));

        var axis = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x90)), 1);
        context.DrawLine(axis, new AvPoint(0, mid), new AvPoint(r.Width, mid));

        // Area: black ahead above the axis (dark), white ahead below (light).
        var blackArea = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0x05, 0x05, 0x08));
        var whiteArea = new ImmutableSolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        var line = new Pen(LineBrush ?? Brushes.White, 1.4, lineJoin: PenLineJoin.Round);
        int? prev = null;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] is not { } v)
            {
                prev = null;
                continue;
            }

            if (prev is { } j && values[j] is { } pv)
            {
                var a = new AvPoint(X(j), Y(pv));
                var b = new AvPoint(X(i), Y(v));
                var area = new StreamGeometry();
                using (StreamGeometryContext ctx = area.Open())
                {
                    ctx.BeginFigure(new AvPoint(a.X, mid), true);
                    ctx.LineTo(a);
                    ctx.LineTo(b);
                    ctx.LineTo(new AvPoint(b.X, mid));
                    ctx.EndFigure(true);
                }

                context.DrawGeometry((pv + v) / 2 >= 0 ? blackArea : whiteArea, null, area);
                context.DrawLine(line, a, b);
            }

            prev = i;
        }

        if (CurrentIndex >= 0 && CurrentIndex < values.Count)
        {
            double x = X(CurrentIndex);
            context.DrawLine(new Pen(Accent ?? Brushes.Orange, 1.2), new AvPoint(x, 0), new AvPoint(x, r.Height));
            if (values[CurrentIndex] is { } cv)
            {
                context.DrawEllipse(Accent ?? Brushes.Orange, null, new AvPoint(x, Y(cv)), 3, 3);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Values is not { Count: > 1 } values || MoveClickedCommand is not { } command)
        {
            return;
        }

        double x = e.GetPosition(this).X;
        int index = (int)Math.Round((x - 4) / Math.Max(1, Bounds.Width - 8) * (values.Count - 1));
        index = Math.Clamp(index, 0, values.Count - 1);
        if (command.CanExecute(index))
        {
            command.Execute(index);
        }
    }
}
