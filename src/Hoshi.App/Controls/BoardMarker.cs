using Hoshi.Core;

namespace Hoshi.App.Controls;

/// <summary>SGF markup shapes (TR, SQ, CR, MA, LB).</summary>
public enum BoardMarkerKind
{
    Triangle,
    Square,
    Circle,
    Cross,
    Label,
}

/// <summary>A marker drawn on an intersection. <see cref="Text"/> is only used by <see cref="BoardMarkerKind.Label"/>.</summary>
public sealed record BoardMarker(Point Point, BoardMarkerKind Kind, string? Text = null);
