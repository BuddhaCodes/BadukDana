using CommunityToolkit.Mvvm.ComponentModel;
using Hoshi.App.Services.Engines;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>A player in the "Play against an engine" dialog: you, or one of the engines.</summary>
public sealed record PlayerOption(string Label, EngineChoice? Engine)
{
    public override string ToString() => Label;
}

/// <summary>A rule set in the dialog.</summary>
public sealed record RulesOption(string Label, RuleSet Rules);

/// <summary>
/// "New game against an engine": who plays Black and White (you or any engine, so also engine vs engine), board
/// size, handicap, komi and rules.
/// </summary>
public sealed partial class NewEngineGameViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(Hint))]
    private PlayerOption _black;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart), nameof(Hint))]
    private PlayerOption _white;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Handicaps))]
    private int _size = 19;

    [ObservableProperty]
    private int _handicap;

    [ObservableProperty]
    private double _komi = 6.5;

    [ObservableProperty]
    private RulesOption _rules;

    public NewEngineGameViewModel(IReadOnlyList<EngineChoice> engines, RuleSet? rules = null)
    {
        ArgumentNullException.ThrowIfNull(engines);
        PlayerOption you = new(Tr.T("Engines.You"), null);
        Players = [you, .. engines.Select(e => new PlayerOption(e.Name, e))];
        _black = you;
        _white = Players.Count > 1 ? Players[1] : you;
        RulesOptions =
        [
            new(Tr.T("Game.Rules.Japanese"), RuleSet.Japanese),
            new(Tr.T("Game.Rules.Chinese"), RuleSet.Chinese),
            new(Tr.T("Game.Rules.Korean"), RuleSet.Korean),
            new("AGA", RuleSet.Aga),
            new(Tr.T("Game.Rules.NewZealand"), RuleSet.NewZealand),
        ];
        _rules = RulesOptions.FirstOrDefault(r => r.Rules == rules) ?? RulesOptions[0];
    }

    public IReadOnlyList<PlayerOption> Players { get; }

    public IReadOnlyList<int> Sizes { get; } = [9, 13, 19];

    public IReadOnlyList<RulesOption> RulesOptions { get; }

    /// <summary>0 (none) and 2 up to the maximum fixed handicap for the size.</summary>
    public IReadOnlyList<int> Handicaps => [0, .. Enumerable.Range(2, Math.Max(0, Core.Handicap.MaxFixed(Size) - 1))];

    public bool HasEngines => Players.Count > 1;

    public bool CanStart => Black.Engine is not null || White.Engine is not null;

    public string Hint =>
        !HasEngines ? Tr.T("Engines.NoEngines")
        : !CanStart ? Tr.T("Engines.ChooseAnEngine")
        : Black.Engine is not null && White.Engine is not null ? Tr.T("Engines.EngineVsEngineHint")
        : Tr.T("Engines.PlayHint");

    partial void OnSizeChanged(int value)
    {
        if (!Handicaps.Contains(Handicap))
        {
            Handicap = 0;
        }
    }

    partial void OnHandicapChanged(int value) => Komi = value >= 2 ? 0.5 : Rules.Rules.DefaultKomi;

    partial void OnRulesChanged(RulesOption value)
    {
        if (Handicap < 2)
        {
            Komi = value.Rules.DefaultKomi;
        }
    }

    /// <summary>The new game: size, rules, komi, handicap stones and the players' names.</summary>
    public GameTree CreateTree(string? humanName = null)
    {
        GameTree tree = GameTree.Create(Size, Rules.Rules, Komi);
        tree.Info.BlackPlayer = Black.Engine?.Name ?? humanName ?? Tr.T("Engines.You");
        tree.Info.WhitePlayer = White.Engine?.Name ?? humanName ?? Tr.T("Engines.You");
        tree.Info.Date = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        if (Handicap >= 2)
        {
            tree.Info.Handicap = Handicap;
            tree.Root.SetValues("AB", Core.Handicap.FixedPoints(Size, Handicap).Select(p => p.ToSgf()));
        }

        return tree;
    }
}
