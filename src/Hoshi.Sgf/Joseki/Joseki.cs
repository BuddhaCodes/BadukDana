using System.Globalization;
using System.Text;
using Hoshi.Core;

namespace Hoshi.Sgf.Joseki;

public readonly record struct JosekiMove(Stone Color, Point Point);

/// <summary>A joseki sequence: every move from the empty board to one leaf of a joseki SGF.</summary>
/// <param name="Id">The same for every orientation of the sequence (smallest of its eight spellings).</param>
public sealed record JosekiLine(string Id, string Name, int Size, IReadOnlyList<JosekiMove> Moves, string? Comment)
{
    public static string CanonicalId(IReadOnlyList<JosekiMove> moves, int size) =>
        Symmetry.All.Select(s => Spell(moves, s, size)).Min(StringComparer.Ordinal)!;

    private static string Spell(IReadOnlyList<JosekiMove> moves, Symmetry s, int size)
    {
        var sb = new StringBuilder(size.ToString(CultureInfo.InvariantCulture)).Append(':');
        foreach (JosekiMove m in moves)
        {
            sb.Append(m.Color == Stone.Black ? 'B' : 'W').Append(s.Apply(m.Point, size).ToSgf());
        }

        return sb.ToString();
    }
}

/// <summary>Turns joseki SGF files (a tree of variations from an empty board) into <see cref="JosekiLine"/>s.</summary>
public static class JosekiLibraryReader
{
    /// <summary>
    /// One line per leaf (passes and setup stones are ignored; lines under two moves are skipped). A line is named
    /// after the last <c>N[]</c> on its path, or "<paramref name="collection"/> n". Orientations of the same
    /// sequence are kept once.
    /// </summary>
    public static IReadOnlyList<JosekiLine> ExtractLines(GameTree tree, string? collection)
    {
        ArgumentNullException.ThrowIfNull(tree);
        int size = tree.Info.Width;
        if (size != tree.Info.Height || size > 19)
        {
            return [];
        }

        string prefix = string.IsNullOrWhiteSpace(collection) ? "Joseki" : collection.Trim();
        var lines = new List<JosekiLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (GameNode leaf in tree.Root.Descendants().Where(n => n.Children.Count == 0))
        {
            IReadOnlyList<GameNode> path = GameCursor.Path(leaf);
            var moves = new List<JosekiMove>();
            string? name = null;
            foreach (GameNode node in path)
            {
                if (node.GetMove(size) is { Point: { } p } move)
                {
                    moves.Add(new JosekiMove(move.Color, p));
                }

                if (node.GetValue("N") is { Length: > 0 } n)
                {
                    name = n.Trim();
                }
            }

            if (moves.Count < 2)
            {
                continue;
            }

            string id = JosekiLine.CanonicalId(moves, size);
            if (!seen.Add(id))
            {
                continue;
            }

            string? comment = string.IsNullOrWhiteSpace(leaf.Comment) ? null : leaf.Comment.Trim();
            lines.Add(new JosekiLine(id, name ?? string.Create(CultureInfo.InvariantCulture, $"{prefix} {lines.Count + 1}"), size, moves, comment));
        }

        return lines;
    }
}
