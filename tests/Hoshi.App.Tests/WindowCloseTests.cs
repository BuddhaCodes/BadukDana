using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;

namespace Hoshi.App.Tests;

/// <summary>Done and Escape close the secondary windows (they used to do nothing: the command was bound too early).</summary>
public sealed class WindowCloseTests
{
    private static (PreferencesWindow Window, PreferencesViewModel Vm, TestSettings Settings) Preferences()
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { Engines = [new EngineEntry("GNU Go", "/usr/games/gnugo", "--mode gtp")] });
        var themes = new ThemeService(settings);
        themes.ApplyCurrent(Application.Current!.Resources);
        var host = new FakeGtpHost();
        host.Refresh(settings.Current);
        var vm = new PreferencesViewModel(themes, settings, engines: host);
        var window = new PreferencesWindow { DataContext = vm, Width = 900, Height = 680 };
        window.Show();
        return (window, vm, settings);
    }

    private static void Click(Window window, string name)
    {
        Button button = window.FindControl<Button>(name)!;
        button.Command.Should().NotBeNull($"{name} must have a command");
        button.Command!.CanExecute(button.CommandParameter).Should().BeTrue();
        button.Command.Execute(button.CommandParameter);
    }

    [AvaloniaFact]
    public void Done_closes_the_preferences_and_keeps_what_was_typed()
    {
        (PreferencesWindow window, PreferencesViewModel vm, TestSettings settings) = Preferences();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        vm.KataGoExecutable = "/opt/katago/katago";
        vm.AnalysisVisits = 400;
        vm.EngineItems.Single(i => !i.IsBuiltIn).Arguments = "--mode gtp --level 8";

        Click(window, "DoneButton");

        closed.Should().BeTrue();
        settings.Current.KataGoExecutable.Should().Be("/opt/katago/katago");
        settings.Current.AnalysisVisits.Should().Be(400);
        settings.Current.Engines.Single().Arguments.Should().Be("--mode gtp --level 8");
    }

    [AvaloniaFact]
    public void Escape_closes_the_preferences()
    {
        (PreferencesWindow window, _, _) = Preferences();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        closed.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Done_stays_open_on_the_engines_tab_when_the_engine_list_cannot_be_saved()
    {
        (PreferencesWindow window, PreferencesViewModel vm, TestSettings settings) = Preferences();
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        vm.AddEngineCommand.Execute(null);
        vm.EngineItems.Last().Name = "GNU Go";

        Click(window, "DoneButton");

        closed.Should().BeFalse();
        window.FindControl<TabControl>("Tabs")!.SelectedItem.Should().BeSameAs(window.FindControl<TabItem>("EnginesTab"));
        vm.EnginesStatus.Should().Be("Ya hay otro motor con ese nombre.");
        settings.Current.Engines.Should().ContainSingle();
        window.Close();
    }

    [AvaloniaFact]
    public void Escape_closes_the_lobby_and_the_game_library()
    {
        var lobby = new LobbyWindow { DataContext = new LobbyViewModel(new FakeOgsClient(), new ImmediateDispatcher()) };
        lobby.Show();
        bool lobbyClosed = false;
        lobby.Closed += (_, _) => lobbyClosed = true;
        lobby.CloseCommand.Execute(null);
        lobbyClosed.Should().BeTrue();

        var replays = new ReplaysWindow();
        replays.Show();
        bool replaysClosed = false;
        replays.Closed += (_, _) => replaysClosed = true;
        replays.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        replaysClosed.Should().BeTrue();
    }
}
