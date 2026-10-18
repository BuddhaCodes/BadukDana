using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Hoshi.App.Controls;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Tests;

public sealed class TitleBarTests
{
    [AvaloniaFact]
    public async Task Every_window_gets_hoshis_title_bar_with_working_buttons()
    {
        HoshiTitleBar.AlwaysShow = true;
        try
        {
            ThemeService.Apply(HoshiThemes.Mochi, animations: false, Application.Current!.Resources);
            var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher());
            var window = new MainWindow(vm) { Width = 1100, Height = 720 };
            window.Show();
            await Task.Delay(50);

            // The header is the main window's title bar: Hoshi's thin bar hides and the caption buttons sit in the header.
            HoshiTitleBar bar = window.GetVisualDescendants().OfType<HoshiTitleBar>().Single();
            bar.IsVisible.Should().BeFalse("the header already is the title bar");
            HoshiTitleBar.GetIsMerged(window).Should().BeTrue();
            window.ExtendClientAreaToDecorationsHint.Should().BeTrue("the header replaces the system title bar");
            Border header = window.FindControl<Border>("AppHeader")!;
            HoshiTitleBar.GetIsDragArea(header).Should().BeTrue();
            CaptionButtons caption = header.GetVisualDescendants().OfType<CaptionButtons>().Single();
            caption.IsVisible.Should().BeTrue();

            Button maximize = caption.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Maximize");
            maximize.Command.Should().BeNull();
            maximize.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Maximized);
            Avalonia.Automation.AutomationProperties.GetName(maximize).Should().Be("Restore");
            maximize.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Normal);

            // Zen hides the header: the thin title bar comes back so the window can still be moved and closed.
            vm.IsZen = true;
            await Task.Delay(30);
            bar.IsVisible.Should().BeTrue();
            bar.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == window.Title);
            vm.IsZen = false;
            await Task.Delay(30);
            bar.IsVisible.Should().BeFalse();

            string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
            Directory.CreateDirectory(dir);
            using (WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "title-bar.png"));
            }

            var prefs = new PreferencesWindow { DataContext = new PreferencesViewModel(new ThemeService()) };
            prefs.Show(window);
            await Task.Delay(30);
            HoshiTitleBar dialogBar = prefs.GetVisualDescendants().OfType<HoshiTitleBar>().Single();
            dialogBar.GetVisualDescendants().OfType<Button>()
                .Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Minimize").IsVisible
                .Should().BeFalse("a dialog of the main window does not minimise on its own");
            prefs.Close();
            window.Close();

            // The splash has no decorations: no bar cuts its round card, and nothing is extended.
            var splash = new SplashWindow();
            splash.Show();
            await Task.Delay(30);
            splash.GetVisualDescendants().OfType<HoshiTitleBar>().Single().IsVisible.Should().BeFalse();
            splash.ExtendClientAreaToDecorationsHint.Should().BeFalse();
            using (WriteableBitmap frame = splash.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
            {
                frame.Save(Path.Combine(dir, "splash.png"));
            }

            splash.Close();
        }
        finally
        {
            HoshiTitleBar.AlwaysShow = false;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
    }
}
