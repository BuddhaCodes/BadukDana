using Hoshi.Core;
using Hoshi.Sgf.Export;

namespace Hoshi.Sgf.Tests;

public sealed class TextDiagramTests
{
    private static BoardState Position()
    {
        // Black (2,2), white (3,2), black (6,6) last; a triangle on the white stone, a label "a" on an empty point.
        BoardState board = BoardState.Create(9);
        board = board.Play(Stone.Black, new Point(2, 2));
        board = board.Play(Stone.White, new Point(3, 2));
        return board.Play(Stone.Black, new Point(6, 6));
    }

    [Fact]
    public void Senseis_diagram_has_the_header_the_frame_the_stones_and_the_marks()
    {
        string text = TextDiagram.Senseis(Position(), new Point(6, 6), [new Markup(new Point(3, 2), MarkupKind.Triangle), new Markup(new Point(0, 8), MarkupKind.Label, "a")], coordinates: true, title: "Move 3");
        string[] lines = text.Split('\n');

        lines[0].Should().Be("$$Wc9 Move 3", "white to play, coordinates, a 9×9 board");
        lines[1].Should().Be("$$ +-------------------+");
        lines[2].Should().Be("$$ | . . . . . . . . . |");
        lines[4].Should().Be("$$ | . . X Q . . , . . |", "star point, black stone, white stone with a triangle");
        lines[8].Should().Be("$$ | . . , . . . B . . |", "the last move is circled");
        lines[10].Should().Be("$$ | a . . . . . . . . |");
        lines[11].Should().Be("$$ +-------------------+");
        lines.Should().HaveCount(12);
    }

    [Fact]
    public void Senseis_marks_follow_the_stone_under_them()
    {
        BoardState board = BoardState.Create(9).Play(Stone.Black, new Point(0, 0)).Play(Stone.White, new Point(1, 0));
        Markup[] marks =
        [
            new(new Point(0, 0), MarkupKind.Square), new(new Point(1, 0), MarkupKind.Cross),
            new(new Point(2, 0), MarkupKind.Circle), new(new Point(3, 0), MarkupKind.Square),
            new(new Point(4, 0), MarkupKind.Triangle), new(new Point(5, 0), MarkupKind.Cross),
        ];

        string row = TextDiagram.Senseis(board, null, marks, coordinates: false).Split('\n')[2];
        row.Should().Be("$$ | # P C S T M . . . |");
        TextDiagram.Senseis(board, null, null, coordinates: false).Split('\n')[0].Should().Be("$$B");
    }

    [Fact]
    public void The_code_block_has_coordinates_and_brackets_around_the_last_move()
    {
        string text = TextDiagram.CodeBlock(Position(), new Point(6, 6));
        string[] lines = text.Split('\n');

        lines[0].Should().Be("```");
        lines[1].Should().Be("   A B C D E F G H J");
        lines[4].Should().Be(" 7 . . X O . . + . .  7");
        lines[8].Should().Be(" 3 . . + . . .[X]. .  3");
        lines[11].Should().Be("   A B C D E F G H J");
        lines[12].Should().Be("```");
    }
}
