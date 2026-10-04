using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.App.Services.Music;
using Hoshi.Core.Localization;

namespace Hoshi.App.ViewModels;

/// <summary>
/// The speaker button in the toolbar: one click mutes or unmutes everything (effects and music); its panel adjusts the
/// effects volume, stone sounds and the music. Saved in the settings (shared with Preferences → Analysis).
/// </summary>
public sealed partial class AudioViewModel : ViewModelBase
{
    private readonly ISettingsService? _settings;
    private readonly IMusicService? _player;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tooltip), nameof(MuteLabel))]
    private bool _isMuted;

    [ObservableProperty]
    private int _soundVolume;

    [ObservableProperty]
    private bool _stoneSounds;

    [ObservableProperty]
    private bool _music;

    [ObservableProperty]
    private int _musicVolume;

    public AudioViewModel(ISettingsService? settings = null, IMusicService? music = null)
    {
        _settings = settings;
        _player = music;
        Refresh();
    }

    public string Tooltip => Tr.T(IsMuted ? "Audio.TipMuted" : "Audio.Tip");

    public string MuteLabel => Tr.T(IsMuted ? "Audio.Unmute" : "Audio.Mute");

    /// <summary>Why music cannot play on this machine, or null.</summary>
    public string? MusicProblem => _player?.Problem;

    /// <summary>Re-reads the settings (Preferences may have changed them); called when the panel opens.</summary>
    public void Refresh()
    {
        AppSettings s = _settings?.Current ?? new AppSettings();
        _loading = true;
        IsMuted = s.Muted;
        SoundVolume = s.SoundVolume;
        StoneSounds = s.StoneSounds;
        Music = s.Music;
        MusicVolume = s.MusicVolume;
        _loading = false;
        OnPropertyChanged(nameof(MusicProblem));
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnIsMutedChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Save();
        if (value)
        {
            _player?.Stop();
        }
        else if (Music)
        {
            _player?.Start();
        }
    }

    partial void OnSoundVolumeChanged(int value) => Save();

    partial void OnStoneSoundsChanged(bool value) => Save();

    partial void OnMusicChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Save();
        if (value && !IsMuted)
        {
            _player?.Start();
        }
        else if (!value)
        {
            _player?.Stop();
        }

        OnPropertyChanged(nameof(MusicProblem));
    }

    partial void OnMusicVolumeChanged(int value)
    {
        if (_loading)
        {
            return;
        }

        _player?.SetVolume(Math.Clamp(value, 0, 100) / 100.0);
        Save();
    }

    private void Save()
    {
        if (_loading || _settings is null)
        {
            return;
        }

        _settings.Save(_settings.Current with
        {
            Muted = IsMuted,
            SoundVolume = Math.Clamp(SoundVolume, 0, 100),
            StoneSounds = StoneSounds,
            Music = Music,
            MusicVolume = Math.Clamp(MusicVolume, 0, 100),
        });
    }
}
