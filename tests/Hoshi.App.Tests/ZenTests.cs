using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Phone;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Tests;

/// <summary>Zen mode: the board alone, a still background, quieter (or silent) effects.</summary>
[Collection("Language")]
public sealed class ZenTests
{
    [AvaloniaFact]
    public void Zen_mode_leaves_the_board_alone_on_the_desktop()
    {
        var settings = new TestSettings();
        var vm = new MainWindowViewModel(new GameViewModel(), settings: settings);
        var window = new MainWindow(vm) { Width = 1200, Height = 820 };
        window.Show();
        GoBoardControl board = window.FindDeep<GoBoardControl>("Board")!;
        double before = board.Bounds.Width;

        vm.ToggleZenCommand.Execute(null);
        window.UpdateLayout();
        window.FindDeep<Grid>("Sidebar")!.IsVisible.Should().BeFalse();
        window.FindDeep<Border>("BottomBar")!.IsVisible.Should().BeFalse();
        window.FindDeep<Button>("ZenExit")!.IsVisible.Should().BeTrue();
        window.FindDeep<ThemeBackground>("Backdrop")!.Paused.Should().BeTrue();
        board.Bounds.Width.Should().BeGreaterThan(before, "the board gets the sidebar's room");
        board.Effects.Should().Be(EffectsLevel.Subtle, "full effects are softened");

        vm.ToggleZenCommand.Execute(null);
        window.UpdateLayout();
        window.FindDeep<Grid>("Sidebar")!.IsVisible.Should().BeTrue();
        window.FindDeep<Border>("BottomBar")!.IsVisible.Should().BeTrue();
        board.Effects.Should().Be(EffectsLevel.Full);
        window.Close();
    }

    [AvaloniaFact]
    public void A_silent_zen_keeps_only_the_stones_click()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { ZenSilence = true });
        var zen = new ZenMode();
        var inner = new FakeSoundService();
        var sounds = new MutableSoundService(inner, settings, zen);
        var vm = new MainWindowViewModel(new GameViewModel(), settings: settings, sounds: sounds, zen: zen);

        vm.IsZen = true;
        zen.Silent.Should().BeTrue();
        vm.BoardEffects.Should().Be(EffectsLevel.Off);
        sounds.Play(SoundEffect.ExplosionBig, 1);
        sounds.Play(SoundEffect.CaptureSmall, 1);
        sounds.Play(SoundEffect.Stone, 1);
        inner.Played.Select(p => p.Effect).Should().Equal(SoundEffect.Stone);

        vm.IsZen = false;
        zen.Silent.Should().BeFalse();
        sounds.Play(SoundEffect.CaptureSmall, 1);
        inner.Played.Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void On_the_phone_zen_hides_the_bar_and_back_leaves_it()
    {
        var navigation = new PhoneNavigation();
        var main = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher(), dialogs: navigation, preferencesWindow: navigation);
        var shell = new PhoneShellViewModel(main, navigation);
        var window = new Window { Width = 400, Height = 860, Content = new PhoneView { DataContext = shell } };
        window.Show();
        shell.ToggleSheetCommand.Execute(PhoneSheet.Moves);

        main.ToggleZenCommand.Execute(null);
        shell.Sheet.Should().Be(PhoneSheet.None);
        window.FindDeep<Border>("PhoneBar")!.IsVisible.Should().BeFalse();
        window.FindDeep<Button>("PhoneZenExit")!.IsVisible.Should().BeTrue();

        shell.Back().Should().BeTrue();
        main.IsZen.Should().BeFalse();
        window.FindDeep<Border>("PhoneBar")!.IsVisible.Should().BeTrue();
        window.Close();
    }
}
