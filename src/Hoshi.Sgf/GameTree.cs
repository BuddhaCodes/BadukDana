using System.Globalization;
using Hoshi.Core;

namespace Hoshi.Sgf;

/// <summary>One game of an SGF collection.</summary>
public sealed class GameTree
{
    public GameTree(GameNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = root;
        Info = new GameInfo(root);
    }

    public GameNode Root { get; }

    /// <summary>Typed view over the game-info properties of the root node.</summary>
    public GameInfo Info { get; }

    public static GameTree Create(int size, RuleSet? rules = null, double? komi = null)
    {
        var root = new GameNode();
        root.SetValue("GM", "1");
        root.SetValue("FF", "4");
        root.SetValue("SZ", size.ToString(CultureInfo.InvariantCulture));
        var tree = new GameTree(root);
        if (rules is not null)
        {
            tree.Info.Rules = rules;
        }

        tree.Info.Komi = komi;
        return tree;
    }

    public IEnumerable<GameNode> AllNodes() => Root.Descendants();
}

/// <summary>Game-info properties (FF[4] "game-info" and "root" types). Setting an empty value removes it.</summary>
public sealed class GameInfo
{
    private static readonly Dictionary<string, string> RuleAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["japanese"] = "japanese",
        ["jp"] = "japanese",
        ["korean"] = "korean",
        ["chinese"] = "chinese",
        ["cn"] = "chinese",
        ["aga"] = "aga",
        ["nz"] = "nz",
        ["new zealand"] = "nz",
        ["newzealand"] = "nz",
        ["ing"] = "ing",
        ["goe"] = "ing",
    };

    private readonly GameNode _root;

    internal GameInfo(GameNode root) => _root = root;

    public int Width => Size.Width;

    public int Height => Size.Height;

    public double? Komi
    {
        get => double.TryParse(_root.GetValue("KM"), NumberStyles.Float, CultureInfo.InvariantCulture, out double k) ? k : null;
        set => Set("KM", value?.ToString(CultureInfo.InvariantCulture));
    }

    public int Handicap
    {
        get => int.TryParse(_root.GetValue("HA"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
        set => Set("HA", value >= 2 ? value.ToString(CultureInfo.InvariantCulture) : null);
    }

    /// <summary>Rule set from <c>RU</c>, recognising the names other programs write; null when absent or unknown.</summary>
    public RuleSet? Rules
    {
        get
        {
            string? ru = _root.GetValue("RU")?.Trim();
            return ru is not null && RuleAliases.TryGetValue(ru, out string? name) ? RuleSet.FromName(name) : null;
        }

        set => Set("RU", value is null ? null : DisplayRuleName(value));
    }

    public string? RulesText => _root.GetValue("RU");

    public string? BlackPlayer { get => _root.GetValue("PB"); set => Set("PB", value); }

    public string? BlackRank { get => _root.GetValue("BR"); set => Set("BR", value); }

    public string? WhitePlayer { get => _root.GetValue("PW"); set => Set("PW", value); }

    public string? WhiteRank { get => _root.GetValue("WR"); set => Set("WR", value); }

    public string? Date { get => _root.GetValue("DT"); set => Set("DT", value); }

    public string? Event { get => _root.GetValue("EV"); set => Set("EV", value); }

    public string? Result { get => _root.GetValue("RE"); set => Set("RE", value); }

    public string? GameName { get => _root.GetValue("GN"); set => Set("GN", value); }

    public string? Place { get => _root.GetValue("PC"); set => Set("PC", value); }

    private (int Width, int Height) Size
    {
        get
        {
            string? sz = _root.GetValue("SZ");
            if (string.IsNullOrWhiteSpace(sz))
            {
                return (19, 19);
            }

            string[] parts = sz.Split(':');
            int w = int.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
            int h = parts.Length > 1 ? int.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture) : w;
            return (w, h);
        }
    }

    private static string DisplayRuleName(RuleSet rules) => rules.Name switch
    {
        "japanese" => "Japanese",
        "korean" => "Korean",
        "chinese" => "Chinese",
        "aga" => "AGA",
        "nz" => "NZ",
        "ing" => "GOE",
        _ => rules.Name,
    };

    private void Set(string id, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _root.RemoveProperty(id);
        }
        else
        {
            _root.SetValue(id, value.Trim());
        }
    }
}
