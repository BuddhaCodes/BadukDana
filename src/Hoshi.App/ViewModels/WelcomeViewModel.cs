using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// The first-start tour: a few cards on what Hoshi does and how (playing, the panels, problems, online), then the
/// rules lessons for beginners. Shown once; the menu opens it again.
/// </summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    private static readonly string[] Steps = ["Welcome.Play", "Welcome.Analysis", "Welcome.Study", "Welcome.Learn", "Welcome.Online"];

    private readonly ISettingsService? _settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Text), nameof(StepText), nameof(IsLast), nameof(IsFirst))]
    private int _step;

    [ObservableProperty]
    private bool _isOpen;

    public WelcomeViewModel(ISettingsService? settings = null)
    {
        _settings = settings;
        IsOpen = settings is not null && !settings.Current.WelcomeSeen;
    }

    /// <summary>Raised by "Learn the rules": the rules lessons should open.</summary>
    public event EventHandler? RulesRequested;

    public string Title => Tr.T(Steps[Step] + ".Title");

    /// <summary>The card's text; on phones "click" reads "tap".</summary>
    public string Text => Tr.T(Steps[Step] + (PlatformInfo.IsPhone && Tr.Has(Steps[Step] + ".Phone") ? ".Phone" : ".Text"));

    public string StepText => $"{Step + 1} / {Steps.Length}";

    public bool IsFirst => Step == 0;

    public bool IsLast => Step == Steps.Length - 1;

    [RelayCommand]
    private void Next()
    {
        if (IsLast)
        {
            Close();
        }
        else
        {
            Step++;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 0)
        {
            Step--;
        }
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
        if (_settings is not null && !_settings.Current.WelcomeSeen)
        {
            _settings.Save(_settings.Current with { WelcomeSeen = true });
        }
    }

    [RelayCommand]
    private void LearnRules()
    {
        Close();
        RulesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The menu's "Welcome tour".</summary>
    [RelayCommand]
    private void Open()
    {
        Step = 0;
        IsOpen = true;
    }
}
