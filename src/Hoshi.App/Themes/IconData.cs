using Avalonia.Media;

namespace Hoshi.App.Themes;

/// <summary>
/// A vector icon: SVG path data in a <paramref name="Size"/>×<paramref name="Size"/> box, either filled (Phosphor)
/// or stroked with <paramref name="StrokeWidth"/> (Lucide, Tabler), drawn in the control's foreground colour.
/// </summary>
public sealed record IconData(string Data, double Size, bool Stroke, double StrokeWidth)
{
    private Geometry? _geometry;

    public Geometry Geometry => _geometry ??= Geometry.Parse(Data);
}
