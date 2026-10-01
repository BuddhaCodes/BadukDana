using Hoshi.Core;
using Hoshi.Sgf.Joseki;

namespace Hoshi.Sgf.Tests;

public sealed class JosekiTests
{
    private static Point P(string sgf) => Point.FromSgf(sgf);

    // 3-3 invasion under a top-right 4-4 point (19×19).
    private const string Invasion = "(;GM[1]SZ[19];B[pd];W[qc];B[pc]N[3-3 invasion];W[qd];B[qe];W[re];B[rf];W[rd])";

    [Fact]
    public void Symmetries_map_the_board_onto_itself_and_identity_is_first()
    {
        Symmetry.All.Should().HaveCount(8);
        Symmetry.All[0].Apply(P("pd"), 19).Should().Be(P("pd"));
        Symmetry.All.Select(s => s.Apply(P("pd"), 19)).Distinct().Should().HaveCount(4, "a 4-4 point has four images");
        Symmetry.All.Select(s => s.Apply(P("qc"), 19)).Distinct().Should().HaveCount(4);
        Symmetry.All.Select(s => s.Apply(P("pc"), 19)).Distinct().Should().HaveCount(8);
        foreach (Symmetry s in Symmetry.All)
        {
            Point p = s.Apply(P("ac"), 19);
            p.X.Should().BeInRange(0, 18);
            p.Y.Should().BeInRange(0, 18);
        }
    }

    [Fact]
    public void Every_symmetry_has_an_inverse()
    {
        foreach (Symmetry s in Symmetry.All)
        {
            s.Inverse.Apply(s.Apply(P("cf"), 19), 19).Should().Be(P("cf"));
        }
    }

    [Fact]
    public void Extracts_one_line_per_leaf_with_the_name_from_the_path()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(
            SgfParser.Parse("(;SZ[19];B[pd](;W[qc];B[pc]N[Block top];W[qd])(;W[nc]N[Approach];B[qf]))"), "Test");

        lines.Should().HaveCount(2);
        lines[0].Name.Should().Be("Block top");
        lines[0].Moves.Select(m => m.Point).Should().Equal(P("pd"), P("qc"), P("pc"), P("qd"));
        lines[0].Moves.Select(m => m.Color).Should().Equal(Stone.Black, Stone.White, Stone.Black, Stone.White);
        lines[1].Name.Should().Be("Approach");
        lines[0].Size.Should().Be(19);
    }

    [Fact]
    public void Mirrored_lines_get_the_same_id_and_are_deduplicated()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(
            SgfParser.Parse("(;SZ[19](;B[pd];W[qc];B[pc];W[qd])(;B[dp];W[cq];B[cp];W[dq])(;B[pd];W[qc];B[qd];W[pc]))"), "Test");

        lines.Should().HaveCount(1, "all three are the same sequence in different orientations");
    }

    [Fact]
    public void Lines_shorter_than_two_moves_and_passes_are_skipped()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(
            SgfParser.Parse("(;SZ[19](;B[pd])(;B[dd];W[];B[cc];W[dc]))"), null);

        lines.Should().ContainSingle();
        lines[0].Moves.Should().HaveCount(3);
        lines[0].Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_line_is_named_from_the_collection_when_it_has_no_name()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(SgfParser.Parse("(;SZ[19];B[pd];W[qc])"), "My lines");
        lines[0].Name.Should().Be("My lines 1");
    }

    [Fact]
    public void The_comment_of_the_last_move_is_kept()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(SgfParser.Parse("(;SZ[19];B[pd];W[qc]C[White lives.])"), null);
        lines[0].Comment.Should().Be("White lives.");
    }

    [Fact]
    public void A_drill_accepts_the_line_and_completes()
    {
        JosekiLine line = Single(Invasion);
        var drill = new JosekiDrill(line, Symmetry.All[0], [line]);

        foreach (JosekiMove move in line.Moves.SkipLast(1))
        {
            drill.Attempt(move.Point).Should().Be(DrillAnswer.Correct);
        }

        drill.Attempt(line.Moves[^1].Point).Should().Be(DrillAnswer.Completed);
        drill.IsComplete.Should().BeTrue();
        drill.Mistakes.Should().Be(0);
        drill.Board[P("rd")].Should().Be(Stone.White);
    }

    [Fact]
    public void A_wrong_move_counts_as_a_mistake_and_shows_the_answer_after_two_misses()
    {
        JosekiLine line = Single(Invasion);
        var drill = new JosekiDrill(line, Symmetry.All[0], [line]);

        drill.Attempt(P("aa")).Should().Be(DrillAnswer.Wrong);
        drill.Hint.Should().BeNull();
        drill.Attempt(P("ab")).Should().Be(DrillAnswer.Wrong);
        drill.Hint.Should().Be(P("pd"));
        drill.Mistakes.Should().Be(2);
        drill.Board[P("aa")].Should().Be(Stone.Empty, "wrong stones are never placed");
        drill.Attempt(P("pd")).Should().Be(DrillAnswer.Correct);
        drill.Hint.Should().BeNull();
    }

    [Fact]
    public void A_mirrored_move_follows_the_same_line_in_the_other_orientation()
    {
        JosekiLine line = Single(Invasion);
        var drill = new JosekiDrill(line, Symmetry.All[0], [line]);
        drill.Attempt(P("pd"));
        drill.Attempt(P("qc"));

        // Block from the side (the line blocks on top): the same joseki mirrored on the diagonal.
        drill.Attempt(P("qd")).Should().Be(DrillAnswer.Correct);
        drill.Expected.Should().Be(P("pc"));
    }

    [Fact]
    public void Another_known_line_is_accepted_as_also_joseki_without_advancing()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(
            SgfParser.Parse("(;SZ[19];B[pd](;W[qc];B[pc];W[qd])(;W[nc];B[qf];W[kc]))"), null);
        var drill = new JosekiDrill(lines[0], Symmetry.All[0], lines);
        drill.Attempt(P("pd"));

        drill.Attempt(P("nc")).Should().Be(DrillAnswer.AlsoJoseki);
        drill.Mistakes.Should().Be(0);
        drill.Expected.Should().Be(P("qc"));
    }

    [Fact]
    public void A_drill_in_another_corner_expects_transformed_moves()
    {
        JosekiLine line = Single(Invasion);
        Symmetry s = Symmetry.All.First(x => x.Apply(P("pd"), 19) == P("dp"));
        var drill = new JosekiDrill(line, s, [line]);

        drill.Expected.Should().Be(P("dp"));
        drill.Attempt(P("dp")).Should().Be(DrillAnswer.Correct);
        drill.ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public void Leitner_boxes_rise_on_success_and_reset_on_mistakes()
    {
        var start = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        JosekiCard card = JosekiCard.New("x");
        card.IsDue(start).Should().BeTrue();

        card = card.Review(perfect: true, start);
        card.Box.Should().Be(2);
        card.Due.Should().Be(start.AddDays(Leitner.Interval(2).TotalDays));
        card.IsDue(start.AddHours(1)).Should().BeFalse();

        card = card.Review(perfect: true, card.Due).Review(perfect: true, start.AddDays(10));
        card.Box.Should().Be(4);

        card = card.Review(perfect: false, start.AddDays(30));
        card.Box.Should().Be(1);
        card.Lapses.Should().Be(1);
        card.Reviews.Should().Be(4);

        Enumerable.Range(0, 20).Aggregate(card, (c, _) => c.Review(true, start)).Box.Should().Be(Leitner.MaxBox);
    }

    [Fact]
    public void Picks_due_cards_first_then_new_ones()
    {
        var now = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var cards = new Dictionary<string, JosekiCard>
        {
            ["a"] = JosekiCard.New("a").Review(true, now),             // not due
            ["b"] = JosekiCard.New("b").Review(false, now.AddDays(-5)), // due (box 1, overdue)
        };

        Leitner.Next(["a", "b", "c"], cards, now, new Random(1)).Should().Be("b");
        cards["b"] = cards["b"].Review(true, now);
        Leitner.Next(["a", "b", "c"], cards, now, new Random(1)).Should().Be("c");
        cards["c"] = JosekiCard.New("c").Review(true, now);
        Leitner.Next(["a", "b", "c"], cards, now, new Random(1)).Should().BeNull("nothing is due");
        Leitner.DueCount(["a", "b", "c"], cards, now).Should().Be(0);
        Leitner.DueCount(["a", "b", "c", "d"], cards, now).Should().Be(1);
    }

    private static JosekiLine Single(string sgf) => JosekiLibraryReader.ExtractLines(SgfParser.Parse(sgf), null).Single();
}

public sealed class JosekiMatcherTests
{
    private static Point P(string sgf) => Point.FromSgf(sgf);

    private static readonly IReadOnlyList<JosekiLine> Lines = JosekiLibraryReader.ExtractLines(
        SgfParser.Parse("(;SZ[19];B[pd](;W[qc];B[pc];W[qd])(;W[nc];B[qf]))"), null);

    [Fact]
    public void Finds_every_continuation_in_any_corner()
    {
        // The same start in the bottom-left corner.
        CornerMove[] seq = [new(Stone.Black, P("dp"))];
        IReadOnlyList<Point> next = JosekiMatcher.Continuations(Lines, seq, 19);

        next.Should().Contain(P("cq"), "the 3-3 invasion");
        next.Should().HaveCount(3, "the 3-3 point has one image, the approach two (one per side)");
    }

    [Fact]
    public void Colours_may_be_swapped_and_tenuki_or_unknown_shapes_match_nothing()
    {
        JosekiMatcher.Continuations(Lines, [new(Stone.White, P("pd")), new(Stone.Black, P("qc"))], 19).Should().BeEquivalentTo([P("pc"), P("qd")]);
        JosekiMatcher.Continuations(Lines, [new(Stone.Black, P("pd")), new(Stone.White, null)], 19).Should().BeEmpty();
        JosekiMatcher.Continuations(Lines, [new(Stone.Black, P("aa"))], 19).Should().BeEmpty();
        JosekiMatcher.Continuations(Lines, [], 19).Should().BeEmpty();
    }

    [Theory]
    [InlineData("pd", JosekiFamily.Hoshi)]
    [InlineData("qd", JosekiFamily.Komoku)]
    [InlineData("pc", JosekiFamily.Komoku)]
    [InlineData("qc", JosekiFamily.SanSan)]
    [InlineData("dp", JosekiFamily.Hoshi)]
    [InlineData("pe", JosekiFamily.Takamoku)]
    [InlineData("qe", JosekiFamily.Mokuhazushi)]
    [InlineData("jj", JosekiFamily.Other)]
    public void Families_come_from_the_first_stone(string point, JosekiFamily family) =>
        JosekiFamilies.Of(P(point), 19).Should().Be(family);
}

public sealed class JosekiMatchingTests
{
    [Fact]
    public void A_completed_line_is_still_matched()
    {
        IReadOnlyList<JosekiLine> lines = JosekiLibraryReader.ExtractLines(SgfParser.Parse("(;SZ[19];B[pd];W[qc]N[Short])"), null);
        CornerMove[] seq = [new(Stone.Black, Point.FromSgf("pd")), new(Stone.White, Point.FromSgf("qc"))];

        JosekiMatcher.Matching(lines, seq, 19).Select(m => m.Line.Name).Distinct().Should().Equal("Short");
        JosekiMatcher.Continuations(lines, seq, 19).Should().BeEmpty();
    }
}
