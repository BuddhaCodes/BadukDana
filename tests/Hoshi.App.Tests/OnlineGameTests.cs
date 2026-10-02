using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core;
using Hoshi.Ogs;
using Hoshi.Ogs.Games;
using Hoshi.Sgf;

namespace Hoshi.App.Tests;

internal sealed class FakeOnlineGame(long gameId, long myPlayerId) : IOnlineGame
{
    public long GameId { get; } = gameId;

    public long MyPlayerId { get; } = myPlayerId;

    public DateTimeOffset ServerNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000);

    public List<string> Sent { get; } = [];

    public bool Connected { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler<OgsGameSnapshot>? GamedataReceived;

    public event EventHandler<OgsGameMove>? MoveReceived;

    public event EventHandler<OgsClock>? ClockChanged;

    public event EventHandler<OgsGamePhase>? PhaseChanged;

    public event EventHandler<IReadOnlyList<Point>>? RemovedStonesChanged;

    public event EventHandler<OgsGameResult>? GameEnded;

    public event EventHandler<OgsChatLine>? ChatReceived;

    public event EventHandler<IReadOnlyList<string>>? ChatRemoved;

    public event EventHandler<string>? ErrorReceived;

    public event EventHandler<int>? UndoRequested;

    public event EventHandler<int>? UndoAccepted;

    public void Connect() => Connected = true;

    public void Play(Point? point) => Sent.Add("move " + (point?.ToSgf() ?? ".."));

    public void Resign() => Sent.Add("resign");

    public void SendChat(string body) => Sent.Add("chat " + body);

    public void SendTranslatedChat(IReadOnlyDictionary<string, string> phrases) => Sent.Add("phrase " + phrases["en"]);

    public void RemoveChat(params string[] ids) => ChatRemoved?.Invoke(this, ids);

    public void RequestUndo() => Sent.Add("undo?");

    public void AcceptUndo() => Sent.Add("undo!");

    public void SetRemovedStones(IEnumerable<Point> stones, bool removed) =>
        Sent.Add($"removed {removed} {OgsGameParser.Encode(stones.OrderBy(p => p.Y).ThenBy(p => p.X))}");

    public void AcceptRemovedStones(IEnumerable<Point> allRemoved) =>
        Sent.Add("accept " + OgsGameParser.Encode(allRemoved.OrderBy(p => p.Y).ThenBy(p => p.X)));

    public void RejectRemovedStones() => Sent.Add("reject");

    public void Dispose() => Disposed = true;

    public void Gamedata(OgsGameSnapshot g) => GamedataReceived?.Invoke(this, g);

    public void Move(OgsGameMove m) => MoveReceived?.Invoke(this, m);

    public void Clock(OgsClock c) => ClockChanged?.Invoke(this, c);

    public void Phase(OgsGamePhase p) => PhaseChanged?.Invoke(this, p);

    public void Removed(params Point[] p) => RemovedStonesChanged?.Invoke(this, p);

    public void End(OgsGameResult r) => GameEnded?.Invoke(this, r);

    public void Chat(OgsChatLine l) => ChatReceived?.Invoke(this, l);

    public void Error(string e) => ErrorReceived?.Invoke(this, e);

    public void UndoRequest(int n) => UndoRequested?.Invoke(this, n);

    public void UndoAccept(int n) => UndoAccepted?.Invoke(this, n);
}

public sealed class OnlineGameViewModelTests
{
    private static readonly OgsUser Me = new(1001, "kuro_test", 25.4, false);
    private static readonly OgsUser Rival = new(2002, "shiro_test", 27.2, false);

    private readonly FakeOnlineGame _game = new(70000001, Me.Id);
    private readonly GameViewModel _board = new();
    private readonly OnlineGameViewModel _vm;

    public OnlineGameViewModelTests()
    {
        _vm = new OnlineGameViewModel(_game, _board, new ImmediateDispatcher());
        _vm.Connect();
    }

    private static OgsGameSnapshot Snapshot(params OgsGameMove[] moves) => new()
    {
        GameId = 70000001,
        Width = 9,
        Height = 9,
        Komi = 6.5,
        Black = Me,
        White = Rival,
        Moves = moves,
        TimeControl = new OgsTimeControl { System = "byoyomi", MainTime = 600, PeriodTime = 30, Periods = 5 },
    };

    [Fact]
    public void Gamedata_builds_the_tree_and_shows_the_last_move()
    {
        _game.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2))));

        _game.Connected.Should().BeTrue();
        _board.IsOnline.Should().BeTrue();
        _board.MoveNumber.Should().Be(2);
        _board.Board[new Point(2, 2)].Should().Be(Stone.White);
        _board.Tree.Info.BlackPlayer.Should().Be("kuro_test");
        _board.Tree.Info.Komi.Should().Be(6.5);
        _board.Title.Should().Be("OGS #70000001 · kuro_test vs shiro_test — Hoshi");
        _vm.MyColor.Should().Be(Stone.Black);
        _vm.IsMyTurn.Should().BeTrue();
        _vm.StatusText.Should().Be("Tu turno");
        _board.StatusText.Should().Be("Tu turno");
    }

    [Fact]
    public void A_click_sends_the_move_and_the_stone_appears_when_the_server_echoes_it()
    {
        _game.Gamedata(Snapshot());

        _board.PlayCommand.Execute(new Point(3, 2));

        _game.Sent.Should().Equal("move dc");
        _board.Board[new Point(3, 2)].Should().Be(Stone.Empty, "nothing is placed before the server confirms");
        _vm.IsSending.Should().BeTrue();
        _board.PlayCommand.Execute(new Point(5, 5));
        _game.Sent.Should().HaveCount(1, "a second click while sending is ignored");

        _game.Move(new(1, Stone.Black, new Point(3, 2)));

        _board.Board[new Point(3, 2)].Should().Be(Stone.Black);
        _vm.IsSending.Should().BeFalse();
        _vm.IsMyTurn.Should().BeFalse();
        _vm.StatusText.Should().Be("Turno del rival");
        _board.PlayCommand.Execute(new Point(5, 5));
        _game.Sent.Should().HaveCount(1, "it is the opponent's turn");
    }

    [Fact]
    public void Illegal_moves_are_not_sent()
    {
        _game.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2))));

        _board.PlayCommand.Execute(new Point(4, 4));

        _game.Sent.Should().BeEmpty();
        _vm.StatusText.Should().StartWith("Jugada ilegal");
    }

    [Fact]
    public void Opponent_moves_are_followed_unless_the_user_is_reviewing_earlier_moves()
    {
        _game.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2))));
        _game.Move(new(3, Stone.Black, new Point(6, 6)));
        _board.GoBackCommand.Execute(null);

        _game.Move(new(4, Stone.White, new Point(6, 2)));

        _board.MoveNumber.Should().Be(2, "the user was looking at move 2");
        _board.MainLineMoveCount.Should().Be(4);
        _vm.IsMyTurn.Should().BeTrue();
        _board.PlayCommand.Execute(new Point(0, 0));
        _game.Sent.Should().BeEmpty("a click while reviewing jumps to the last move instead of playing");
        _board.MoveNumber.Should().Be(4);
    }

    [Fact]
    public void Server_errors_unlock_the_board()
    {
        _game.Gamedata(Snapshot());
        _board.PlayCommand.Execute(new Point(3, 2));

        _game.Error("Illegal move");

        _vm.IsSending.Should().BeFalse();
        _vm.StatusText.Should().Be("Illegal move");
    }

    [Fact]
    public void Pass_and_resign_are_only_for_the_player_in_play_phase()
    {
        _game.Gamedata(Snapshot());

        _vm.PassCommand.Execute(null);
        _board.PassKeyCommand.CanExecute(null).Should().BeFalse("the P shortcut never passes online");
        _game.Move(new(1, Stone.Black, null));
        _vm.PassCommand.CanExecute(null).Should().BeFalse("not your turn");
        _vm.PassCommand.Execute(null);
        _vm.ResignCommand.Execute(null);

        _game.Sent.Should().Equal("move ..", "resign");
        _vm.StatusText.Should().Be("Solo puedes pasar en tu turno");
    }

    [Fact]
    public void Clocks_count_down_for_the_player_to_move()
    {
        _game.Gamedata(Snapshot());
        long start = _game.ServerNow.ToUnixTimeMilliseconds();
        _game.Clock(new OgsClock(70000001, Me.Id, Me.Id, Rival.Id, start, 0, new(15, 5, 30), new(600, 5, 30)));
        _vm.BlackClock.Should().Be("0:15 + 5×0:30");

        _game.ServerNow = _game.ServerNow.AddSeconds(8);
        _vm.Tick();

        _vm.BlackClock.Should().Be("0:07 + 5×0:30");
        _vm.WhiteClock.Should().Be("10:00 + 5×0:30");
        _vm.IsBlackClockLow.Should().BeTrue();
        _vm.IsWhiteClockLow.Should().BeFalse();
    }

    [Fact]
    public void Stone_removal_toggles_whole_groups_and_accepts_the_score()
    {
        _game.Gamedata(Snapshot(
            new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(0, 0)),
            new(3, Stone.Black, new Point(4, 5)), new(4, Stone.White, new Point(1, 0))));
        _game.Phase(OgsGamePhase.StoneRemoval);
        _vm.StatusText.Should().StartWith("Conteo");

        _board.PlayCommand.Execute(new Point(0, 0));
        _game.Sent.Should().Equal("removed True aaba");
        _game.Removed(new Point(0, 0), new Point(1, 0));
        _board.Markers.Should().Contain(new Markup(new Point(0, 0), MarkupKind.Cross));

        _board.PlayCommand.Execute(new Point(1, 0));
        _game.Sent[^1].Should().Be("removed False aaba", "clicking a dead group revives it");
        _vm.AcceptScoreCommand.Execute(null);
        _game.Sent[^1].Should().Be("accept aaba");

        _game.End(new OgsGameResult(Stone.Black, "12.5 points"));
        _vm.IsFinished.Should().BeTrue();
        _vm.StatusText.Should().Be("Ganan negras por 12.5 puntos");
        _board.Tree.Info.Result.Should().Be("B+12.5");
    }

    [Fact]
    public void Undo_requests_and_accepted_undo_truncate_the_game()
    {
        _game.Gamedata(Snapshot(new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 2))));

        _game.UndoRequest(1);
        _vm.IsUndoRequested.Should().BeTrue();
        _vm.AcceptUndoCommand.Execute(null);
        _game.UndoAccept(1);

        _game.Sent.Should().Equal("undo!");
        _board.MainLineMoveCount.Should().Be(1);
        _board.MoveNumber.Should().Be(1);
        _vm.IsMyTurn.Should().BeFalse();
    }

    [Fact]
    public void Chat_history_is_kept_once_translated_phrases_are_localized_and_removals_honoured()
    {
        _game.Gamedata(Snapshot());
        var hello = new OgsChatLine("1", "main", Rival.Id, "shiro_test", "Have a good game!", 0, DateTimeOffset.UnixEpoch,
            OgsChatKind.Translated, new Dictionary<string, string> { ["en"] = "Have a good game!", ["es"] = "¡Buena partida!" });
        _game.Chat(hello);
        _game.Chat(hello); // replayed on reconnect
        _game.Chat(new OgsChatLine("2", "spectator", 99, "kibitzer", "nice", 3, DateTimeOffset.UnixEpoch));

        _vm.ChatLines.Should().HaveCount(2);
        _vm.ChatLines[0].Body.Should().Be("¡Buena partida!");
        _vm.ChatLines[1].Header.Should().EndWith("espectador");
        _vm.IsChatEmpty.Should().BeFalse();

        _game.RemoveChat("2");
        _vm.ChatLines.Should().ContainSingle();
    }

    [Fact]
    public void Quick_phrases_go_out_translated_and_muting_hides_and_counts()
    {
        _game.Gamedata(Snapshot());
        _vm.SendQuickPhraseCommand.Execute(_vm.QuickPhrases[2]);
        _game.Sent.Should().Contain("phrase Thanks for the game!");
        _vm.QuickPhrases[2].Translations["es"].Should().Be("¡Gracias por la partida!");

        _vm.ToggleChatMuteCommand.Execute(null);
        _game.Chat(new OgsChatLine("9", "main", Rival.Id, "shiro_test", "hurry up", 4, DateTimeOffset.UnixEpoch));
        _vm.HiddenCount.Should().Be(1);
        _vm.MutedText.Should().Be("Chat silenciado · 1 mensaje(s) nuevo(s).");
        _vm.ToggleChatMuteCommand.Execute(null);
        _vm.HiddenCount.Should().Be(0);
    }

    [Fact]
    public void Chat_is_listed_and_sent()
    {
        _game.Gamedata(Snapshot());
        _game.Chat(new OgsChatLine("1", "main", Rival.Id, "shiro_test", "hola", 0, DateTimeOffset.UnixEpoch));

        _vm.ChatInput = "buena suerte";
        _vm.SendChatCommand.Execute(null);

        _vm.ChatLines.Should().ContainSingle().Which.Should().Match<ChatLineItem>(c => c.Username == "shiro_test" && c.Body == "hola" && !c.IsMine && c.MoveNumber == 0);
        _game.Sent.Should().Equal("chat buena suerte");
        _vm.ChatInput.Should().BeEmpty();
    }

    [Fact]
    public void Leaving_keeps_the_game_as_a_local_savable_tree()
    {
        _game.Gamedata(Snapshot(new OgsGameMove(1, Stone.Black, new Point(4, 4))));

        _vm.LeaveCommand.Execute(null);

        _game.Disposed.Should().BeTrue();
        _board.IsOnline.Should().BeFalse();
        _board.MoveNumber.Should().Be(1);
        _board.IsDirty.Should().BeTrue();
        _board.UndoCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Free_handicap_lets_black_place_several_stones()
    {
        _game.Gamedata(Snapshot([]) with { Handicap = 2, FreeHandicapPlacement = true });

        _board.PlayCommand.Execute(new Point(2, 2));
        _game.Move(new(1, Stone.Black, new Point(2, 2)));

        _vm.IsMyTurn.Should().BeTrue("black places the second handicap stone");
        _board.PlayCommand.Execute(new Point(6, 6));
        _game.Sent.Should().Equal("move cc", "move gg");
    }

    [Fact]
    public void Tree_uses_SGF_setup_for_fixed_handicap()
    {
        GameTree_for_handicap().Root.GetValues("AB").Should().Equal("cc", "gg");
    }

    private static Hoshi.Sgf.GameTree GameTree_for_handicap() =>
        OnlineGameViewModel.BuildTree(Snapshot([]) with
        {
            Handicap = 2,
            InitialBlack = [new Point(2, 2), new Point(6, 6)],
            InitialPlayer = Stone.White,
        });
}

public sealed class MainWindowOnlineTests
{
    [AvaloniaFact]
    public async Task Lobby_game_start_opens_the_game_on_the_board_with_clocks_and_chat()
    {
        var ogs = new FakeOgsClient { HasStoredSession = true };
        var lobby = new LobbyViewModel(ogs, new ImmediateDispatcher());
        await lobby.InitializeAsync();
        var game = new GameViewModel();
        var vm = new MainWindowViewModel(game, null, lobby, ogs, new ImmediateDispatcher());
        var window = new MainWindow(vm) { Width = 1100, Height = 760 };
        window.Show();

        await vm.OpenOnlineGameAsync(70000001);
        var online = (FakeOnlineGame)GetGame(vm.Online!);
        online.Gamedata(new OgsGameSnapshot
        {
            GameId = 70000001, Width = 9, Height = 9, Komi = 6.5,
            Black = FakeOgsClient.Me, White = FakeOgsClient.Rival,
            Moves = [new(1, Stone.Black, new Point(4, 4)), new(2, Stone.White, new Point(2, 6))],
            TimeControl = new OgsTimeControl { System = "byoyomi", MainTime = 600, PeriodTime = 30, Periods = 5 },
        });
        online.Clock(new OgsClock(70000001, FakeOgsClient.Me.Id, FakeOgsClient.Me.Id, FakeOgsClient.Rival.Id,
            online.ServerNow.ToUnixTimeMilliseconds(), 0, new(590, 5, 30), new(596, 5, 30)));
        online.Chat(new OgsChatLine("1", "main", FakeOgsClient.Rival.Id, "rival", "¡suerte!", 2, DateTimeOffset.UnixEpoch));
        online.Chat(new OgsChatLine("2", "main", FakeOgsClient.Me.Id, FakeOgsClient.Me.Username, "Gracias, igualmente", 2, DateTimeOffset.UnixEpoch));
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(20);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        ogs.OpenedGames.Should().Equal(70000001);
        window.FindControl<StackPanel>("OnlinePanel")!.IsEffectivelyVisible.Should().BeTrue();
        window.FindControl<TextBlock>("BlackClock")!.Text.Should().Be("9:50 + 5×0:30");
        window.FindControl<TextBox>("CommentBox")!.IsEffectivelyVisible.Should().BeFalse();
        window.FindControl<DockPanel>("ChatPanel")!.IsEffectivelyVisible.Should().BeTrue();
        vm.Online!.ChatLines.Should().HaveCount(2);
        window.FindControl<ListBox>("ChatList")!.ItemCount.Should().Be(2);
        window.Title.Should().StartWith("OGS #70000001");
        game.Board.Width.Should().Be(9);

        using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
        frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "phase5-online-game.png"));

        vm.Online!.LeaveCommand.Execute(null);
        vm.IsOnline.Should().BeFalse();
        window.FindControl<TextBox>("CommentBox")!.IsEffectivelyVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Opening_an_active_game_from_the_lobby_raises_GameStarted()
    {
        var ogs = new FakeOgsClient { HasStoredSession = true };
        ogs.Games.Add(new OgsActiveGame(42, "g", FakeOgsClient.Me, FakeOgsClient.Rival, 19, 19, 100, "play"));
        var lobby = new LobbyViewModel(ogs, new ImmediateDispatcher());
        await lobby.InitializeAsync();
        long? opened = null;
        lobby.GameStarted += (_, id) => opened = id;

        lobby.OpenGameCommand.Execute(lobby.ActiveGames[0]);

        opened.Should().Be(42);
    }

    private static IOnlineGame GetGame(OnlineGameViewModel vm) =>
        (IOnlineGame)typeof(OnlineGameViewModel)
            .GetField("_game", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(vm)!;
}
