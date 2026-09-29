using Hoshi.Sgf;

namespace Hoshi.App.Controls;

/// <summary>
/// Grid layout of a game tree for <see cref="GameTreeControl"/>: rows are depths, the main line stays in column 0
/// and each variation is placed in the leftmost column that is free for all the rows its subtree spans.
/// </summary>
public sealed class GameTreeLayout
{
    private readonly Dictionary<GameNode, (int Column, int Row)> _positions = [];

    private GameTreeLayout()
    {
    }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public IReadOnlyDictionary<GameNode, (int Column, int Row)> Positions => _positions;

    public static GameTreeLayout Compute(GameNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var layout = new GameTreeLayout();
        Dictionary<GameNode, int> heights = SubtreeHeights(root);
        var nextFree = new List<int>(); // per row: first free column

        // Explicit stack instead of recursion so very long games are fine.
        var work = new Stack<(GameNode Node, int Column, int Row)>();
        work.Push((root, 0, 0));
        while (work.Count > 0)
        {
            (GameNode node, int requested, int row) = work.Pop();
            int column = Place(layout, nextFree, node, requested, row);

            // Push variations in reverse so they are placed after the main child, left to right.
            IReadOnlyList<GameNode> children = node.Children;
            var pending = new List<(GameNode, int, int)>();
            for (int i = 0; i < children.Count; i++)
            {
                GameNode child = children[i];
                int childRow = row + 1;
                int childColumn = i == 0 ? column : -1;
                pending.Add((child, childColumn, childRow));
            }

            // The main child must be placed (and its whole line reserved) before siblings pick columns,
            // so resolve sibling columns lazily: push siblings first, main child last (popped first).
            for (int i = pending.Count - 1; i >= 1; i--)
            {
                work.Push(pending[i]);
            }

            if (pending.Count > 0)
            {
                work.Push(pending[0]);
            }
        }

        layout.Rows = nextFree.Count;
        layout.Columns = nextFree.Count == 0 ? 0 : nextFree.Max();
        return layout;

        int Place(GameTreeLayout l, List<int> free, GameNode node, int column, int row)
        {
            if (column < 0)
            {
                int parentColumn = l._positions[node.Parent!].Column;
                int height = heights[node];
                column = parentColumn + 1;
                for (int r = row; r < row + height; r++)
                {
                    column = Math.Max(column, r < free.Count ? free[r] : 0);
                }

                // Reserve the column for the whole subtree height so later siblings go further right.
                for (int r = row; r < row + height; r++)
                {
                    Reserve(free, r, column + 1);
                }
            }

            l._positions[node] = (column, row);
            Reserve(free, row, column + 1);
            return column;
        }
    }

    private static void Reserve(List<int> free, int row, int value)
    {
        while (free.Count <= row)
        {
            free.Add(0);
        }

        free[row] = Math.Max(free[row], value);
    }

    /// <summary>Number of rows each subtree spans (1 for a leaf), computed without recursion.</summary>
    private static Dictionary<GameNode, int> SubtreeHeights(GameNode root)
    {
        var heights = new Dictionary<GameNode, int>();
        var order = root.Descendants().ToList();
        for (int i = order.Count - 1; i >= 0; i--)
        {
            GameNode n = order[i];
            int h = 0;
            foreach (GameNode c in n.Children)
            {
                h = Math.Max(h, heights[c]);
            }

            heights[n] = h + 1;
        }

        return heights;
    }
}
