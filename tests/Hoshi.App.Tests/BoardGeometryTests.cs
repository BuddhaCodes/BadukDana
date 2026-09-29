using Hoshi.App.Controls;
using Hoshi.Core;
using Point = Hoshi.Core.Point;
using AvPoint = Avalonia.Point;

namespace Hoshi.App.Tests;

public sealed class BoardGeometryTests
{
    [Fact]
    public void Grid_is_square_and_centred_in_a_wide_area()
    {
        BoardGeometry g = BoardGeometry.Create(1000, 600, 19, 19, showCoordinates: false);

        double span = 18 + (2 * BoardGeometry.PlainMargin);
        g.Cell.Should().BeApproximately(600 / span, 1e-9);
        g.BoardRect.Width.Should().BeApproximately(g.BoardRect.Height, 1e-9);
        g.BoardRect.Center.X.Should().BeApproximately(500, 1e-9);
        g.BoardRect.Center.Y.Should().BeApproximately(300, 1e-9);
    }

    [Fact]
    public void Coordinates_need_a_wider_margin()
    {
        BoardGeometry plain = BoardGeometry.Create(800, 800, 19, 19, showCoordinates: false);
        BoardGeometry withCoords = BoardGeometry.Create(800, 800, 19, 19, showCoordinates: true);

        withCoords.Cell.Should().BeLessThan(plain.Cell);
        withCoords.BoardRect.Width.Should().BeApproximately(800, 1e-9);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(18, 0)]
    [InlineData(9, 9)]
    [InlineData(18, 18)]
    public void Hit_testing_an_intersection_centre_returns_it(int x, int y)
    {
        BoardGeometry g = BoardGeometry.Create(760, 760, 19, 19, showCoordinates: true);

        g.HitTest(g.Center(new Point(x, y))).Should().Be(new Point(x, y));
    }

    [Fact]
    public void Hit_testing_near_but_not_on_an_intersection()
    {
        BoardGeometry g = BoardGeometry.Create(760, 760, 19, 19, showCoordinates: true);
        AvPoint c = g.Center(new Point(3, 3));

        g.HitTest(new AvPoint(c.X + (g.Cell * 0.3), c.Y + (g.Cell * 0.2))).Should().Be(new Point(3, 3));
        g.HitTest(new AvPoint(c.X + (g.Cell * 0.45), c.Y + (g.Cell * 0.45))).Should().BeNull("corners between points are ambiguous");
        g.HitTest(new AvPoint(2, 2)).Should().BeNull("the margin is outside the grid");
    }

    [Fact]
    public void Star_points_by_board_size()
    {
        BoardGeometry.StarPoints(19, 19).Should().HaveCount(9).And.Contain(new Point(3, 15));
        BoardGeometry.StarPoints(13, 13).Should().HaveCount(5).And.Contain(new Point(6, 6));
        BoardGeometry.StarPoints(9, 9).Should().BeEquivalentTo([new Point(2, 2), new Point(6, 2), new Point(2, 6), new Point(6, 6), new Point(4, 4)]);
        BoardGeometry.StarPoints(5, 5).Should().BeEmpty();
    }
}
