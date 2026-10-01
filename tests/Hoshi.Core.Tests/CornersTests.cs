namespace Hoshi.Core.Tests;

public sealed class CornersTests
{
    private static Point P(string sgf) => Point.FromSgf(sgf);

    [Fact]
    public void Each_corner_covers_its_ten_by_ten_lines()
    {
        Corners.Contains(Corner.TopRight, P("pd"), 19).Should().BeTrue();
        Corners.Contains(Corner.TopRight, P("dd"), 19).Should().BeFalse();
        Corners.Contains(Corner.TopRight, P("jd"), 19).Should().BeTrue("the centre line belongs to both corners");
        Corners.Contains(Corner.TopLeft, P("jd"), 19).Should().BeTrue();
        Corners.Contains(Corner.TopRight, P("jj"), 19).Should().BeFalse("tengen is in no corner");
        Corners.Contains(Corner.TopRight, P("ij"), 19).Should().BeFalse();
        Corners.Nearest(P("dp"), 19).Should().Be(Corner.BottomLeft);
        Corners.Distance(Corner.BottomRight, P("qq"), 19).Should().Be((2, 2));
    }

    [Fact]
    public void A_corner_sequence_ignores_other_corners_and_marks_tenuki()
    {
        (Stone, Point?)[] game =
        [
            (Stone.Black, P("pd")),
            (Stone.White, P("dp")),   // another corner
            (Stone.Black, P("dd")),   // another corner
            (Stone.White, P("qc")),
            (Stone.Black, P("pc")),
            (Stone.White, null),      // pass
            (Stone.Black, P("qd")),   // Black twice in a row here: White tenuki'd
        ];

        IReadOnlyList<CornerMove> seq = Corners.Sequence(game, Corner.TopRight, 19);

        seq.Should().Equal(
            new CornerMove(Stone.Black, P("pd")),
            new CornerMove(Stone.White, P("qc")),
            new CornerMove(Stone.Black, P("pc")),
            new CornerMove(Stone.White, null),
            new CornerMove(Stone.Black, P("qd")));
        seq[3].IsTenuki.Should().BeTrue();
    }

    [Fact]
    public void Two_symmetries_carry_one_corner_onto_another()
    {
        foreach (Corner from in Corners.All)
        {
            IReadOnlyList<Symmetry> maps = Corners.Mapping(from, Corner.TopRight, 19);
            maps.Should().HaveCount(2);
            Point local = from switch
            {
                Corner.TopLeft => P("dd"),
                Corner.TopRight => P("pd"),
                Corner.BottomLeft => P("dp"),
                _ => P("pp"),
            };
            maps.Should().OnlyContain(s => s.Apply(local, 19) == P("pd"));
        }
    }
}
