using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Hoshi.App.Themes;

namespace Hoshi.App.Controls;

/// <summary>
/// Draws an <see cref="IconData"/> from the current theme's icon set (bind <see cref="Icon"/> to
/// <c>{DynamicResource Icon.X}</c>). Filled icons use the foreground brush; stroked ones a round pen.
/// Replaces emoji-like glyphs (⏮ ⏭), which Windows renders as colour emoji.
/// </summary>
public sealed class HoshiIcon : Control
{
    public static readonly StyledProperty<IconData?> IconProperty =
        AvaloniaProperty.Register<HoshiIcon, IconData?>(nameof(Icon));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<HoshiIcon>();

    public static readonly StyledProperty<double> SizeProperty =
        AvaloniaProperty.Register<HoshiIcon, double>(nameof(Size), 18);

    static HoshiIcon()
    {
        AffectsRender<HoshiIcon>(IconProperty, ForegroundProperty);
        AffectsMeasure<HoshiIcon>(SizeProperty);
    }

    public IconData? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public double Size
    {
        get => GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    public override void Render(DrawingContext context)
    {
        if (Icon is not { } icon || Foreground is not { } brush || icon.Size <= 0)
        {
            return;
        }

        double side = Math.Min(Bounds.Width, Bounds.Height);
        double scale = side / icon.Size;
        var offset = new Vector((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset)))
        {
            if (icon.Stroke)
            {
                // Slightly thinner than the source's 2 px at 24 so small icons stay elegant.
                var pen = new Pen(brush, icon.StrokeWidth * 0.85, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                context.DrawGeometry(null, pen, icon.Geometry);
            }
            else
            {
                context.DrawGeometry(brush, null, icon.Geometry);
            }
        }
    }
}
