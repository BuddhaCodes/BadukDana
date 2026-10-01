using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>Renders the English screenshots used by the promotional page (docs/site).</summary>
[Collection("Language")]
public sealed class SiteScreenshotTests
{
    private static readonly (int X, int Y)[] Opening =
        [(15, 3), (3, 15), (16, 15), (3, 3), (14, 16), (2, 5), (5, 2), (16, 9), (9, 15), (13, 2), (15, 13), (2, 13), (9, 3), (11, 15)];

    [AvaloniaFact]
    public async Task Promotional_screenshots_are_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        try
        {
            foreach ((HoshiTheme theme, string name, bool analysis, bool territory) in new[]
            {
                (HoshiThemes.NightSky, "night-analysis.png", true, false),
                (HoshiThemes.ZenGarden, "zen-territory.png", false, true),
                (HoshiThemes.InkAndGold, "ink.png", false, false),
                (HoshiThemes.WarmMinimal, "warm.png", false, false),
            })
            {
                ThemeService.Apply(theme, animations: false, Application.Current!.Resources);
                var engine = new FakeAnalysisEngine { BestMove = new Point(13, 13) };
                var vm = new MainWindowViewModel(new GameViewModel(), engine: territory ? null : engine, ui: new ImmediateDispatcher());
                foreach ((int x, int y) in Opening)
                {
                    vm.Game.PlayCommand.Execute(new Point(x, y));
                }

                var window = new MainWindow(vm) { Width = 1400, Height = 880 };
                window.Show();
                vm.Analysis.IsAnalysisOn = analysis;
                vm.Analysis.IsTerritoryOn = territory;
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(20);
                }

                using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
                frame.Save(Path.Combine(dir, name));
                window.Close();
            }
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }
}
