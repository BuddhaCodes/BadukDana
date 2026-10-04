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
            ThemeService.Apply(HoshiThemes.WarmMinimal, animations: false, Application.Current!.Resources);
            var vm = new MainWindowViewModel(new GameViewModel(), ui: new ImmediateDispatcher());
            var window = new MainWindow(vm) { Width = 1100, Height = 720 };
            window.Show();
            await Task.Delay(50);

            HoshiTitleBar bar = window.GetVisualDescendants().OfType<HoshiTitleBar>().Single();
            bar.IsVisible.Should().BeTrue();
            window.ExtendClientAreaToDecorationsHint.Should().BeTrue("the bar replaces the system one");
            bar.GetVisualDescendants().OfType<TextBlock>().Should().Contain(t => t.Text == window.Title);

            Button maximize = bar.GetVisualDescendants().OfType<Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == "Maximize");
            maximize.Command.Should().BeNull();
            maximize.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Maximized);
            Avalonia.Automation.AutomationProperties.GetName(maximize).Should().Be("Restore");
            maximize.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.WindowState.Should().Be(WindowState.Normal);

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
        }
        finally
        {
            HoshiTitleBar.AlwaysShow = false;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
    }
}
