using Hoshi.Core;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Controls;

/// <summary>
/// Maps board intersections to control coordinates (device-independent pixels) and back.
/// The grid is centred in the available area, square cells, with a margin around it for the stones and coordinates.
/// </summary>
public readonly record struct BoardGeometry
{
    /// <summary>Margin around the grid, in cells, without coordinates (DESIGN.md: ≈ 0.8 cells).</summary>
    public const double PlainMargin = 0.8;

    /// <summary>Margin around the grid, in cells, when coordinates are shown on all four sides.</summary>
    public const double CoordinateMargin = 1.35;

    private BoardGeometry(int columns, int rows, double cell, double originX, double originY, double margin)
    {
        Columns = columns;
        Rows = rows;
        Cell = cell;
        OriginX = originX;
        OriginY = originY;
        Margin = margin;
    }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>Distance between two adjacent lines.</summary>
    public double Cell { get; }

    /// <summary>Position of intersection (0,0).</summary>
    public double OriginX { get; }

    public double OriginY { get; }

    public double Margin { get; }

    /// <summary>The wooden board rectangle, including margins.</summary>
    public Avalonia.Rect BoardRect => new(
        OriginX - (Margin * Cell),
        OriginY - (Margin * Cell),
        ((Columns - 1) + (2 * Margin)) * Cell,
        ((Rows - 1) + (2 * Margin)) * Cell);

    public static BoardGeometry Create(double width, double height, int columns, int rows, bool showCoordinates)
    {
        double margin = showCoordinates ? CoordinateMargin : PlainMargin;
        double spanX = (columns - 1) + (2 * margin);
        double spanY = (rows - 1) + (2 * margin);
        double cell = Math.Max(0, Math.Min(width / spanX, height / spanY));
        double originX = ((width - ((columns - 1) * cell)) / 2);
        double originY = ((height - ((rows - 1) * cell)) / 2);
        return new BoardGeometry(columns, rows, cell, originX, originY, margin);
    }

    public AvPoint Center(Point p) => new(OriginX + (p.X * Cell), OriginY + (p.Y * Cell));

    /// <summary>The intersection under <paramref name="position"/>, if it is within half a cell of one.</summary>
    public Point? HitTest(AvPoint position)
    {
        if (Cell <= 0)
        {
            return null;
        }

        int x = (int)Math.Round((position.X - OriginX) / Cell);
        int y = (int)Math.Round((position.Y - OriginY) / Cell);
        if (x < 0 || y < 0 || x >= Columns || y >= Rows)
        {
            return null;
        }

        AvPoint c = Center(new Point(x, y));
        double dx = position.X - c.X;
        double dy = position.Y - c.Y;
        return (dx * dx) + (dy * dy) <= (Cell * Cell / 4) ? new Point(x, y) : null;
    }

    /// <summary>Star points for the usual board sizes (none for sizes without a convention).</summary>
    public static IReadOnlyList<Point> StarPoints(int columns, int rows)
    {
        if (columns != rows || columns < 7)
        {
            return [];
        }

        int size = columns;
        int e = size >= 13 ? 3 : 2;
        int hi = size - 1 - e;
        int mid = size / 2;
        var points = new List<Point> { new(e, e), new(hi, e), new(e, hi), new(hi, hi) };
        if (size % 2 == 1)
        {
            points.Add(new Point(mid, mid));
            if (size >= 15)
            {
                points.AddRange([new(mid, e), new(mid, hi), new(e, mid), new(hi, mid)]);
            }
        }

        return points;
    }
}
