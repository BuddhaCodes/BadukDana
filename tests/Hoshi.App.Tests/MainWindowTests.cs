using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hoshi.App.Tests;

public sealed class MainWindowTests
{
    private static readonly Color BgWindow = Color.Parse("#1E1E1E");

    [AvaloniaFact]
    public void Window_background_is_the_Bg_Window_token()
    {
        var window = new MainWindow(new MainWindowViewModel());
        window.Show();

        window.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(BgWindow);

        Application.Current!.TryGetResource("Bg.Window", null, out object? token).Should().BeTrue();
        ((SolidColorBrush)token!).Color.Should().Be(BgWindow);
    }

    [AvaloniaFact]
    public void Board_sits_on_tatami_next_to_a_dark_sidebar_and_is_saved_as_screenshot()
    {
        var window = new MainWindow(new MainWindowViewModel()) { Width = 1100, Height = 800 };
        window.Show();

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame rendered");

        // Around the board: Sabaki's tatami (greenish straw); the sidebar is Sabaki's #111.
        Color tatami = Pixels.Read(frame, 6, 6);
        tatami.G.Should().BeGreaterThan(tatami.B, "tatami is green-yellow straw");
        tatami.R.Should().BeInRange(120, 230);
        Pixels.Read(frame, 1100 - 20, 400).Should().Be(Color.Parse("#111111"));

        string outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, "phase0-main-window.png"));
    }

    [AvaloniaFact]
    public void Title_shows_the_file_name_and_unsaved_changes()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm);
        window.Show();

        window.Title.Should().Be("Sin título — Hoshi");

        vm.Game.PlayCommand.Execute(new Hoshi.Core.Point(3, 3));

        window.Title.Should().Be("Sin título * — Hoshi");
    }

    [Fact]
    public void Host_resolves_main_window_and_view_model()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddAppServices()
            .AddOgs(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .BuildServiceProvider(validateScopes: true);

        services.GetRequiredService<MainWindowViewModel>().Game.Should().BeSameAs(services.GetRequiredService<GameViewModel>());
        services.GetRequiredService<MainWindowViewModel>()
            .Should().BeSameAs(services.GetRequiredService<MainWindowViewModel>());
    }

    [AvaloniaFact]
    public void Edit_mode_turns_the_bar_into_Sabaki_s_edit_bar()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1100, Height = 800 };
        window.Show();

        vm.Game.ToggleEditModeCommand.Execute(null);

        Border bar = window.FindControl<Border>("BottomBar")!;
        bar.Classes.Should().Contain("edit");
        window.FindControl<StackPanel>("EditBar")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<Grid>("PlayerInfo")!.IsEffectivelyVisible.Should().BeFalse();
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Pixels.Read(frame, 400, 800 - 3).Should().Be(Color.Parse("#C4BD64"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "ui-edit-mode.png"));
    }
}
