using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

public sealed class SkinTests
{
    [Fact]
    public void Without_choices_the_theme_board_is_unchanged()
    {
        Skins.Compose(HoshiThemes.NightSky.Board, null, null).Should().BeSameAs(HoshiThemes.NightSky.Board);
    }

    [Fact]
    public void A_goban_and_stones_replace_only_their_part_of_the_theme()
    {
        BoardStyle theme = HoshiThemes.InkAndGold.Board;
        BoardStyle s = Skins.Compose(theme, Skins.Board("walnut"), Skins.StoneSet("yunzi"));

        s.Texture.Should().EndWith("Boards/walnut.jpg");
        s.Lines.Should().Be(Skins.Board("walnut")!.Lines, "dark wood gets light lines");
        s.StoneSet.Should().Be("yunzi");
        s.WhiteVariants.Should().Be(4);
        s.Effect.Should().Be(theme.Effect, "the theme keeps its placement effect");
        s.BoardShadow.Should().Be(theme.BoardShadow);
    }

    [Fact]
    public void Vector_stones_clear_the_sprite_set()
    {
        BoardStyle s = Skins.Compose(HoshiThemes.NightSky.Board, null, Skins.StoneSet("shudan"));

        s.StoneSet.Should().BeNull();
        s.Stones.Should().Be(StoneStyle.Shudan);
    }

    [Fact]
    public void Classic_can_change_only_its_stones()
    {
        BoardStyle s = Skins.Compose(HoshiThemes.Classic.Board, null, Skins.StoneSet("clam-slate"));

        s.ShudanTexture.Should().BeFalse();
        s.Texture.Should().Be("avares://Hoshi/Assets/Sabaki/board.png", "Classic keeps Shudan's board");
        s.StoneSet.Should().Be("clam-slate");
    }

    [Fact]
    public void Ids_are_unique_and_every_shipped_skin_has_a_name()
    {
        Skins.Boards.Select(b => b.Id).Should().OnlyHaveUniqueItems();
        Skins.Stones.Select(b => b.Id).Should().OnlyHaveUniqueItems();
        Skins.Backgrounds.Select(b => b.Id).Should().OnlyHaveUniqueItems();
        foreach (string key in Skins.Boards.Select(b => b.NameKey).Concat(Skins.Stones.Select(s => s.NameKey)).Concat(Skins.Backgrounds.Select(b => b.NameKey)))
        {
            Hoshi.Core.Localization.Tr.T(key).Should().NotBe(key, $"{key} needs a translation");
        }
    }

    [AvaloniaFact]
    public void All_the_art_is_shipped_and_loads()
    {
        foreach (BoardSkin b in Skins.Boards.Where(b => b.Texture is not null))
        {
            Skins.Image(b.Texture).Should().NotBeNull(b.Id);
        }

        foreach (BackgroundSkin b in Skins.Backgrounds.Where(b => b.Tile is not null))
        {
            Skins.Image(b.Tile).Should().NotBeNull(b.Id);
        }

        foreach (StoneSkin s in Skins.Stones.Where(s => s.Set is not null))
        {
            for (int x = 0; x < 19; x++)
            {
                for (int y = 0; y < 19; y++)
                {
                    Skins.StoneSprite(s.Set!, true, s.BlackVariants, x, y).Should().NotBeNull(s.Id);
                    Skins.StoneSprite(s.Set!, false, s.WhiteVariants, x, y).Should().NotBeNull(s.Id);
                }
            }
        }
    }

    [AvaloniaFact]
    public void A_stone_keeps_its_variant_but_neighbours_differ()
    {
        var first = Skins.StoneSprite("clam-slate", false, 8, 3, 3);
        Skins.StoneSprite("clam-slate", false, 8, 3, 3).Should().BeSameAs(first);
        Enumerable.Range(0, 19).Select(x => Skins.StoneSprite("clam-slate", false, 8, x, 3)).Distinct().Count().Should().BeGreaterThan(3);
    }

    [AvaloniaFact]
    public void Choices_are_saved_and_restored()
    {
        var settings = new MemorySettings();
        var themes = new ThemeService(settings);
        themes.SelectSkins(Skins.Board("bamboo"), Skins.StoneSet("jade"), Skins.Background("sashiko"));

        settings.Current.BoardSkin.Should().Be("bamboo");
        settings.Current.StoneSkin.Should().Be("jade");
        settings.Current.BackgroundSkin.Should().Be("sashiko");
        var again = new ThemeService(settings);
        again.EffectiveBoard.StoneSet.Should().Be("jade");
        again.EffectiveBackground.Should().Be(BackgroundKind.Sashiko);
    }

    [AvaloniaFact]
    public void Preferences_offer_the_skins_and_apply_them_at_once()
    {
        var settings = new MemorySettings();
        var themes = new ThemeService(settings);
        var vm = new PreferencesViewModel(themes, settings);

        vm.SelectedBoard.Id.Should().BeNull("the theme's own by default");
        vm.SelectedStones = vm.StoneOptions.First(o => o.Id == "glass");
        vm.PreviewStyle.StoneSet.Should().Be("glass");
        vm.StoneOptions.First(o => o.Id == "glass").HasPreview2.Should().BeTrue();

        vm.ResetSkinsCommand.Execute(null);
        settings.Current.StoneSkin.Should().BeNull();
    }

    private static readonly (int X, int Y, Stone S)[] Position =
    [
        (3, 3, Stone.Black), (15, 15, Stone.White), (15, 3, Stone.Black), (3, 15, Stone.White), (2, 5, Stone.Black), (5, 2, Stone.White),
        (16, 13, Stone.Black), (13, 16, Stone.White), (9, 9, Stone.Black), (10, 9, Stone.White), (9, 10, Stone.White), (8, 9, Stone.Black),
        (4, 3, Stone.White), (3, 4, Stone.Black), (14, 15, Stone.Black), (15, 14, Stone.White), (16, 4, Stone.White), (4, 16, Stone.Black),
    ];

    /// <summary>Renders every goban with every stone set (for looking at; not compared against anything).</summary>
    [AvaloniaFact]
    public void Gallery_is_rendered()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "skins");
        Directory.CreateDirectory(dir);
        BoardState board = BoardState.Create(19).Setup([.. Position.Select(p => (new Point(p.X, p.Y), p.S))]);
        int i = 0;
        foreach (BoardSkin b in Skins.Boards.Where(b => b.Texture is not null))
        {
            StoneSkin s = Skins.Stones[i++ % Skins.Stones.Count];
            BackgroundSkin bg = Skins.Backgrounds[(i + 2) % Skins.Backgrounds.Count];
            var panel = new Panel();
            panel.Children.Add(new ThemeBackground { Kind = bg.Kind, Animate = false });
            panel.Children.Add(new GoBoardControl
            {
                Board = board,
                BoardStyle = Skins.Compose(HoshiThemes.NightSky.Board, b, s),
                IsInteractive = false,
                Margin = new Thickness(40),
                LastMove = new Point(9, 10),
            });
            var window = new Window { Width = 720, Height = 720, Content = panel };
            window.Show();
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
            frame.Save(Path.Combine(dir, $"{b.Id}+{s.Id}+{bg.Id}.png"));
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Preferences_board_tab_is_rendered()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots", "skins");
        Directory.CreateDirectory(dir);
        var settings = new MemorySettings();
        var themes = new ThemeService(settings);
        themes.ApplyCurrent(Application.Current!.Resources);
        var vm = new PreferencesViewModel(themes, settings);
        vm.SelectedBoard = vm.BoardOptions.First(o => o.Id == "kaya-itame");
        vm.SelectedStones = vm.StoneOptions.First(o => o.Id == "clam-slate");
        vm.SelectedBackground = vm.BackgroundOptions.First(o => o.Id == "sashiko");
        var window = new PreferencesWindow { DataContext = vm, Width = 900, Height = 640 };
        window.Show();
        window.FindControl<TabControl>("Tabs")!.SelectedIndex = 1;
        await Task.Delay(50);
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
        frame.Save(Path.Combine(dir, "preferences-board.png"));
        window.Close();
        ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
    }

    private sealed class MemorySettings : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();

        public void Save(AppSettings settings) => Current = settings;
    }
}
