using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Phone;
using Hoshi.App.Themes;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;
using Hoshi.Sgf.Study;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Tests;

/// <summary>
/// Renders the footage of the promotional video (tools/promo) in English: the desktop at 2560×1440 and the phone at
/// 824×1784, both at twice-ish the UI size so the video can zoom in. Only runs with HOSHI_PROMO=1 (it takes a while
/// and writes a few hundred frames); the analysis numbers come from <see cref="PromoEngine"/>, a scripted stand-in (not KataGo): most moves near the best, two mistakes and a blunder.
/// </summary>
[Collection("Language")]
public sealed class PromoFootageTests
{
    private static readonly string[] Game =
        "Q16 D4 R4 D16 O3 C14 Q10 F17 C6 F3 Q13 J4 R6 L17 K16 K17 J16 J17 H16 C10 E15 D15 E16 C17 R14 Q15 P15 Q14 R15 P16 O16 P17 O17 P14 O14 P13 N13 O12 N12 O11 N11 M3 M4 L4 M5 F5 E5 F6 D6 E7"
            .Split(' ');

    private static string Dir(string sub)
    {
        string dir = Path.Combine(Environment.GetEnvironmentVariable("HOSHI_PROMO_OUT") ?? Path.Combine(AppContext.BaseDirectory, "promo"), sub);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("HOSHI_PROMO") == "1";

    [AvaloniaFact]
    public async Task Desktop_footage()
    {
        if (!Enabled)
        {
            return;
        }

        Tr.SetLanguage(Tr.English);
        try
        {
            // The game unfolding, one frame per move (Mochi).
            ThemeService.Apply(HoshiThemes.Mochi, animations: false, Application.Current!.Resources);
            (Window w, MainWindowViewModel vm) = Desktop(null);
            string seq = Dir("desk-seq");
            for (int i = 0; i <= Game.Length; i++)
            {
                if (i > 0)
                {
                    vm.Game.PlayCommand.Execute(Point.FromHuman(Game[i - 1], 19));
                }

                await Settle(4);
                Save(w, Path.Combine(seq, $"{i:000}.png"));
            }

            w.Close();

            // Every theme, same position.
            foreach (HoshiTheme theme in HoshiThemes.All)
            {
                ThemeService.Apply(theme, animations: false, Application.Current!.Resources);
                (w, vm) = Desktop(null, moves: 36);
                await Settle();
                Save(w, Path.Combine(Dir("desk"), $"theme-{theme.Id}.png"));
                w.Close();
            }

            // Review with the analysis (Mochi and Pole star), study with drawings, territory.
            foreach (HoshiTheme theme in new[] { HoshiThemes.Mochi, HoshiThemes.NightSky })
            {
                ThemeService.Apply(theme, animations: false, Application.Current!.Resources);
                var engine = new PromoEngine();
                (w, vm) = Desktop(engine, moves: 30);
                vm.Analysis.IsAnalysisOn = true;
                await Settle();
                vm.SetModeCommand.Execute(WorkMode.Review);
                await Settle();
                Save(w, Path.Combine(Dir("desk"), $"review-{theme.Id}.png"));

                vm.SetModeCommand.Execute(WorkMode.Study);
                vm.Study.SelectColorCommand.Execute(StudyColor.Red);
                vm.Study.SelectToolCommand.Execute(StudyTool.Arrow);
                vm.Study.BoardDragCommand.Execute(new BoardDrag(Point.FromHuman("O12", 19), Point.FromHuman("R12", 19)));
                vm.Study.SelectColorCommand.Execute(StudyColor.Blue);
                vm.Study.SelectToolCommand.Execute(StudyTool.Area);
                vm.Study.BoardDragCommand.Execute(new BoardDrag(Point.FromHuman("M17", 19), Point.FromHuman("P14", 19)));
                vm.Study.SelectToolCommand.Execute(StudyTool.Label);
                vm.Study.BoardClickCommand.Execute(Point.FromHuman("K10", 19));
                vm.Study.BoardClickCommand.Execute(Point.FromHuman("K4", 19));
                vm.Study.SelectToolCommand.Execute(StudyTool.Arrow);
                await Settle();
                Save(w, Path.Combine(Dir("desk"), $"study-{theme.Id}.png"));
                w.Close();
            }

            ThemeService.Apply(HoshiThemes.Mochi, animations: false, Application.Current!.Resources);
            (w, vm) = Desktop(null, moves: 44);
            vm.Analysis.IsTerritoryOn = true;
            await Settle();
            Save(w, Path.Combine(Dir("desk"), "territory-mochi.png"));
            w.Close();

            var settings = new TestSettings();
            settings.Save(settings.Current with { UiScale = 1.6 });
            var prefs = new PreferencesWindow { DataContext = new PreferencesViewModel(new ThemeService(settings), settings), Width = 1500, Height = 1100 };
            prefs.Show();
            await Settle();
            Save(prefs, Path.Combine(Dir("desk"), "preferences.png"));
            prefs.Close();
        }
        finally
        {
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }

    [AvaloniaFact]
    public async Task Phone_footage()
    {
        if (!Enabled)
        {
            return;
        }

        bool wasPhone = PlatformInfo.IsPhone;
        PlatformInfo.IsPhone = true;
        Tr.SetLanguage(Tr.English);
        try
        {
            foreach (HoshiTheme theme in new[] { HoshiThemes.Mochi, HoshiThemes.NightSky, HoshiThemes.MochiNight })
            {
                ThemeService.Apply(theme, animations: false, Application.Current!.Resources);
                var engine = new PromoEngine();
                (Window w, PhoneShellViewModel shell) = Phone(engine, moves: 0);
                string seq = Dir($"phone-seq-{theme.Id}");
                shell.IsHome = true;
                await Settle();
                Save(w, Path.Combine(Dir("phone"), $"home-{theme.Id}.png"));
                shell.ContinueGameCommand.Execute(null);
                for (int i = 0; i <= 30; i++)
                {
                    if (i > 0)
                    {
                        shell.Main.Game.PlayCommand.Execute(Point.FromHuman(Game[i - 1], 19));
                    }

                    if (theme == HoshiThemes.Mochi || i == 30)
                    {
                        await Settle(4);
                        Save(w, Path.Combine(seq, $"{i:000}.png"));
                    }
                }

                // Tap and confirm: the waiting stone with its guide lines.
                shell.Main.Game.ConfirmMoves = true;
                shell.Main.Game.PlayCommand.Execute(Point.FromHuman("R12", 19));
                await Settle();
                Save(w, Path.Combine(Dir("phone"), $"confirm-{theme.Id}.png"));
                shell.Main.Game.CancelPendingCommand.Execute(null);
                shell.Main.Game.ConfirmMoves = false;

                shell.ShowSheetCommand.Execute(PhoneSheet.Analysis);
                await Settle(30);
                Save(w, Path.Combine(Dir("phone"), $"analysis-{theme.Id}.png"));
                shell.ShowSheetCommand.Execute(PhoneSheet.Study);
                shell.Main.Study.SelectColorCommand.Execute(StudyColor.Red);
                shell.Main.Study.SelectToolCommand.Execute(StudyTool.Arrow);
                shell.Main.Study.BoardDragCommand.Execute(new BoardDrag(Point.FromHuman("O12", 19), Point.FromHuman("R12", 19)));
                await Settle(30);
                Save(w, Path.Combine(Dir("phone"), $"study-{theme.Id}.png"));
                shell.ToggleSheetCommand.Execute(PhoneSheet.Menu);
                await Settle(30);
                Save(w, Path.Combine(Dir("phone"), $"more-{theme.Id}.png"));
                shell.ToggleSheetCommand.Execute(PhoneSheet.Menu);
                shell.Main.Analysis.IsTerritoryOn = true;
                await Settle();
                Save(w, Path.Combine(Dir("phone"), $"territory-{theme.Id}.png"));
                shell.Main.Analysis.IsTerritoryOn = false;
                shell.Main.ToggleZenCommand.Execute(null);
                await Settle();
                Save(w, Path.Combine(Dir("phone"), $"zen-{theme.Id}.png"));
                w.Close();
            }
        }
        finally
        {
            PlatformInfo.IsPhone = wasPhone;
            ThemeService.Apply(HoshiThemes.Default, animations: false, Application.Current!.Resources);
            Tr.SetLanguage(Tr.Spanish);
        }
    }

    private static (Window Window, MainWindowViewModel Vm) Desktop(IAnalysisEngine? engine, int moves = 0)
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { UiScale = 1.3333 });
        var game = new GameViewModel();
        game.Tree.Info.BlackPlayer = "Ana";
        game.Tree.Info.WhitePlayer = "Kenji";
        var vm = new MainWindowViewModel(game, engine: engine, ui: new ImmediateDispatcher(), settings: settings);
        foreach (string m in Game.Take(moves))
        {
            game.PlayCommand.Execute(Point.FromHuman(m, 19));
        }

        var window = new MainWindow(vm) { Width = 2560, Height = 1440 };
        window.Show();
        return (window, vm);
    }

    private static (Window Window, PhoneShellViewModel Shell) Phone(IAnalysisEngine engine, int moves)
    {
        var settings = new TestSettings();
        settings.Save(settings.Current with { UiScale = 2, ConfirmMoves = false });
        var game = new GameViewModel();
        game.Tree.Info.BlackPlayer = "Ana";
        game.Tree.Info.WhitePlayer = "Kenji";
        foreach (string m in Game.Take(moves))
        {
            game.PlayCommand.Execute(Point.FromHuman(m, 19));
        }

        var navigation = new PhoneNavigation();
        var main = new MainWindowViewModel(game, engine: engine, ui: new ImmediateDispatcher(), dialogs: navigation, preferencesWindow: navigation, settings: settings);
        var shell = new PhoneShellViewModel(main, navigation, settings);
        var window = new Window { Width = 824, Height = 1784, Content = new PhoneView { DataContext = shell } };
        window.Show();
        return (window, shell);
    }

    private static async Task Settle(int ticks = 20)
    {
        for (int i = 0; i < ticks; i++)
        {
            await Task.Delay(20);
        }
    }

    private static void Save(TopLevel window, string path)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame");
        frame.Save(path);
    }

    /// <summary>
    /// A scripted stand-in for KataGo in the promo footage: the game's own next move is the best one, most moves lose
    /// a fraction of a point, moves 12 and 21 are mistakes and move 27 is a blunder; ownership follows the nearest stones.
    /// </summary>
    private sealed class PromoEngine : IAnalysisEngine
    {
        private static readonly Dictionary<int, double> Losses = new() { [7] = 2.4, [12] = 4.5, [18] = 1.3, [21] = 6.0, [27] = 9.5 };

        public string? Problem => null;

        public int Visits => 400;

        public event EventHandler? Changed { add { } remove { } }

        private static double Loss(int move) => Losses.TryGetValue(move, out double l) ? l : 0;

        public Task<IReadOnlyList<Hoshi.Engines.KataGo.TurnAnalysis>> AnalyzeAsync(Hoshi.Engines.KataGo.AnalysisQuery query, CancellationToken cancellationToken)
        {
            IReadOnlyList<int> turns = query.Turns.Count == 0 ? [query.Moves.Count] : query.Turns;
            var list = turns.Select(t =>
            {
                double lead = 1.5;
                for (int m = 1; m <= t && m <= query.Moves.Count; m++)
                {
                    lead += query.Moves[m - 1].Color == Hoshi.Core.Stone.Black ? -Loss(m) : Loss(m);
                }

                var toMove = t % 2 == 0 ? Hoshi.Core.Stone.Black : Hoshi.Core.Stone.White;
                double wr = 1 / (1 + Math.Exp(-lead / 5));
                // The next move of the game is the best one, except for the scripted mistakes: there the engine
                // prefers another point and the played one is a candidate that loses the scripted points.
                Point? played = t < query.Moves.Count ? query.Moves[t].Point : null;
                Point alt = Point.FromHuman(t % 2 == 0 ? "R12" : "D10", 19);
                double sign = toMove == Hoshi.Core.Stone.Black ? 1 : -1;
                double WinFor(double l) => 1 / (1 + Math.Exp(-l / 5));
                var cands = new List<Hoshi.Engines.KataGo.MoveCandidate>();
                if (played is { } pl && Losses.ContainsKey(t + 1))
                {
                    double after = lead - (sign * Loss(t + 1));
                    cands.Add(new(alt, 0, 300, WinFor(lead), lead, 0.45, [alt]));
                    cands.Add(new(pl, 1, 60, WinFor(after), after, 0.12, [pl]));
                }
                else
                {
                    Point best = played ?? alt;
                    cands.Add(new(best, 0, 260, WinFor(lead), lead, 0.42, [best]));
                    cands.Add(new(alt == best ? Point.FromHuman("K10", 19) : alt, 1, 90, WinFor(lead - (sign * 1.6)), lead - (sign * 1.6), 0.18, []));
                }

                return new Hoshi.Engines.KataGo.TurnAnalysis(t, toMove, wr, lead, 400, cands, Ownership(query, t));
            }).ToList();
            return Task.FromResult<IReadOnlyList<Hoshi.Engines.KataGo.TurnAnalysis>>(list);
        }

        private static double[] Ownership(Hoshi.Engines.KataGo.AnalysisQuery q, int t)
        {
            var stones = q.Moves.Take(t).Where(m => m.Point is not null).Select(m => (P: m.Point!.Value, B: m.Color == Hoshi.Core.Stone.Black)).ToList();
            var own = new double[q.Width * q.Height];
            for (int y = 0; y < q.Height; y++)
            {
                for (int x = 0; x < q.Width; x++)
                {
                    double v = 0;
                    foreach ((Point p, bool b) in stones)
                    {
                        int d = Math.Abs(p.X - x) + Math.Abs(p.Y - y);
                        if (d <= 3)
                        {
                            v += (b ? 1 : -1) * (4 - d) / 4.0;
                        }
                    }

                    own[(y * q.Width) + x] = Math.Clamp(v, -1, 1);
                }
            }

            return own;
        }
    }
}
