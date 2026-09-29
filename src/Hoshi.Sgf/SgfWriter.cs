using System.Reflection;
using System.Text;

namespace Hoshi.Sgf;

/// <summary>
/// Writes FF[4] SGF. Output is stable: properties follow a fixed order (root/game info, moves and setup,
/// comments, markup, then everything else alphabetically) so saved files diff cleanly.
/// </summary>
public static class SgfWriter
{
    private static readonly string[] Order =
    [
        // Root and game info.
        "FF", "GM", "CA", "AP", "ST", "SZ", "KM", "HA", "RU", "TM", "OT",
        "PB", "BR", "BT", "PW", "WR", "WT", "DT", "EV", "RO", "PC", "GN", "GC", "RE", "SO", "US", "AN", "CP",

        // Moves and setup.
        "B", "W", "AB", "AW", "AE", "PL", "BL", "WL", "OB", "OW",

        // Node annotation.
        "N", "C", "GB", "GW", "DM", "UC", "HO", "V", "BM", "TE", "DO", "IT",

        // Markup.
        "TR", "SQ", "CR", "MA", "LB", "AR", "LN", "DD", "SL",
    ];

    private static readonly Dictionary<string, int> Rank =
        Order.Select((id, i) => (id, i)).ToDictionary(t => t.id, t => t.i, StringComparer.Ordinal);

    private static readonly string AppValue =
        "Hoshi:" + (typeof(SgfWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] ?? "0.1.0");

    public static string Write(GameTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var sb = new StringBuilder();
        WriteTree(sb, tree.Root, isRoot: true);
        sb.Append('\n');
        return sb.ToString();
    }

    public static string Write(IEnumerable<GameTree> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return string.Concat(collection.Select(Write));
    }

    /// <summary>UTF-8 without a byte-order mark, matching the <c>CA[UTF-8]</c> written in the root.</summary>
    public static byte[] WriteBytes(GameTree tree) => new UTF8Encoding(false).GetBytes(Write(tree));

    private static void WriteTree(StringBuilder sb, GameNode first, bool isRoot)
    {
        sb.Append('(');
        GameNode node = first;
        bool root = isRoot;
        while (true)
        {
            WriteNode(sb, node, root);
            root = false;
            if (node.Children.Count == 1)
            {
                sb.Append('\n');
                node = node.Children[0];
                continue;
            }

            foreach (GameNode child in node.Children)
            {
                sb.Append('\n');
                WriteTree(sb, child, isRoot: false);
            }

            break;
        }

        sb.Append(')');
    }

    private static void WriteNode(StringBuilder sb, GameNode node, bool isRoot)
    {
        sb.Append(';');
        IEnumerable<(string Id, IReadOnlyList<string> Values)> props = node.PropertyIds
            .Select(id => (id, node.GetValues(id)));

        if (isRoot)
        {
            var overrides = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["FF"] = ["4"],
                ["GM"] = [node.GetValue("GM") ?? "1"],
                ["CA"] = ["UTF-8"],
                ["AP"] = [AppValue],
            };
            props = props.Where(p => !overrides.ContainsKey(p.Id))
                .Concat(overrides.Select(o => (o.Key, o.Value)));
        }

        foreach ((string id, IReadOnlyList<string> values) in props
            .OrderBy(p => Rank.TryGetValue(p.Id, out int r) ? r : int.MaxValue)
            .ThenBy(p => p.Id, StringComparer.Ordinal))
        {
            sb.Append(id);
            foreach (string v in values)
            {
                sb.Append('[');
                AppendEscaped(sb, v);
                sb.Append(']');
            }
        }
    }

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        foreach (char c in value)
        {
            if (c is ']' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }
    }
}
