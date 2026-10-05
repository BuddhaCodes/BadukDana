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

    [AvaloniaFact]
    public async Task Joseki_screenshots_are_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        string data = Directory.CreateTempSubdirectory("hoshi-site-joseki").FullName;
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        try
        {
            ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
            var game = new GameViewModel();
            var library = new Services.Joseki.JosekiLibrary(dataDirectory: data);
            var hints = new JosekiAssistantViewModel(game, library, new Hoshi.Ogs.Joseki.JosekiExplorer(new FakeExplorer())) { IsOn = true };
            var trainer = new JosekiTrainerViewModel(library, game, explorer: new Hoshi.Ogs.Joseki.JosekiExplorer(new FakeExplorer()), random: new Random(5));
            var vm = new MainWindowViewModel(game, ui: new ImmediateDispatcher(), joseki: trainer, josekiHints: hints);
            foreach (string m in new[] { "D4", "Q4", "Q16" })
            {
                game.PlayCommand.Execute(Point.FromHuman(m, 19));
            }

            await hints.Pending;
            var window = new MainWindow(vm) { Width = 1400, Height = 880 };
            window.Show();
            await Settle();
            Save(window, Path.Combine(dir, "joseki-hints.png"));

            vm.OpenJosekiCommand.Execute(null);
            Hoshi.Sgf.Joseki.JosekiDrill drill = trainer.Drill!;
            for (int i = 0; i < 5; i++)
            {
                trainer.PlayCommand.Execute(drill.Expected!.Value);
            }

            trainer.PlayCommand.Execute(new Point(9, 3));
            await Settle();
            Save(window, Path.Combine(dir, "joseki-trainer.png"));
            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
            Directory.Delete(data, recursive: true);
        }

        static async Task Settle()
        {
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(20);
            }
        }

        static void Save(Avalonia.Controls.Window window, string path)
        {
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            frame.Save(path);
        }
    }

    [AvaloniaFact]
    public async Task Engine_game_screenshot_is_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        EngineMatchViewModel.EngineVsEngineDelay = TimeSpan.Zero;
        try
        {
            ThemeService.Apply(HoshiThemes.InkAndGold, animations: false, Application.Current!.Resources);
            var host = new FakeGtpHost("KataGo", "Leela Zero") { HoldWhenEmpty = true };
            foreach ((int x, int y) in Opening)
            {
                host.Moves.Enqueue(new Point(x, y).ToHuman(19));
            }

            var game = new GameViewModel();
            var match = new EngineMatchViewModel(game, host, new ImmediateDispatcher());
            var analysis = new FakeAnalysisEngine { BestMove = new Point(13, 13) };
            var vm = new MainWindowViewModel(game, engine: analysis, ui: new ImmediateDispatcher(), engineMatch: match, engines: host);
            var options = new NewEngineGameViewModel(host.Engines);
            options.Black = options.Players[1];
            options.White = options.Players[2];
            await vm.StartEngineGameAsync(options);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (game.MoveNumber < Opening.Length && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(20);
            }

            game.MoveNumber.Should().Be(Opening.Length);
            var window = new MainWindow(vm) { Width = 1400, Height = 880 };
            window.Show();
            vm.Analysis.IsAnalysisOn = true;
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(20);
            }

            match.Thinker.Should().NotBeNull();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            frame.Save(Path.Combine(dir, "engines.png"));
            window.Close();
            match.Stop();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }

    [AvaloniaFact]
    public void Weak_groups_screenshot_is_rendered()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
        try
        {
            // A white group short of eyes inside Black's wall (contested), and a black stone lost inside White's area.
            (int X, int Y, Hoshi.Core.Stone S, double Own)[] stones =
            [
                (2, 1, Hoshi.Core.Stone.Black, 0.85), (3, 1, Hoshi.Core.Stone.Black, 0.85), (1, 2, Hoshi.Core.Stone.Black, 0.85),
                (1, 3, Hoshi.Core.Stone.Black, 0.85), (2, 4, Hoshi.Core.Stone.Black, 0.8), (4, 3, Hoshi.Core.Stone.Black, 0.7),
                (2, 2, Hoshi.Core.Stone.White, 0.05), (3, 2, Hoshi.Core.Stone.White, 0.05), (2, 3, Hoshi.Core.Stone.White, 0.05),
                (5, 5, Hoshi.Core.Stone.White, -0.85), (7, 5, Hoshi.Core.Stone.White, -0.85), (6, 7, Hoshi.Core.Stone.White, -0.85),
                (6, 6, Hoshi.Core.Stone.Black, -0.8), (5, 6, Hoshi.Core.Stone.White, -0.85),
            ];
            Hoshi.Core.BoardState state = Hoshi.Core.BoardState.Create(9).Setup([.. stones.Select(s => (new Point(s.X, s.Y), s.S))]);
            double[] own = new double[81];
            foreach (var st in stones)
            {
                own[(st.Y * 9) + st.X] = st.Own;
            }

            var board = new Hoshi.App.Controls.GoBoardControl
            {
                Board = state,
                BoardStyle = HoshiThemes.NightSky.Board,
                ShowCoordinates = false,
                GroupStatuses = Hoshi.Core.GroupStrength.Assess(state, own),
                GroupPulseTime = 1.3,
                Margin = new Thickness(18),
            };
            var window = new Avalonia.Controls.Window
            {
                Width = 480,
                Height = 480,
                Content = new Avalonia.Controls.Panel { Children = { new Hoshi.App.Controls.ThemeBackground { Kind = BackgroundKind.NightSky, Animate = false }, board } },
            };
            window.Show();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            frame.Save(Path.Combine(dir, "weak-groups.png"));
            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
    }

    [AvaloniaFact]
    public async Task Board_and_stones_screenshot_is_rendered_in_English()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "site");
        Directory.CreateDirectory(dir);
        Tr.SetLanguage(Tr.English);
        try
        {
            var themes = new ThemeService();
            themes.ApplyCurrent(Application.Current!.Resources);
            var vm = new PreferencesViewModel(themes);
            vm.SelectedBoard = vm.BoardOptions.First(o => o.Id == "kaya-itame");
            vm.SelectedStones = vm.StoneOptions.First(o => o.Id == "clam-slate");
            vm.SelectedBackground = vm.BackgroundOptions.First(o => o.Id == "sashiko");
            var window = new PreferencesWindow { DataContext = vm, Width = 900, Height = 600 };
            window.Show();
            Avalonia.Controls.ControlExtensions.FindControl<Avalonia.Controls.TabControl>(window, "Tabs")!.SelectedIndex = 1;
            await Task.Delay(60);
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            frame.Save(Path.Combine(dir, "board-prefs.png"));
            window.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }
}
