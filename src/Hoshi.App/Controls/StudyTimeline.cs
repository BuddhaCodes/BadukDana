using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Hoshi.App.ViewModels;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// The pins of the game along a line: one coloured dot per studied move (stacked when a move has several), the
/// current move as a tick. Clicking a dot executes <see cref="MarkClickedCommand"/> with its <see cref="TimelineMark"/>.
/// </summary>
public sealed class StudyTimeline : Control
{
    public static readonly StyledProperty<IReadOnlyList<TimelineMark>?> MarksProperty =
        AvaloniaProperty.Register<StudyTimeline, IReadOnlyList<TimelineMark>?>(nameof(Marks));

    public static readonly StyledProperty<int> TotalProperty =
        AvaloniaProperty.Register<StudyTimeline, int>(nameof(Total));

    public static readonly StyledProperty<int> CurrentProperty =
        AvaloniaProperty.Register<StudyTimeline, int>(nameof(Current));

    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<StudyTimeline, IBrush?>(nameof(LineBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<StudyTimeline, IBrush?>(nameof(Accent), Brushes.Orange);

    public static readonly StyledProperty<ICommand?> MarkClickedCommandProperty =
        AvaloniaProperty.Register<StudyTimeline, ICommand?>(nameof(MarkClickedCommand));

    private const double Pad = 7;
    private const double Dot = 4.2;

    static StudyTimeline()
    {
        AffectsRender<StudyTimeline>(MarksProperty, TotalProperty, CurrentProperty, LineBrushProperty, AccentProperty);
    }

    public IReadOnlyList<TimelineMark>? Marks
    {
        get => GetValue(MarksProperty);
        set => SetValue(MarksProperty, value);
    }

    /// <summary>Moves on the line (the right end).</summary>
    public int Total
    {
        get => GetValue(TotalProperty);
        set => SetValue(TotalProperty, value);
    }

    /// <summary>The move shown on the board.</summary>
    public int Current
    {
        get => GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
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

    public ICommand? MarkClickedCommand
    {
        get => GetValue(MarkClickedCommandProperty);
        set => SetValue(MarkClickedCommandProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 30);

    public override void Render(DrawingContext context)
    {
        var r = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, r);
        if (r.Width < 2 * Pad)
        {
            return;
        }

        double y = r.Height - 8;
        IBrush line = LineBrush ?? Brushes.Gray;
        context.DrawLine(new Pen(line, 1.2, lineCap: PenLineCap.Round), new AvPoint(Pad, y), new AvPoint(r.Width - Pad, y));

        // The current move: a small accent tick on the line.
        double cx = X(Current, r.Width);
        context.DrawLine(new Pen(Accent ?? Brushes.Orange, 2, lineCap: PenLineCap.Round), new AvPoint(cx, y - 5), new AvPoint(cx, y + 4));

        foreach ((TimelineMark mark, AvPoint at) in Layout(r))
        {
            Color c = Color.Parse(mark.Colour);
            context.DrawEllipse(new ImmutableSolidColorBrush(c), new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(160, 0, 0, 0)), 1), at, Dot, Dot);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        AvPoint p = e.GetPosition(this);
        var r = new Rect(Bounds.Size);
        TimelineMark? hit = Layout(r)
            .Select(x => (x.Mark, Distance: Math.Abs(x.At.X - p.X) + (Math.Abs(x.At.Y - p.Y) * 0.5)))
            .Where(x => x.Distance < 10)
            .OrderBy(x => x.Distance)
            .Select(x => x.Mark)
            .FirstOrDefault();
        if (hit is not null && MarkClickedCommand is { } command && command.CanExecute(hit))
        {
            command.Execute(hit);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        AvPoint p = e.GetPosition(this);
        TimelineMark? near = Layout(new Rect(Bounds.Size)).Where(x => Math.Abs(x.At.X - p.X) < 8).Select(x => x.Mark).FirstOrDefault();
        ToolTip.SetTip(this, near?.Tip);
        Cursor = near is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    private double X(int move, double width) => Pad + ((width - (2 * Pad)) * Math.Clamp(move, 0, Math.Max(1, Total)) / Math.Max(1, Total));

    /// <summary>Where each dot goes: on the line, stacked upwards when several marks share a move.</summary>
    private IEnumerable<(TimelineMark Mark, AvPoint At)> Layout(Rect r)
    {
        double y = r.Height - 8;
        foreach (IGrouping<int, TimelineMark> g in (Marks ?? []).GroupBy(m => m.Move))
        {
            int i = 0;
            foreach (TimelineMark m in g.Take(3))
            {
                yield return (m, new AvPoint(X(g.Key, r.Width), y - (i * ((2 * Dot) + 1))));
                i++;
            }
        }
    }
}
