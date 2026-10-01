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

    [AvaloniaFact]
    public void Window_background_follows_the_Bg_Window_token()
    {
        var window = new MainWindow(new MainWindowViewModel());
        window.Show();

        Application.Current!.TryGetResource("Bg.Window", null, out object? token).Should().BeTrue();
        window.Background.Should().BeOfType<SolidColorBrush>()
            .Which.Color.Should().Be(((SolidColorBrush)token!).Color);
    }

    public static TheoryData<string> ThemeIds => [.. Hoshi.App.Themes.HoshiThemes.All.Select(t => t.Id)];

    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void Every_theme_renders_its_backdrop_and_sidebar_and_is_saved_as_screenshot(string id)
    {
        Hoshi.App.Themes.HoshiTheme theme = Hoshi.App.Themes.HoshiThemes.ById(id);
        Hoshi.App.Themes.ThemeService.Apply(theme, animations: false, Application.Current!.Resources);
        try
        {
            var vm = new MainWindowViewModel();
            foreach (var p in new[] { (3, 3), (15, 15), (15, 3), (3, 15), (2, 5), (16, 13), (9, 9) })
            {
                vm.Game.PlayCommand.Execute(new Hoshi.Core.Point(p.Item1, p.Item2));
            }

            var window = new MainWindow(vm) { Width = 1100, Height = 800 };
            window.Show();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");

            Pixels.Read(frame, 1100 - 20, 400).Should().Be(theme.Sidebar, "the sidebar uses the theme colour");
            Color backdrop = Pixels.Read(frame, 6, 6);
            backdrop.Should().NotBe(theme.Sidebar, "the board sits on the theme's backdrop");
            if (theme.Background == Hoshi.App.Themes.BackgroundKind.Tatami)
            {
                backdrop.G.Should().BeGreaterThan(backdrop.B, "tatami is green-yellow straw");
            }

            string outDir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(outDir);
            frame.Save(Path.Combine(outDir, $"theme-{id}.png"));

            window.Width = 1600;
            window.Height = 900;
            using WriteableBitmap wide = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            wide.Save(Path.Combine(outDir, $"theme-{id}-wide.png"));
        }
        finally
        {
            Hoshi.App.Themes.ThemeService.Apply(Hoshi.App.Themes.HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
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

    [Fact]
    public void The_host_uses_the_desktop_lifetime_not_the_console_one()
    {
        // ConsoleLifetime blocks process exit until the host is disposed, which kept Hoshi alive after closing.
        using ServiceProvider services = new ServiceCollection().AddLogging().AddAppServices().BuildServiceProvider();
        services.GetRequiredService<Microsoft.Extensions.Hosting.IHostLifetime>().Should().BeOfType<Services.DesktopLifetime>();
    }

    [AvaloniaFact]
    public void Edit_mode_turns_the_bar_into_the_theme_s_edit_bar()
    {
        var vm = new MainWindowViewModel();
        var window = new MainWindow(vm) { Width = 1100, Height = 800 };
        window.Show();

        vm.Game.ToggleEditModeCommand.Execute(null);

        Border bar = window.FindControl<Border>("BottomBar")!;
        bar.Classes.Should().Contain("edit");
        window.FindControl<StackPanel>("EditBar")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<Grid>("PlayerInfo")!.IsEffectivelyVisible.Should().BeFalse();
        Application.Current!.TryGetResource("Bg.EditBar", null, out object? edit).Should().BeTrue();
        ((SolidColorBrush)edit!).Color.Should().Be(Hoshi.App.Themes.HoshiThemes.Default.EditBar);
    }
}
