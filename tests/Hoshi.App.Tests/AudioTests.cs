using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services;
using Hoshi.App.Services.Music;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Engines.KataGo;

namespace Hoshi.App.Tests;

public sealed class AudioTests
{
    [Fact]
    public void Muting_silences_every_effect_and_stops_the_music_until_unmuted()
    {
        var settings = new TestSettings();
        var inner = new FakeSoundService();
        var sounds = new MutableSoundService(inner, settings);
        var music = new FakeMusic();
        var audio = new AudioViewModel(settings, music);
        audio.Music.Should().BeTrue("music is on by default");

        sounds.Play(SoundEffect.Stone, 0.5);
        inner.Played.Should().HaveCount(1);

        audio.ToggleMuteCommand.Execute(null);
        settings.Current.Muted.Should().BeTrue();
        music.Stops.Should().Be(1);
        audio.Tooltip.Should().Be("Sonido apagado");
        sounds.Play(SoundEffect.ExplosionBig, 1);
        inner.Played.Should().HaveCount(1, "nothing plays while muted");

        audio.ToggleMuteCommand.Execute(null);
        settings.Current.Muted.Should().BeFalse();
        music.Starts.Should().Be(1, "the music comes back when it was on");
        sounds.Play(SoundEffect.Stone, 0.5);
        inner.Played.Should().HaveCount(2);
    }

    [Fact]
    public void Volumes_and_music_are_saved_and_the_panel_rereads_the_settings()
    {
        var settings = new TestSettings();
        var music = new FakeMusic();
        var audio = new AudioViewModel(settings, music);

        audio.SoundVolume = 40;
        audio.MusicVolume = 20;
        audio.Music = false;
        settings.Current.SoundVolume.Should().Be(40);
        settings.Current.MusicVolume.Should().Be(20);
        settings.Current.Music.Should().BeFalse();
        music.Volume.Should().Be(0.2);
        music.Stops.Should().Be(1);

        audio.ToggleMuteCommand.Execute(null);
        audio.ToggleMuteCommand.Execute(null);
        music.Starts.Should().Be(0, "unmuting does not start music that was switched off");

        settings.Save(settings.Current with { SoundVolume = 90 }); // changed in Preferences
        audio.Refresh();
        audio.SoundVolume.Should().Be(90);
        music.Stops.Should().Be(2, "re-reading the settings does not start or stop anything");
    }

    [AvaloniaFact]
    public async Task The_sound_panel_opens_from_the_toolbar()
    {
        ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
        var settings = new TestSettings();
        var audio = new AudioViewModel(settings, new FakeMusic());
        var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher(), settings: settings, audio: audio);
        var window = new MainWindow(vm) { Width = 1100, Height = 720 };
        window.Show();
        Button button = window.FindControl<Button>("AudioButton")!;
        button.IsVisible.Should().BeTrue();

        button.Flyout!.ShowAt(button);
        await Task.Delay(80);
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(dir, "audio-panel.png"));
        }

        button.Flyout.Hide();
        window.Close();
    }

    private sealed class FakeMusic : IMusicService
    {
        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public double Volume { get; private set; }

        public bool IsPlaying { get; private set; }

        public string? Problem => null;

        public double Heat => 0;

        public void Start()
        {
            Starts++;
            IsPlaying = true;
        }

        public void Stop()
        {
            Stops++;
            IsPlaying = false;
        }

        public void SetVolume(double volume) => Volume = volume;

        public void OnVerdict(MoveQuality quality, int importance = 3, bool opening = false)
        {
        }

        public void OnBattle(double heat)
        {
        }
    }
}
