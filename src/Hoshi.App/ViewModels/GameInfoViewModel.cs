using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Hoshi.Core;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

/// <summary>Editable copy of the game-info properties; <see cref="ApplyTo"/> writes them back on OK.</summary>
public sealed partial class GameInfoViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _blackPlayer = string.Empty;

    [ObservableProperty]
    private string _blackRank = string.Empty;

    [ObservableProperty]
    private string _whitePlayer = string.Empty;

    [ObservableProperty]
    private string _whiteRank = string.Empty;

    [ObservableProperty]
    private string _gameName = string.Empty;

    [ObservableProperty]
    private string _event = string.Empty;

    [ObservableProperty]
    private string _date = string.Empty;

    [ObservableProperty]
    private string _place = string.Empty;

    [ObservableProperty]
    private string _result = string.Empty;

    [ObservableProperty]
    private string _komi = string.Empty;

    [ObservableProperty]
    private RuleSet? _rules;

    public GameInfoViewModel()
    {
    }

    public GameInfoViewModel(GameInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        _blackPlayer = info.BlackPlayer ?? string.Empty;
        _blackRank = info.BlackRank ?? string.Empty;
        _whitePlayer = info.WhitePlayer ?? string.Empty;
        _whiteRank = info.WhiteRank ?? string.Empty;
        _gameName = info.GameName ?? string.Empty;
        _event = info.Event ?? string.Empty;
        _date = info.Date ?? string.Empty;
        _place = info.Place ?? string.Empty;
        _result = info.Result ?? string.Empty;
        _komi = info.Komi?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _rules = info.Rules;
    }

    public IReadOnlyList<RuleSet> AvailableRules => RuleSet.All;

    public bool KomiIsValid => string.IsNullOrWhiteSpace(Komi) || TryParseKomi(Komi, out _);

    public void ApplyTo(GameInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.BlackPlayer = BlackPlayer;
        info.BlackRank = BlackRank;
        info.WhitePlayer = WhitePlayer;
        info.WhiteRank = WhiteRank;
        info.GameName = GameName;
        info.Event = Event;
        info.Date = Date;
        info.Place = Place;
        info.Result = Result;
        info.Komi = TryParseKomi(Komi, out double k) ? k : null;
        if (Rules is not null)
        {
            info.Rules = Rules;
        }
    }

    partial void OnKomiChanged(string value) => OnPropertyChanged(nameof(KomiIsValid));

    private static bool TryParseKomi(string text, out double komi) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out komi);
}
