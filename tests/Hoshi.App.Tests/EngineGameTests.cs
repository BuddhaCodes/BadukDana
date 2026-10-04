using System.Threading.Channels;
using Hoshi.App.Services;
using Hoshi.App.Services.Engines;
using Hoshi.App.ViewModels;
using Hoshi.Core;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;

namespace Hoshi.App.Tests;

/// <summary>A scripted GTP engine: each command gets the answer of a function (which may wait); analysis streams until interrupted.</summary>
internal sealed class ScriptedGtpProcess(Func<string, Task<string>> answer) : IEngineProcess
{
    private readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
    private readonly object _gate = new();
    private bool _streaming;

    public List<string> Received { get; } = [];

    public bool HasExited { get; private set; }

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_streaming)
            {
                _streaming = false;
                _out.Writer.TryWrite(string.Empty);
            }

            if (line.Length == 0)
            {
                return Task.CompletedTask;
            }

            lock (Received)
            {
                Received.Add(line);
            }

            if (line == "quit")
            {
                HasExited = true;
                _out.Writer.TryWrite(null);
                return Task.CompletedTask;
            }
        }

        _ = Task.Run(async () =>
        {
            string reply = await answer(line);
            lock (_gate)
            {
                if (reply.StartsWith("STREAM:", StringComparison.Ordinal))
                {
                    _out.Writer.TryWrite("=");
                    foreach (string l in reply["STREAM:".Length..].Split('|'))
                    {
                        _out.Writer.TryWrite(l);
                    }

                    _streaming = true;
                    return;
                }

                foreach (string l in reply.Split('\n'))
                {
                    _out.Writer.TryWrite(l);
                }

                _out.Writer.TryWrite(string.Empty);
            }
        });
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) => await _out.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        HasExited = true;
        _out.Writer.TryWrite(null);
        return ValueTask.CompletedTask;
    }

    public string[] Commands()
    {
        lock (Received)
        {
            return [.. Received];
        }
    }
}

/// <summary>Engines backed by <see cref="ScriptedGtpProcess"/>; genmove answers come from <see cref="Moves"/>.</summary>
internal sealed class FakeGtpHost : IGtpEngineHost
{
    private readonly Dictionary<string, GtpEngine> _running = [];

    public FakeGtpHost(params string[] names)
    {
        Engines = [.. names.Select(n => new EngineChoice(n, new GtpEngineConfig(n, "/fake/" + n), IsBuiltIn: false))];
    }

    public IReadOnlyList<EngineChoice> Engines { get; private set; }

    public IReadOnlyList<ConsoleLine> ConsoleLines => [];

    public Queue<string> Moves { get; } = new();

    /// <summary>When set, genmove waits for it.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Func<string, string>? Other { get; set; }

    public List<ScriptedGtpProcess> Processes { get; } = [];

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

    public void Refresh(AppSettings settings) => Engines = [.. settings.Engines.Select(e => new EngineChoice(e.Name, new GtpEngineConfig(e.Name, e.Executable), false))];

    public async Task<GtpEngine> AcquireAsync(EngineChoice engine, CancellationToken cancellationToken = default)
    {
        lock (_running)
        {
            if (_running.TryGetValue(engine.Id, out GtpEngine? running))
            {
                return running;
            }
        }

        var process = new ScriptedGtpProcess(Answer);
        Processes.Add(process);
        GtpEngine started = await GtpEngine.ConnectAsync(new GtpClient(process), engine.Config, cancellationToken: cancellationToken);
        lock (_running)
        {
            _running[engine.Id] = started;
        }

        return started;
    }

    public Task StopAsync(string id) => Task.CompletedTask;

    private async Task<string> Answer(string command)
    {
        string verb = command.Split(' ')[0];
        if (verb == "genmove")
        {
            if (Gate is { } gate)
            {
                await gate.Task;
            }

            lock (Moves)
            {
                return Moves.Count > 0 ? "= " + Moves.Dequeue() : "= pass";
            }
        }

        return Other?.Invoke(command) ?? verb switch
        {
            "list_commands" => "= protocol_version\nlist_commands\nname\nversion\nboardsize\nclear_board\nkomi\nplay\ngenmove\nundo\nlz-analyze",
            "name" => "= FakeZero",
            "version" => "= 0.1",
            _ => "=",
        };
    }
}

public sealed class EngineGameTests
{
    public EngineGameTests() => EngineMatchViewModel.EngineVsEngineDelay = TimeSpan.Zero;

    private static async Task Until(Func<bool> condition, string because)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeSpan.FromSeconds(5))
            {
                throw new TimeoutException("Timed out waiting until " + because);
            }

            await Task.Delay(10);
        }
    }

    private static (GameViewModel Game, EngineMatchViewModel Match, NewEngineGameViewModel Options) Setup(FakeGtpHost host)
    {
        var game = new GameViewModel();
        var match = new EngineMatchViewModel(game, host, new ImmediateDispatcher());
        var options = new NewEngineGameViewModel(host.Engines) { Size = 9 };
        return (game, match, options);
    }

    [Fact]
    public async Task The_engine_answers_your_moves_and_undo_takes_back_both()
    {
        var host = new FakeGtpHost("FakeZero");
        host.Moves.Enqueue("E5");
        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.Black = options.Players[0];
        options.White = options.Players[1];
        game.Load(options.CreateTree(), null);
        match.Start(options.Black.Engine, options.White.Engine);
        game.Tree.Info.WhitePlayer.Should().Be("FakeZero");
        game.Tree.Info.Komi.Should().Be(6.5);

        game.PlayCommand.Execute(new Point(2, 2));
        await Until(() => game.MoveNumber == 2, "the engine replied");
        game.Board[new Point(4, 4)].Should().Be(Stone.White);
        host.Processes[0].Commands().Should().ContainInOrder("boardsize 9", "clear_board", "komi 6.5", "play B C7", "genmove W");
        match.StatusText.Should().BeNull("nobody is thinking: your turn");

        game.UndoCommand.Execute(null);
        game.MoveNumber.Should().Be(0, "undo takes back the engine's reply and your move");
    }

    [Fact]
    public async Task While_the_engine_thinks_clicks_and_passes_are_ignored_and_going_back_abandons_the_question()
    {
        var host = new FakeGtpHost("FakeZero") { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        host.Moves.Enqueue("E5");
        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.White = options.Players[1];
        game.Load(options.CreateTree(), null);
        match.Start(null, options.White.Engine);

        game.PlayCommand.Execute(new Point(2, 2));
        await Until(() => host.Processes.Count == 1 && host.Processes[0].Commands().Contains("genmove W"), "the engine was asked");
        match.Thinker.Should().NotBeNull();
        game.StatusText.Should().Be("FakeZero está pensando…");
        game.PlayCommand.Execute(new Point(6, 6));
        game.MoveNumber.Should().Be(1, "it is the engine's turn");
        game.PassCommand.CanExecute(null).Should().BeFalse();
        game.GhostStone.Should().Be(Stone.Empty);

        game.GoBackCommand.Execute(null);
        match.Thinker.Should().BeNull("going back abandons the question");
        host.Gate.SetResult();
        await Task.Delay(100);
        game.MoveNumber.Should().Be(0);
        game.CurrentNode.Children.Should().HaveCount(1, "the late answer is not played");
    }

    [Fact]
    public async Task Two_engines_play_each_other_until_both_pass()
    {
        var host = new FakeGtpHost("FakeZero", "OtherGo");
        foreach (string m in new[] { "C3", "G7", "pass", "pass" })
        {
            host.Moves.Enqueue(m);
        }

        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.Black = options.Players[1];
        options.White = options.Players[2];
        options.Hint.Should().Contain("entre sí");
        game.Load(options.CreateTree(), null);
        match.Start(options.Black.Engine, options.White.Engine);
        match.IsEngineVsEngine.Should().BeTrue();

        await Until(() => game.IsGameOver, "both passed");
        game.MoveNumber.Should().Be(4);
        game.Board[new Point(2, 6)].Should().Be(Stone.Black);
        game.Board[new Point(6, 2)].Should().Be(Stone.White);
        host.Processes.Should().HaveCount(2, "one process per engine");
        game.PlayCommand.Execute(new Point(4, 4));
        game.MoveNumber.Should().Be(4, "both colours belong to engines");
    }

    [Fact]
    public async Task A_resigning_engine_ends_the_game_with_the_result()
    {
        var host = new FakeGtpHost("FakeZero");
        host.Moves.Enqueue("resign");
        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.White = options.Players[1];
        game.Load(options.CreateTree(), null);
        match.Start(null, options.White.Engine);
        game.PlayCommand.Execute(new Point(4, 4));
        await Until(() => match.Finished is not null, "the engine resigned");
        game.Tree.Info.Result.Should().Be("B+R");
        game.StatusText.Should().Be("FakeZero abandona");
    }

    [Fact]
    public async Task With_handicap_the_engine_as_white_moves_first_and_new_games_stop_the_match()
    {
        var host = new FakeGtpHost("FakeZero");
        host.Moves.Enqueue("E5");
        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.White = options.Players[1];
        options.Handicap = 2;
        options.Komi.Should().Be(0.5);
        game.Load(options.CreateTree(), null);
        game.Board.ToMove.Should().Be(Stone.White);
        match.Start(null, options.White.Engine);
        await Until(() => game.MoveNumber == 1, "white opened");
        host.Processes[0].Commands().Should().Contain(["play B G7", "play B C3"]).And.Contain("genmove W");
        game.Board[new Point(4, 4)].Should().Be(Stone.White);

        game.NewGameCommand.Execute(19);
        await Until(() => !match.IsActive, "the match ended");
        game.Opponent.Should().BeNull();
    }

    [Fact]
    public async Task An_engine_that_fails_pauses_the_match_with_its_message()
    {
        var host = new FakeGtpHost("FakeZero");
        host.Other = c => c.StartsWith("play", StringComparison.Ordinal) ? "? illegal move"
            : c.StartsWith("list_commands", StringComparison.Ordinal) ? "= play\ngenmove" : "=";
        (GameViewModel game, EngineMatchViewModel match, NewEngineGameViewModel options) = Setup(host);
        options.White = options.Players[1];
        game.Load(options.CreateTree(), null);
        match.Start(null, options.White.Engine);
        game.PlayCommand.Execute(new Point(4, 4));
        await Until(() => match.Problem is not null, "the error arrived");
        match.IsPaused.Should().BeTrue();
        match.StatusText.Should().Contain("illegal move");

        match.TogglePauseCommand.Execute(null);
        match.Problem.Should().BeNull("resuming tries again");
    }

    [Fact]
    public async Task Gtp_analysis_streams_until_it_has_the_visits()
    {
        var host = new FakeGtpHost("LeelaLike")
        {
            Other = c => c.StartsWith("lz-analyze", StringComparison.Ordinal)
                ? "STREAM:info move D4 visits 8 winrate 6000 prior 500 order 0 pv D4|info move D4 visits 40 winrate 6100 prior 500 order 0 pv D4 Q16"
                : null!,
        };
        var adapter = new GtpAnalysisEngine(host, host.Engines[0]);
        var updates = new List<TurnAnalysis>();
        IReadOnlyList<TurnAnalysis> result = await adapter.AnalyzeLiveAsync(
            new AnalysisQuery
            {
                Width = 19,
                Height = 19,
                Komi = 6.5,
                Moves = [new EngineMove(Stone.Black, new Point(15, 3))],
                MaxVisits = 30,
            },
            a => updates.Add(a),
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().ContainSingle();
        result[0].Turn.Should().Be(1);
        result[0].ToMove.Should().Be(Stone.White);
        result[0].Visits.Should().Be(40);
        result[0].Winrate.Should().BeApproximately(0.39, 1e-9, "61% for White to move");
        updates.Should().HaveCount(2);
        host.Processes[0].Commands().Should().Contain("lz-analyze W 25");
    }

    [Fact]
    public void The_dialog_offers_you_and_every_engine_with_sensible_handicaps_and_komi()
    {
        var options = new NewEngineGameViewModel(new FakeGtpHost("A", "B").Engines);
        options.Players.Select(p => p.Label).Should().Equal("Tú", "A", "B");
        options.White.Label.Should().Be("A", "the first engine plays White by default");
        options.CanStart.Should().BeTrue();
        options.Handicaps.Should().Equal(0, 2, 3, 4, 5, 6, 7, 8, 9);
        options.Handicap = 9;
        options.Size = 9;
        options.Handicap.Should().Be(9, "9×9 allows 9 too");
        options.Size = 13;
        options.Rules = options.RulesOptions.First(r => r.Rules == RuleSet.Chinese);
        options.Handicap = 0;
        options.Komi.Should().Be(RuleSet.Chinese.DefaultKomi);

        options.White = options.Players[0];
        options.CanStart.Should().BeFalse();
        new NewEngineGameViewModel([]).Hint.Should().Contain("no hay motores");
    }
}
