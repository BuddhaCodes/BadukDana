using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services;
using Hoshi.App.Services.Engines;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Engines.Gtp;

namespace Hoshi.App.Tests;

public sealed class EngineUiTests
{
    private static string Shots()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "engines");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Save(Window window, string name)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
        frame.Save(Path.Combine(Shots(), name));
    }

    [AvaloniaFact]
    public async Task Preferences_edit_save_and_test_the_engine_list()
    {
        var settings = new TestSettings();
        var host = new FakeGtpHost();
        var vm = new PreferencesViewModel(new ThemeService(settings), settings, engines: host);
        vm.HasEngineHost.Should().BeTrue();
        vm.EngineItems.Should().BeEmpty();
        vm.AnalysisEngineOptions.Should().ContainSingle().Which.Id.Should().BeNull();

        vm.AddEngineCommand.Execute(null);
        vm.SelectedEngineItem!.Name.Should().Be("Motor nuevo");
        vm.SelectedEngineItem.Name = "Leela Zero";
        vm.SelectedEngineItem.Executable = "\"/opt/leela/leelaz\"";
        vm.SelectedEngineItem.Arguments = "--gtp -w best.gz";
        vm.AddEngineCommand.Execute(null);
        vm.SelectedEngineItem!.Name = "Leela Zero";
        vm.SaveEnginesCommand.Execute(null);
        vm.EnginesStatus.Should().Be("Ya hay otro motor con ese nombre.");
        settings.Current.Engines.Should().BeEmpty();

        vm.SelectedEngineItem.Name = "GNU Go";
        vm.SelectedEngineItem.Executable = "/usr/games/gnugo";
        vm.SelectedEngineItem.Arguments = "--mode gtp";
        vm.SaveEnginesCommand.Execute(null);
        settings.Current.Engines.Should().Equal(
            new EngineEntry("Leela Zero", "/opt/leela/leelaz", "--gtp -w best.gz"),
            new EngineEntry("GNU Go", "/usr/games/gnugo", "--mode gtp"));
        host.Engines.Select(e => e.Name).Should().Equal("Leela Zero", "GNU Go");

        vm.AnalysisEngineOptions.Select(o => o.Label).Should().Equal("KataGo de Hoshi (motor de análisis)", "Leela Zero", "GNU Go");
        vm.SelectedAnalysisEngine = vm.AnalysisEngineOptions[1];
        settings.Current.AnalysisEngine.Should().Be("Leela Zero");

        vm.SelectedEngineItem = vm.EngineItems[0];
        await vm.TestGtpEngineCommand.ExecuteAsync(null);
        vm.EnginesStatus.Should().Be("Funciona: FakeZero 0.1, 11 comandos GTP, análisis con lz-analyze.");

        vm.SelectedEngineItem = vm.EngineItems[1];
        vm.RemoveEngineCommand.Execute(null);
        settings.Current.Engines.Should().ContainSingle().Which.Name.Should().Be("Leela Zero");
    }

    [AvaloniaFact]
    public async Task Preferences_engines_tab_is_rendered()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with
        {
            Engines = [new EngineEntry("Leela Zero", "/opt/leela/leelaz", "--gtp --noponder -w best-network.gz", "time_settings 0 5 1"), new EngineEntry("GNU Go", "/usr/games/gnugo", "--mode gtp --level 10")],
        });
        var themes = new ThemeService(settings);
        themes.ApplyCurrent(Application.Current!.Resources);
        var host = new FakeGtpHost();
        host.Refresh(settings.Current);
        var vm = new PreferencesViewModel(themes, settings, engines: host) { EnginesStatus = "Funciona: Leela Zero 0.17, 31 comandos GTP, análisis con lz-analyze." };
        var window = new PreferencesWindow { DataContext = vm, Width = 900, Height = 680 };
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedIndex = 3;
        await Task.Delay(60);
        window.FindControl<TextBox>("EngineArgsBox")!.Text.Should().Be("--gtp --noponder -w best-network.gz");
        Save(window, "preferences-engines.png");
        window.Close();
    }

    [AvaloniaFact]
    public async Task The_engine_bar_the_new_game_dialog_and_the_console_are_rendered()
    {
        ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
        EngineMatchViewModel.EngineVsEngineDelay = TimeSpan.Zero;
        var host = new FakeGtpHost("KataGo de Hoshi", "Leela Zero") { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        foreach (string m in new[] { "C3", "G3", "C7", "G7", "E5" })
        {
            host.Moves.Enqueue(m);
        }

        var game = new GameViewModel();
        var match = new EngineMatchViewModel(game, host, new ImmediateDispatcher());
        var vm = new MainWindowViewModel(game, ui: new ImmediateDispatcher(), engineMatch: match, engines: host);
        var options = new NewEngineGameViewModel(host.Engines) { Size = 9 };
        options.White = options.Players[1];
        await vm.StartEngineGameAsync(options);
        game.PlayCommand.Execute(new Hoshi.Core.Point(4, 2));
        var window = new MainWindow(vm) { Width = 1100, Height = 720 };
        window.Show();
        await Task.Delay(120);
        window.FindControl<Border>("EngineBar")!.IsVisible.Should().BeTrue();
        window.FindControl<TextBlock>("EngineStatusText")!.Text.Should().Be("KataGo de Hoshi está pensando…");
        Save(window, "engine-bar-thinking.png");

        host.Gate.SetResult();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (game.MoveNumber < 2 && clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(20);
        }

        game.MoveNumber.Should().Be(2);
        match.TogglePauseCommand.Execute(null);
        await Task.Delay(60);
        window.FindControl<Button>("EnginePause")!.Content.Should().Be("Continuar");
        Save(window, "engine-bar-paused.png");
        window.Close();

        var dialog = new NewEngineGameWindow { DataContext = new NewEngineGameViewModel(host.Engines) { Handicap = 2 } };
        dialog.Show();
        await Task.Delay(60);
        dialog.FindControl<Button>("StartButton")!.IsEnabled.Should().BeTrue();
        Save(dialog, "new-engine-game.png");
        dialog.Close();

        var console = new GtpConsoleViewModel(new ConsoleHost(), new ImmediateDispatcher());
        console.Rows.Should().HaveCount(7);
        var consoleWindow = new GtpConsoleWindow { DataContext = console };
        consoleWindow.Show();
        await Task.Delay(60);
        Save(consoleWindow, "gtp-console.png");
        consoleWindow.Close();
        ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
    }

    /// <summary>
    /// Hoshi's own KataGo over GTP, end to end (only with HOSHI_KATAGO_EXE and HOSHI_KATAGO_MODEL set; never in CI):
    /// KataGo plays both colours of a 9×9 game, then analyses through the GTP adapter.
    /// </summary>
    [AvaloniaFact]
    public async Task Real_KataGo_plays_itself_and_analyses_over_gtp()
    {
        if (Environment.GetEnvironmentVariable("HOSHI_KATAGO_EXE") is not { Length: > 0 } exe
            || Environment.GetEnvironmentVariable("HOSHI_KATAGO_MODEL") is not { Length: > 0 } model)
        {
            return;
        }

        string data = Path.Combine(Path.GetTempPath(), "hoshi-gtp-e2e");
        Directory.CreateDirectory(data);
        var settings = new TestSettings();
        settings.Save(settings.Current with { KataGoExecutable = exe, KataGoModel = model });
        await using var host = new GtpEngineHost(settings, baseDirectory: data, dataDirectory: data);
        EngineChoice kataGo = host.Engines.Should().ContainSingle().Which;
        kataGo.IsBuiltIn.Should().BeTrue();
        kataGo.Config.Arguments.Should().Contain("hoshi_gtp.cfg");

        ThemeService.Apply(HoshiThemes.NightSky, animations: false, Application.Current!.Resources);
        EngineMatchViewModel.EngineVsEngineDelay = TimeSpan.Zero;
        var game = new GameViewModel();
        var match = new EngineMatchViewModel(game, host, new Services.AvaloniaUiDispatcher());
        var vm = new MainWindowViewModel(game, ui: new Services.AvaloniaUiDispatcher(), engineMatch: match, engines: host);
        var options = new NewEngineGameViewModel(host.Engines) { Size = 9 };
        options.Black = options.Players[1];
        await vm.StartEngineGameAsync(options);
        var window = new MainWindow(vm) { Width = 1200, Height = 760 };
        window.Show();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (game.MoveNumber < 12 && match.Problem is null && match.Finished is null && clock.Elapsed < TimeSpan.FromMinutes(4))
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(50);
        }

        match.Problem.Should().BeNull();
        game.MoveNumber.Should().BeGreaterThanOrEqualTo(12);
        match.TogglePauseCommand.Execute(null);
        await Task.Delay(100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "real-katago-vs-katago.png");
        window.Close();

        var analysis = new GtpAnalysisEngine(host, kataGo);
        IReadOnlyList<Hoshi.Engines.KataGo.TurnAnalysis> r = await analysis.AnalyzeAsync(
            new Hoshi.Engines.KataGo.AnalysisQuery { Width = 9, Height = 9, Komi = 6.5, MaxVisits = 50 }, CancellationToken.None);
        r.Should().ContainSingle().Which.Ownership.Should().HaveCount(81);
        ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
    }

    /// <summary>A host with a recorded conversation, for the console screenshot.</summary>
    private sealed class ConsoleHost : IGtpEngineHost
    {
        public IReadOnlyList<EngineChoice> Engines { get; } = [new("KataGo de Hoshi", new GtpEngineConfig("KataGo de Hoshi", "/x/katago"), true)];

        public IReadOnlyList<ConsoleLine> ConsoleLines { get; } =
        [
            new("KataGo de Hoshi", GtpDirection.Log, "Iniciando KataGo de Hoshi…", new DateTime(2026, 10, 4, 18, 2, 11)),
            new("KataGo de Hoshi", GtpDirection.Sent, "genmove W", new DateTime(2026, 10, 4, 18, 2, 14)),
            new("KataGo de Hoshi", GtpDirection.Received, "= Q16", new DateTime(2026, 10, 4, 18, 2, 15)),
            new("KataGo de Hoshi", GtpDirection.Received, "", new DateTime(2026, 10, 4, 18, 2, 15)),
            new("KataGo de Hoshi", GtpDirection.Sent, "kata-analyze B 25 ownership true rootInfo true", new DateTime(2026, 10, 4, 18, 2, 16)),
            new("KataGo de Hoshi", GtpDirection.Received, "info move D4 visits 212 winrate 0.4712 scoreLead -0.6 prior 0.18 order 0 pv D4 Q4 C16", new DateTime(2026, 10, 4, 18, 2, 16)),
            new("KataGo de Hoshi", GtpDirection.Received, "? unknown command", new DateTime(2026, 10, 4, 18, 2, 20)),
        ];

        public event EventHandler<ConsoleLine>? ConsoleLineAdded
        {
            add { }
            remove { }
        }

        public event EventHandler? EnginesChanged
        {
            add { }
            remove { }
        }

        public EngineChoice? Find(string? id) => Engines.FirstOrDefault(e => e.Id == id);

        public Task<GtpEngine> AcquireAsync(EngineChoice engine, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopAsync(string id) => Task.CompletedTask;

        public void Refresh(AppSettings settings)
        {
        }
    }
}
