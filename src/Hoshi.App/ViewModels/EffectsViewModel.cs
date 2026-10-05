using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// The strength of the board effects (impacts of strong moves, shattering captures, the atari alert): Off, Subtle
/// or Full. Changed from the bottom bar, the sound panel, Preferences or the F key, mid-game too; saved at once.
/// </summary>
public sealed partial class EffectsViewModel : ViewModelBase
{
    private readonly ISettingsService? _settings;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOff), nameof(IsSubtle), nameof(IsFull), nameof(Tooltip), nameof(LevelName))]
    private EffectsLevel _level = EffectsLevel.Full;

    public EffectsViewModel(ISettingsService? settings = null)
    {
        _settings = settings;
        Refresh();
    }

    /// <summary>Raised after the level changed (the analysis re-reads the atari alert).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<EffectsLevel> Levels { get; } = [EffectsLevel.Off, EffectsLevel.Subtle, EffectsLevel.Full];

    public bool IsOff
    {
        get => Level == EffectsLevel.Off;
        set
        {
            if (value)
            {
                Level = EffectsLevel.Off;
            }
        }
    }

    public bool IsSubtle
    {
        get => Level == EffectsLevel.Subtle;
        set
        {
            if (value)
            {
                Level = EffectsLevel.Subtle;
            }
        }
    }

    public bool IsFull
    {
        get => Level == EffectsLevel.Full;
        set
        {
            if (value)
            {
                Level = EffectsLevel.Full;
            }
        }
    }

    public string LevelName => Name(Level);

    public string Tooltip => Tr.F("Effects.Tip", LevelName);

    public static string Name(EffectsLevel level) => level switch
    {
        EffectsLevel.Off => Tr.T("Effects.Off"),
        EffectsLevel.Subtle => Tr.T("Effects.Subtle"),
        _ => Tr.T("Effects.Full"),
    };

    /// <summary>F: Full → Subtle → Off → Full.</summary>
    [RelayCommand]
    private void Cycle() => Level = Level switch
    {
        EffectsLevel.Full => EffectsLevel.Subtle,
        EffectsLevel.Subtle => EffectsLevel.Off,
        _ => EffectsLevel.Full,
    };

    [RelayCommand]
    private void Select(EffectsLevel level) => Level = level;

    /// <summary>Re-reads the settings (Preferences may have changed them).</summary>
    public void Refresh()
    {
        _loading = true;
        Level = (_settings?.Current ?? new AppSettings()).Effects;
        _loading = false;
    }

    partial void OnLevelChanged(EffectsLevel value)
    {
        if (_loading)
        {
            return;
        }

        if (_settings is not null && _settings.Current.Effects != value)
        {
            _settings.Save(_settings.Current with { Effects = value });
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
