using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Avalonia.Controls;
using Avalonia.Headless;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class ThemeTests
{
    [AvaloniaFact]
    public void Every_icon_set_has_every_icon_and_it_parses()
    {
        string[] keys = [.. IconSets.All["Lucide"].Keys];
        keys.Should().Contain(["First", "Previous", "Next", "Last", "Pass", "Undo", "Online", "Menu", "Settings", "Check"]);
        foreach ((string set, IReadOnlyDictionary<string, IconData> icons) in IconSets.All)
        {
            icons.Keys.Should().BeEquivalentTo(keys, "set {0} is complete", set);
            foreach (IconData icon in icons.Values)
            {
                icon.Geometry.Bounds.Width.Should().BeGreaterThan(0);
                icon.Size.Should().BeOneOf(24, 256);
            }
        }
    }

    [Fact]
    public void Themes_are_distinct_and_reference_existing_icon_sets()
    {
        HoshiThemes.All.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        HoshiThemes.All.Should().HaveCount(7);
        HoshiThemes.All.Where(t => t.IsLight).Should().Equal(HoshiThemes.Mochi, HoshiThemes.Kaya);
        HoshiThemes.All.Should().OnlyContain(t => IconSets.All.ContainsKey(t.IconSet));
        HoshiThemes.ById("nope").Should().BeSameAs(HoshiThemes.Default);
        HoshiThemes.Default.Id.Should().Be("mochi", "Mochi, the website's look, is the default since 2026-10-18");
        HoshiThemes.All.Where(t => t.Rounded).Should().ContainSingle().Which.Should().BeSameAs(HoshiThemes.Mochi);
    }

    [AvaloniaFact]
    public void Applying_a_theme_replaces_the_dynamic_resources()
    {
        var resources = new Avalonia.Controls.ResourceDictionary();

        ThemeService.Apply(HoshiThemes.ZenGarden, animations: true, resources);

        ((SolidColorBrush)resources["Bg.Bar"]!).Color.Should().Be(HoshiThemes.ZenGarden.Bar);
        resources["Theme.Background"].Should().Be(BackgroundKind.ZenSand);
        resources["Theme.Animations"].Should().Be(true);
        resources["Theme.Board"].Should().BeSameAs(HoshiThemes.ZenGarden.Board);
        resources["Icon.First"].Should().BeSameAs(IconSets.All["Lucide"]["First"]);
        ((FontFamily)resources["Font.UI"]!).Name.Should().Be("Zen Kaku Gothic New");
    }

    [AvaloniaFact]
    public void The_light_kaya_theme_switches_fluent_to_its_light_look_and_back()
    {
        var themes = new ThemeService();
        Avalonia.Styling.ThemeVariant? before = Application.Current!.RequestedThemeVariant;
        try
        {
            themes.Select(HoshiThemes.Kaya, animations: false);
            Application.Current.RequestedThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Light);
            ((SolidColorBrush)Application.Current.Resources["Good"]!).Color.Should().Be(HoshiThemes.Kaya.Good);

            themes.Select(HoshiThemes.NightSky, animations: false);
            Application.Current.RequestedThemeVariant.Should().Be(Avalonia.Styling.ThemeVariant.Dark);
        }
        finally
        {
            Application.Current.RequestedThemeVariant = before;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current.Resources);
        }
    }

    [Fact]
    public void Settings_round_trip_through_json_and_survive_corruption()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hoshi-settings-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new JsonSettingsService(path);
            settings.Current.Should().BeEquivalentTo(new AppSettings());
            var saved = new AppSettings
            {
                Theme = "zen",
                Animations = false,
                Engines = [new EngineEntry("Leela Zero", "/opt/leelaz", "--gtp -w \"my net.gz\"", "time_settings 0 5 1")],
                AnalysisEngine = "Leela Zero",
            };
            settings.Save(saved);

            new JsonSettingsService(path).Current.Should().BeEquivalentTo(saved);

            File.WriteAllText(path, "{ not json");
            new JsonSettingsService(path).Current.Should().BeEquivalentTo(new AppSettings());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void Preferences_apply_and_save_the_chosen_theme()
    {
        var store = new MemorySettings();
        var themes = new ThemeService(store);
        var vm = new PreferencesViewModel(themes);
        try
        {
            vm.Cards.Select(c => c.Name).Should().Equal("Mochi", "Estrella polar", "Kaya", "Tinta y oro", "Jardín zen", "Minimal cálido", "Alto contraste");
            vm.Selected.Theme.Should().BeSameAs(HoshiThemes.NightSky);

            vm.Selected = vm.Cards[3];
            vm.Animations = false;

            themes.Current.Should().BeSameAs(HoshiThemes.InkAndGold);
            store.Current.Should().Be(new AppSettings { Theme = "sumi", Animations = false });
            vm.Cards[3].IsSelected.Should().BeTrue();
            vm.Cards[0].IsSelected.Should().BeFalse();
            Application.Current!.TryGetResource("Theme.Id", null, out object? id).Should().BeTrue();
            id.Should().Be("sumi");
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
    }

    [AvaloniaFact]
    public void Preferences_window_shows_a_card_per_theme_and_is_saved_as_screenshot()
    {
        var vm = new PreferencesViewModel(new ThemeService(new MemorySettings()));
        var window = new Hoshi.App.Views.PreferencesWindow { DataContext = vm };
        window.Show();

        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "preferences.png"));
        window.FindDeep<Avalonia.Controls.ListBox>("ThemeList")!.ItemCount.Should().Be(HoshiThemes.All.Count);
        ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
    }

    [AvaloniaTheory]
    [InlineData(BackgroundKind.NightSky)]
    [InlineData(BackgroundKind.InkMist)]
    [InlineData(BackgroundKind.ZenSand)]
    [InlineData(BackgroundKind.Washi)]
    public void Backgrounds_render_and_only_animated_ones_run_a_timer(BackgroundKind kind)
    {
        var bg = new ThemeBackground { Kind = kind, Animate = true };
        var window = new Avalonia.Controls.Window { Width = 300, Height = 200, Content = bg };
        window.Show();

        bg.IsRunning.Should().Be(kind is BackgroundKind.NightSky or BackgroundKind.InkMist or BackgroundKind.ZenSand);
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        var colours = new HashSet<Color>();
        for (int x = 5; x < 300; x += 17)
        {
            for (int y = 5; y < 200; y += 13)
            {
                colours.Add(Pixels.Read(frame, x, y));
            }
        }

        colours.Count.Should().BeGreaterThan(3, "the backdrop has texture, not a flat fill");
        bg.Animate = false;
        bg.IsRunning.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Playing_a_stone_animates_it_but_jumping_through_the_game_does_not()
    {
        var game = new GameViewModel();
        var board = new GoBoardControl { Animate = true, BoardStyle = HoshiThemes.NightSky.Board };
        var window = new Avalonia.Controls.Window { Width = 400, Height = 400, Content = board };
        window.Show();
        void Sync()
        {
            board.Board = game.Board;
            board.LastMove = game.LastMove;
        }

        Sync();
        game.PlayCommand.Execute(new Point(3, 3));
        Sync();
        board.AnimatingPoint.Should().Be(new Point(3, 3));

        game.PlayCommand.Execute(new Point(15, 15));
        game.PlayCommand.Execute(new Point(15, 3));
        game.GoFirstCommand.Execute(null);
        Sync();
        game.GoLastCommand.Execute(null);
        Sync();
        board.AnimatingPoint.Should().NotBe(new Point(15, 3), "jumping three moves is not a placement");
    }

    private sealed class MemorySettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public void Save(AppSettings settings) => Current = settings;
    }

    [AvaloniaFact]
    public void Mochi_rounds_the_corners_and_brings_the_pastel_background()
    {
        try
        {
            IResourceDictionary r = Application.Current!.Resources;
            ThemeService.Apply(HoshiThemes.Mochi, animations: false, r);
            r["Radius.Control"].Should().Be(new CornerRadius(10));
            r["Radius.Card"].Should().Be(new CornerRadius(18));
            r["ControlCornerRadius"].Should().Be(new CornerRadius(10), "Fluent's own controls follow");
            r["Theme.Background"].Should().Be(BackgroundKind.Mochi);
            r["Font.Title"].Should().Be(new Avalonia.Media.FontFamily("avares://Hoshi.App/Assets/Fonts/Fredoka#Fredoka"));
            r["Icon.Trash"].Should().NotBeNull();

            ThemeService.Apply(HoshiThemes.NightSky, animations: false, r);
            r["Radius.Control"].Should().Be(new CornerRadius(3));
            r["ControlCornerRadius"].Should().Be(new CornerRadius(4));
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
        }
    }

    [AvaloniaFact]
    public void Every_icon_stays_inside_its_box()
    {
        // Guards the generator: a path joined after another must not start with a relative "m" (Lucide's "x"
        // once drew as "<" and its eraser grew a stray line).
        foreach (string set in new[] { "PhosphorLight", "PhosphorRegular", "Lucide", "Tabler" })
        {
            foreach ((string key, IconData icon) in ThemeService.IconSet(set))
            {
                Rect b = icon.Geometry.Bounds;
                double slack = icon.Size * 0.05;
                (b.X >= -slack && b.Y >= -slack && b.Right <= icon.Size + slack && b.Bottom <= icon.Size + slack)
                    .Should().BeTrue($"{set}/{key} is drawn inside its {icon.Size}px box (bounds {b})");
            }
        }
    }
}
