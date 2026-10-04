using System.Threading.Channels;
using Hoshi.Core;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Tests;

/// <summary>A scripted GTP engine: answers commands with a function; streams lines until interrupted by a newline.</summary>
internal sealed class FakeGtpProcess : IEngineProcess
{
    private readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
    private readonly Func<string, string?> _answer;
    private TaskCompletionSource? _streaming;

    public FakeGtpProcess(Func<string, string?> answer) => _answer = answer;

    public List<string> Received { get; } = [];

    public bool HasExited { get; private set; }

    public string? ExitReason { get; set; }

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        lock (Received)
        {
            if (_streaming is { } s)
            {
                _streaming = null;
                _out.Writer.TryWrite(string.Empty); // the newline ends the stream
                s.TrySetResult();
                if (line.Length == 0)
                {
                    return Task.CompletedTask;
                }
            }

            if (line.Length == 0)
            {
                return Task.CompletedTask;
            }

            Received.Add(line);
            if (line == "quit")
            {
                Exit();
                return Task.CompletedTask;
            }

            string? reply = _answer(line);
            if (reply is null)
            {
                return Task.CompletedTask;
            }

            if (reply.StartsWith("STREAM:", StringComparison.Ordinal))
            {
                _out.Writer.TryWrite("=");
                foreach (string l in reply["STREAM:".Length..].Split('|'))
                {
                    _out.Writer.TryWrite(l);
                }

                _streaming = new TaskCompletionSource();
                return Task.CompletedTask;
            }

            foreach (string l in reply.Split('\n'))
            {
                _out.Writer.TryWrite(l);
            }

            _out.Writer.TryWrite(string.Empty);
        }

        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) => await _out.Reader.ReadAsync(cancellationToken);

    public void Exit()
    {
        HasExited = true;
        _out.Writer.TryWrite(null);
    }

    public ValueTask DisposeAsync()
    {
        Exit();
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

public sealed class GtpTests
{
    private static string? Standard(string command) => command.Split(' ')[0] switch
    {
        "protocol_version" => "= 2",
        "list_commands" => "= protocol_version\nlist_commands\nname\nversion\nboardsize\nclear_board\nkomi\nplay\ngenmove\nundo\nlz-analyze\nkata-analyze\nkata-set-rules",
        "name" => "= KataGo",
        "version" => "= 1.16.3",
        "genmove" => "= Q16",
        "kata-analyze" => "STREAM:info move D4 visits 10 winrate 0.6 scoreLead 2.5 prior 0.3 order 0 pv D4 Q16",
        _ => "=",
    };

    private static async Task<(GtpEngine Engine, FakeGtpProcess Process)> Start(Func<string, string?>? answer = null, string init = "")
    {
        var process = new FakeGtpProcess(answer ?? Standard);
        var engine = await GtpEngine.ConnectAsync(new GtpClient(process), new GtpEngineConfig("Test", "/x/engine", "", init));
        return (engine, process);
    }

    private static GtpPosition Position(params EngineMove[] moves) => new()
    {
        Width = 19,
        Height = 19,
        Komi = 6.5,
        Moves = moves,
        ToMove = moves.Length % 2 == 0 ? Stone.Black : Stone.White,
    };

    [Fact]
    public async Task Responses_end_at_an_empty_line_and_skip_banners_and_ids()
    {
        var process = new FakeGtpProcess(c => c == "name" ? "Welcome banner\n=12 Leela Zero" : c == "bad" ? "? unknown command" : "= a\nb");
        await using var client = new GtpClient(process);
        (await client.SendAsync("name")).Should().Be(new GtpResponse(true, "Leela Zero"));
        (await client.SendAsync("bad")).Should().Be(new GtpResponse(false, "unknown command"));
        (await client.SendAsync("multi\tline\r")).Text.Should().Be("a\nb");
        process.Commands().Should().Contain("multi line");
    }

    [Fact]
    public async Task Handshake_reads_name_version_commands_and_runs_init_commands()
    {
        (GtpEngine engine, FakeGtpProcess process) = await Start(init: "time_settings 0 5 1\n# a comment\n\nkata-set-param maxVisits 100 # strength");
        await using (engine)
        {
            engine.DisplayName.Should().Be("KataGo 1.16.3");
            engine.AnalysisKind.Should().Be(GtpAnalysisKind.KataGo);
            process.Commands().Should().ContainInOrder("protocol_version", "list_commands", "name", "version", "time_settings 0 5 1", "kata-set-param maxVisits 100");
        }
    }

    [Fact]
    public async Task Positions_are_synced_incrementally_with_play_and_undo()
    {
        (GtpEngine engine, FakeGtpProcess process) = await Start();
        await using (engine)
        {
            var b1 = new EngineMove(Stone.Black, new Point(3, 3));
            var w1 = new EngineMove(Stone.White, new Point(15, 15));
            var b2 = new EngineMove(Stone.Black, null);
            int before = process.Commands().Length;
            await engine.SetPositionAsync(Position(b1));
            process.Commands()[before..].Should().Equal("boardsize 19", "clear_board", "komi 6.5", "kata-set-rules japanese", "play B D16");

            before = process.Commands().Length;
            await engine.SetPositionAsync(Position(b1, w1, b2));
            process.Commands()[before..].Should().Equal("play W Q4", "play B pass");

            before = process.Commands().Length;
            await engine.SetPositionAsync(Position(b1, new EngineMove(Stone.White, new Point(2, 2))));
            process.Commands()[before..].Should().Equal("undo", "undo", "play W C17");

            before = process.Commands().Length;
            await engine.SetPositionAsync(Position(b1) with { Komi = 7.5 });
            process.Commands()[before..].Should().StartWith(["boardsize 19", "clear_board", "komi 7.5"]);
        }
    }

    [Fact]
    public async Task Setup_stones_rectangular_boards_and_engines_without_undo()
    {
        (GtpEngine engine, FakeGtpProcess process) = await Start(c => c == "list_commands" ? "= play\ngenmove\nrectangular_boardsize" : "=");
        await using (engine)
        {
            var pos = new GtpPosition
            {
                Width = 9,
                Height = 7,
                InitialStones = [(new Point(2, 2), Stone.Black)],
                Moves = [new EngineMove(Stone.White, new Point(4, 4)), new EngineMove(Stone.Black, new Point(5, 5))],
            };
            await engine.SetPositionAsync(pos);
            process.Commands().Should().Contain(["rectangular_boardsize 9 7", "play B C5", "play W E3", "play B F2"]);
            process.Commands().Should().NotContain(c => c.StartsWith("kata-set-rules", StringComparison.Ordinal));

            int before = process.Commands().Length;
            await engine.SetPositionAsync(pos with { Moves = [pos.Moves[0]] });
            process.Commands()[before..].Should().StartWith(["rectangular_boardsize 9 7", "clear_board"], "without undo it starts again");
        }
    }

    [Fact]
    public async Task Genmove_returns_points_passes_and_resignations()
    {
        string reply = "= Q16";
        (GtpEngine engine, FakeGtpProcess process) = await Start(c => c.StartsWith("genmove", StringComparison.Ordinal) ? reply : Standard(c));
        await using (engine)
        {
            GtpMove move = await engine.GenMoveAsync(Position());
            move.Point.Should().Be(new Point(15, 3));
            process.Commands().Should().Contain("genmove B");

            // The engine already has its own move: the next position only adds the reply.
            int before = process.Commands().Length;
            reply = "= pass";
            (await engine.GenMoveAsync(Position(new EngineMove(Stone.Black, new Point(15, 3)), new EngineMove(Stone.White, new Point(3, 3))) with { ToMove = Stone.Black })).IsPass.Should().BeTrue();
            process.Commands()[before..].Should().Equal("play W D16", "genmove B");

            reply = "= resign";
            (await engine.GenMoveAsync(Position())).Resign.Should().BeTrue();

            reply = "? illegal";
            Func<Task> failing = () => engine.GenMoveAsync(Position());
            await failing.Should().ThrowAsync<EngineException>();
        }
    }

    [Fact]
    public async Task Analysis_streams_until_cancelled_and_other_commands_interrupt_it()
    {
        (GtpEngine engine, FakeGtpProcess process) = await Start();
        await using (engine)
        {
            var got = new TaskCompletionSource<TurnAnalysis>();
            using var cts = new CancellationTokenSource();
            Task analysis = engine.AnalyzeAsync(Position(new EngineMove(Stone.Black, new Point(3, 3))), 1, TimeSpan.FromSeconds(0.5), a => got.TrySetResult(a), cts.Token);
            TurnAnalysis first = await got.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first.ToMove.Should().Be(Stone.White);
            first.Winrate.Should().BeApproximately(0.4, 1e-9, "0.6 for White to move is 0.4 for Black");
            first.ScoreLead.Should().Be(-2.5);
            process.Commands().Should().Contain("kata-analyze W 50 ownership true rootInfo true");

            // A move request takes the engine over: the stream ends and genmove runs.
            GtpMove move = await engine.GenMoveAsync(Position(new EngineMove(Stone.Black, new Point(3, 3)))).WaitAsync(TimeSpan.FromSeconds(5));
            move.Point.Should().Be(new Point(15, 3));
            await analysis.WaitAsync(TimeSpan.FromSeconds(5));
            cts.Cancel();
        }
    }

    [Fact]
    public async Task A_dead_engine_fails_the_command_with_its_exit_reason()
    {
        var process = new FakeGtpProcess(_ => null) { ExitReason = "code 1: no model" };
        await using var client = new GtpClient(process);
        Task<GtpResponse> pending = client.SendAsync("genmove b");
        process.Exit();
        Func<Task> act = () => pending;
        (await act.Should().ThrowAsync<EngineException>()).Which.Message.Should().Contain("no model");
    }

    [Fact]
    public void Leela_analysis_lines_are_parsed_from_the_side_to_move()
    {
        const string line = "info move Q16 visits 120 winrate 5600 prior 2100 lcb 5500 order 0 pv Q16 D4 pass info move D4 visits 30 winrate 5200 prior 1900 lcb 5000 order 1 pv D4";
        TurnAnalysis a = GtpAnalysisParser.Parse(line, GtpAnalysisKind.Leela, 19, 19, Stone.Black, 0)!;
        a.Candidates.Should().HaveCount(2);
        a.Best!.Point.Should().Be(new Point(15, 3));
        a.Best.Winrate.Should().BeApproximately(0.56, 1e-9);
        a.Best.Prior.Should().BeApproximately(0.21, 1e-9);
        a.Best.Pv.Should().Equal(new Point(15, 3), new Point(3, 15), null);
        a.Visits.Should().Be(150);
        a.Ownership.Should().BeNull();

        TurnAnalysis w = GtpAnalysisParser.Parse(line, GtpAnalysisKind.Leela, 19, 19, Stone.White, 3)!;
        w.Winrate.Should().BeApproximately(0.44, 1e-9);
        w.Turn.Should().Be(3);
    }

    [Fact]
    public void KataGo_analysis_lines_bring_root_info_and_ownership()
    {
        string own = string.Join(' ', Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? "0.5" : "-1"));
        string line = $"info move A2 visits 8 utility 0.1 winrate 0.7 scoreMean 3 scoreLead 3.5 prior 0.4 order 0 pv A2 B1 pvVisits 8 3 rootInfo visits 9 winrate 0.65 scoreLead 3.1 ownership {own}";
        TurnAnalysis a = GtpAnalysisParser.Parse(line, GtpAnalysisKind.KataGo, 2, 2, Stone.White, 5)!;
        a.Best!.Point.Should().Be(new Point(0, 0));
        a.Best.ScoreLead.Should().Be(-3.5);
        a.Best.Pv.Should().Equal(new Point(0, 0), new Point(1, 1));
        a.Winrate.Should().BeApproximately(0.35, 1e-9);
        a.ScoreLead.Should().Be(-3.1);
        a.Visits.Should().Be(9);
        a.Ownership.Should().Equal(-0.5, 1, -0.5, 1);
        GtpAnalysisParser.Parse("", GtpAnalysisKind.KataGo, 19, 19, Stone.Black, 0).Should().BeNull();
    }

    [Theory]
    [InlineData("-gtp -model \"C:\\My Nets\\b18.bin.gz\"", new[] { "-gtp", "-model", "C:\\My Nets\\b18.bin.gz" })]
    [InlineData("  gtp  --mode=\"a b\"x \\\"q ", new[] { "gtp", "--mode=a bx", "\"q" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("", new string[0])]
    public void Command_lines_are_split_like_a_shell(string line, string[] expected) =>
        CommandLine.Split(line).Should().Equal(expected);
}
