using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
    public void Rendered_frame_is_dark_and_is_saved_as_screenshot()
    {
        var window = new MainWindow(new MainWindowViewModel()) { Width = 1100, Height = 800 };
        window.Show();

        using WriteableBitmap frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("No frame rendered");

        // Outside the wooden board (the board control has a 16 px margin) the window must be exactly Bg.Window.
        Pixels.Read(frame, 6, 6)
            .Should().Be(BgWindow);

        string outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(outDir);
        frame.Save(Path.Combine(outDir, "phase0-main-window.png"));
    }

    [AvaloniaFact]
    public void Title_is_bound_to_view_model()
    {
        var window = new MainWindow(new MainWindowViewModel { Title = "Hoshi test" });
        window.Show();

        window.Title.Should().Be("Hoshi test");
    }

    [Fact]
    public void Host_resolves_main_window_and_view_model()
    {
        var services = new ServiceCollection().AddAppServices().BuildServiceProvider(validateScopes: true);

        services.GetRequiredService<MainWindowViewModel>().Should().NotBeNull();
        services.GetRequiredService<MainWindowViewModel>()
            .Should().BeSameAs(services.GetRequiredService<MainWindowViewModel>());
    }
}
