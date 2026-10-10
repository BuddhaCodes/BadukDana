using Hoshi.Core;
using Hoshi.Sgf.Study;

namespace Hoshi.Sgf.Tests;

public sealed class StudyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 18, 30, 0, TimeSpan.Zero);

    private static GameTree Game(params string[] moves)
    {
        GameTree tree = GameTree.Create(19);
        GameNode node = tree.Root;
        for (int i = 0; i < moves.Length; i++)
        {
            node = node.AddChild();
            node.SetValue(i % 2 == 0 ? "B" : "W", moves[i]);
        }

        return tree;
    }

    private static GameNode Node(GameTree tree, int moveNumber)
    {
        GameNode n = tree.Root;
        for (int i = 0; i < moveNumber; i++)
        {
            n = n.Children[0];
        }

        return n;
    }

    private static NodeStudy Sample(string author = "Ana") => new(
        [
            new StudyPin("p1", PinCategory.Mistake, author, T0, "Too slow ] really \\ slow", [new StudyReply("Ben", T0.AddMinutes(5), "Tenuki to the left was big")]),
            new StudyPin("p2", PinCategory.KeyMoment, author, T0, string.Empty, []),
        ],
        [
            new StudyDrawing("d1", DrawingKind.Arrow, author, StudyColor.Red, [new Point(3, 3), new Point(5, 5)]),
            new StudyDrawing("d2", DrawingKind.Area, author, StudyColor.Blue, [new Point(0, 0), new Point(2, 4)]),
            new StudyDrawing("d3", DrawingKind.Mark, author, StudyColor.Gold, [new Point(9, 9)]),
            new StudyDrawing("d4", DrawingKind.Label, author, StudyColor.Green, [new Point(10, 10)], "A"),
            new StudyDrawing("d5", DrawingKind.Sequence, author, StudyColor.Purple, [new Point(15, 3), new Point(16, 5), new Point(13, 2)], "W"),
        ]);

    [Fact]
    public void Study_items_round_trip_through_an_sgf_file()
    {
        GameTree tree = Game("pd", "dp", "pp");
        StudyStore.Write(Node(tree, 2), Sample());

        GameTree back = SgfParser.Parse(SgfWriter.Write(tree));
        NodeStudy read = StudyStore.Read(Node(back, 2));
        read.Pins.Should().BeEquivalentTo(Sample().Pins, o => o.WithStrictOrdering());
        read.Drawings.Should().BeEquivalentTo(Sample().Drawings, o => o.WithStrictOrdering());
        StudyStore.Read(Node(back, 1)).IsEmpty.Should().BeTrue();
        StudyStore.All(back).Should().ContainSingle().Which.Node.Should().Be(Node(back, 2));
    }

    [Fact]
    public void An_empty_study_removes_the_property_and_unknown_data_is_ignored()
    {
        GameTree tree = Game("pd");
        GameNode n = Node(tree, 1);
        StudyStore.Write(n, Sample());
        StudyStore.Write(n, NodeStudy.Empty);
        n.HasProperty(StudyStore.Property).Should().BeFalse();

        n.SetValue(StudyStore.Property, "{not json");
        StudyStore.Read(n).IsEmpty.Should().BeTrue();
        n.SetValue(StudyStore.Property, """{"v":1,"pins":[{"id":"x","cat":"FromTheFuture","by":"Z","at":"2026-10-10T00:00:00Z","text":"?"}],"draw":[{"id":"y","kind":"Hologram","by":"Z","col":"Gold","pts":["aa"]}],"later":true}""");
        NodeStudy future = StudyStore.Read(n);
        future.Pins.Should().ContainSingle().Which.Category.Should().Be(PinCategory.Question, "unknown categories become questions");
        future.Drawings.Should().BeEmpty("unknown drawings are skipped");
    }

    [Fact]
    public void Other_programs_see_comments_and_markup_and_Hoshi_strips_them_again()
    {
        GameTree tree = Game("pd", "dp");
        GameNode n = Node(tree, 2);
        n.Comment = "My own comment";
        n.SetValue("TR", "aa");
        StudyStore.Write(n, Sample());

        string sgf = StudyStore.WriteWithMirror(tree);
        GameTree other = SgfParser.Parse(sgf);
        GameNode o = Node(other, 2);
        o.Comment.Should().StartWith("My own comment").And.Contain("Mistake (Ana): Too slow ] really \\ slow").And.Contain("↳ Ben: Tenuki to the left was big");
        o.GetValues("AR").Should().Equal("dd:ff");
        o.GetValues("LB").Should().Contain(["kk:A", "pd:1", "qf:2", "nc:3"]);
        o.GetValues("SQ").Should().HaveCount(15, "the area is shown as squares");
        o.GetValues("TR").Should().Equal("aa");

        StudyStore.StripMirror(other);
        o.Comment.Should().Be("My own comment");
        o.HasProperty("AR").Should().BeFalse();
        o.HasProperty("LB").Should().BeFalse();
        o.HasProperty("SQ").Should().BeFalse();
        o.GetValues("TR").Should().BeEquivalentTo(["aa"], "the user's own marks stay");
        StudyStore.Read(o).Pins.Should().HaveCount(2);

        // Writing with the mirror leaves the tree in memory as it was.
        n.Comment.Should().Be("My own comment");
        n.HasProperty("AR").Should().BeFalse();
    }

    [Fact]
    public void Merging_a_friends_study_adds_their_notes_by_move_and_creates_missing_moves()
    {
        GameTree mine = Game("pd", "dp", "pp");
        StudyStore.Write(Node(mine, 2), new NodeStudy([new StudyPin("p1", PinCategory.Question, "Ana", T0, "Why not 3-3?", [])], []));

        GameTree theirs = Game("pd", "dp", "pp", "dd");
        StudyStore.Write(Node(theirs, 2), new NodeStudy(
            [
                new StudyPin("p1", PinCategory.Question, "Ana", T0, "Why not 3-3?", [new StudyReply("Ben", T0.AddHours(1), "3-3 is fine too")]),
                new StudyPin("b1", PinCategory.Idea, "Ben", T0, "Approach first", []),
            ],
            [new StudyDrawing("bd", DrawingKind.Arrow, "Ben", StudyColor.Blue, [new Point(3, 3), new Point(2, 2)])]));
        StudyStore.Write(Node(theirs, 4), new NodeStudy([new StudyPin("b2", PinCategory.GoodMove, "Ben", T0, "Nice", [])], []));

        StudyMergeResult result = StudyStore.Merge(mine, theirs);
        result.Items.Should().Be(4, "two pins, a drawing and a reply are new");
        result.NewMoves.Should().Be(1);

        NodeStudy merged = StudyStore.Read(Node(mine, 2));
        merged.Pins.Should().HaveCount(2);
        merged.Pins.Single(p => p.Id == "p1").Replies.Should().ContainSingle().Which.Author.Should().Be("Ben");
        merged.Drawings.Should().ContainSingle();
        Node(mine, 4).GetMove(19)!.Value.Point.Should().Be(new Point(3, 3));
        StudyStore.Read(Node(mine, 4)).Pins.Should().ContainSingle();

        StudyStore.Merge(mine, theirs).Items.Should().Be(0, "merging twice changes nothing");
        StudyStore.Authors(mine).Should().Equal("Ana", "Ben");
    }

    [Fact]
    public void Merging_without_creating_moves_skips_notes_on_moves_that_are_gone()
    {
        GameTree reloaded = Game("pd", "dp");
        GameTree before = Game("pd", "dp", "pp");
        StudyStore.Write(Node(before, 2), new NodeStudy([new StudyPin("a", PinCategory.Idea, "Ana", T0, "kept", [])], []));
        StudyStore.Write(Node(before, 3), new NodeStudy([new StudyPin("b", PinCategory.Idea, "Ana", T0, "taken back", [])], []));

        StudyStore.Merge(reloaded, before, createMissingMoves: false).Should().Be(new StudyMergeResult(1, 0));
        Node(reloaded, 2).Children.Should().BeEmpty("an online game's moves only come from the server");
        StudyStore.Read(Node(reloaded, 2)).Pins.Should().ContainSingle().Which.Text.Should().Be("kept");
    }

    [Fact]
    public void Areas_cover_the_rectangle_between_their_corners()
    {
        var area = new StudyDrawing("a", DrawingKind.Area, "Ana", StudyColor.Blue, [new Point(4, 1), new Point(2, 2)]);
        area.AreaPoints().Should().HaveCount(6).And.Contain([new Point(2, 1), new Point(4, 2)]);
    }

    [Fact]
    public void Pin_ids_are_short_and_unique()
    {
        var ids = Enumerable.Range(0, 500).Select(_ => StudyStore.NewId()).ToList();
        ids.Should().OnlyHaveUniqueItems().And.OnlyContain(i => i.Length == 8);
    }
}
