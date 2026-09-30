using System.Text.Json;
using System.Threading.Channels;
using Hoshi.Core;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Tests;

internal sealed class FakeEngineProcess : IEngineProcess
{
    private readonly Channel<string> _toEngine = Channel.CreateUnbounded<string>();
    private readonly Channel<string?> _fromEngine = Channel.CreateUnbounded<string?>();

    public bool HasExited { get; private set; }

    public string? ExitReason { get; set; }

    public Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        _toEngine.Writer.TryWrite(line);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken) =>
        await _fromEngine.Reader.ReadAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        Exit();
        return ValueTask.CompletedTask;
    }

    public async Task<JsonElement> NextQueryAsync() =>
        JsonDocument.Parse(await _toEngine.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5))).RootElement;

    public void Reply(string json) => _fromEngine.Writer.TryWrite(json);

    public void Exit()
    {
        HasExited = true;
        _fromEngine.Writer.TryWrite(null);
    }
}

public sealed class KataGoAnalysisEngineTests : IAsyncDisposable
{
    private readonly List<FakeEngineProcess> _processes = [];
    private readonly KataGoAnalysisEngine _engine;

    public KataGoAnalysisEngineTests()
    {
        _engine = new KataGoAnalysisEngine(() =>
        {
            var p = new FakeEngineProcess();
            _processes.Add(p);
            return p;
        });
    }

    public ValueTask DisposeAsync() => _engine.DisposeAsync();

    private static AnalysisQuery Query(params int[] turns) => new()
    {
        Width = 19,
        Height = 19,
        Komi = 6.5,
        Rules = RuleSet.Japanese,
        Moves = [new EngineMove(Stone.Black, new Point(15, 3)), new EngineMove(Stone.White, new Point(3, 15)), new EngineMove(Stone.Black, null)],
        Turns = turns,
        MaxVisits = 50,
    };

    private static string Response(string id, int turn, string toMove, double lead, double winrate, params (string Move, double Lead, double Winrate)[] moves)
    {
        string infos = string.Join(",", moves.Select((m, i) =>
            $$"""{"move":"{{m.Move}}","order":{{i}},"visits":{{100 - i}},"winrate":{{m.Winrate}},"scoreLead":{{m.Lead}},"prior":0.1,"pv":["{{m.Move}}","D4"]}"""));
        return $$$"""{"id":"{{{id}}}","isDuringSearch":false,"turnNumber":{{{turn}}},"moveInfos":[{{{infos}}}],"rootInfo":{"currentPlayer":"{{{toMove}}}","scoreLead":{{{lead}}},"winrate":{{{winrate}}},"visits":100}}""";
    }

    [Fact]
    public void Queries_follow_the_analysis_engine_format()
    {
        JsonElement q = JsonDocument.Parse(KataGoAnalysisEngine.ToJson(Query(0, 3) with
        {
            InitialStones = [(new Point(3, 3), Stone.Black)],
            Rules = RuleSet.NewZealand,
        }, "hoshi-1")).RootElement;

        q.GetProperty("id").GetString().Should().Be("hoshi-1");
        q.GetProperty("moves")[0][0].GetString().Should().Be("B");
        q.GetProperty("moves")[0][1].GetString().Should().Be("Q16", "GTP coordinates skip the letter I and count rows from the bottom");
        q.GetProperty("moves")[1][1].GetString().Should().Be("D4");
        q.GetProperty("moves")[2][1].GetString().Should().Be("pass");
        q.GetProperty("initialStones")[0][1].GetString().Should().Be("D16");
        q.GetProperty("rules").GetString().Should().Be("new-zealand");
        q.GetProperty("komi").GetDouble().Should().Be(6.5);
        q.GetProperty("boardXSize").GetInt32().Should().Be(19);
        q.GetProperty("analyzeTurns").EnumerateArray().Select(t => t.GetInt32()).Should().Equal(0, 3);
        q.GetProperty("maxVisits").GetInt32().Should().Be(50);
        q.GetProperty("includeOwnership").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void Responses_are_parsed_from_black_s_point_of_view()
    {
        string json = Response("x", 2, "B", 8.2, 0.87, ("Q5", 8.18, 0.866), ("D4", 12.3, 0.93));
        TurnAnalysis t = KataGoAnalysisEngine.ParseResponse(JsonDocument.Parse(json).RootElement, 19, 19);

        t.Turn.Should().Be(2);
        t.ToMove.Should().Be(Stone.Black);
        t.ScoreLead.Should().Be(8.2);
        t.Winrate.Should().Be(0.87);
        t.Best!.Point.Should().Be(new Point(15, 14), "Q5 is column 16, row 5 from the bottom");
        t.Candidates.Select(c => c.Order).Should().Equal(0, 1);
        t.Best.Pv.Should().Equal(new Point(15, 14), new Point(3, 15));
        t.Ownership.Should().BeNull();
    }

    [Fact]
    public async Task Multi_turn_queries_complete_when_every_turn_has_answered_even_out_of_order()
    {
        Task<IReadOnlyList<TurnAnalysis>> task = _engine.AnalyzeAsync(Query(0, 1), CancellationToken.None);
        FakeEngineProcess p = await WaitForProcessAsync();
        string id = (await p.NextQueryAsync()).GetProperty("id").GetString()!;

        p.Reply("KataGo warming up (not JSON)");
        p.Reply($$$"""{"id":"{{{id}}}","isDuringSearch":true,"turnNumber":1,"moveInfos":[],"rootInfo":{"scoreLead":0}}""");
        p.Reply(Response(id, 1, "W", 1.0, 0.6, ("D4", 1.0, 0.6)));
        task.IsCompleted.Should().BeFalse();
        p.Reply(Response(id, 0, "B", 0.5, 0.55, ("Q16", 0.5, 0.55)));

        IReadOnlyList<TurnAnalysis> result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        result.Select(t => t.Turn).Should().Equal(0, 1);
        result[1].ToMove.Should().Be(Stone.White);
    }

    [Fact]
    public async Task Errors_fail_the_query()
    {
        Task<IReadOnlyList<TurnAnalysis>> task = _engine.AnalyzeAsync(Query(), CancellationToken.None);
        FakeEngineProcess p = await WaitForProcessAsync();
        string id = (await p.NextQueryAsync()).GetProperty("id").GetString()!;

        p.Reply($$"""{"error":"Illegal move","field":"moves","id":"{{id}}"}""");

        await FluentActions.Awaiting(() => task.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().ThrowAsync<EngineException>().WithMessage("*Illegal move*");
    }

    [Fact]
    public async Task Cancelling_terminates_the_query_on_the_engine()
    {
        using var cts = new CancellationTokenSource();
        Task<IReadOnlyList<TurnAnalysis>> task = _engine.AnalyzeAsync(Query(), cts.Token);
        FakeEngineProcess p = await WaitForProcessAsync();
        string id = (await p.NextQueryAsync()).GetProperty("id").GetString()!;

        await cts.CancelAsync();

        await FluentActions.Awaiting(() => task).Should().ThrowAsync<OperationCanceledException>();
        JsonElement stop = await p.NextQueryAsync();
        stop.GetProperty("action").GetString().Should().Be("terminate");
        stop.GetProperty("terminateId").GetString().Should().Be(id);
    }

    [Fact]
    public async Task A_crashed_engine_fails_pending_queries_and_is_restarted()
    {
        Task<IReadOnlyList<TurnAnalysis>> first = _engine.AnalyzeAsync(Query(), CancellationToken.None);
        FakeEngineProcess p = await WaitForProcessAsync();
        await p.NextQueryAsync();

        p.ExitReason = "código 1: Could not open model file";
        p.Exit();
        await FluentActions.Awaiting(() => first.WaitAsync(TimeSpan.FromSeconds(5))).Should().ThrowAsync<EngineException>()
            .WithMessage("KataGo se cerró (código 1: Could not open model file)");

        _ = _engine.AnalyzeAsync(Query(), CancellationToken.None);
        await WaitUntil(() => _processes.Count == 2);
        (await _processes[1].NextQueryAsync()).GetProperty("id").GetString().Should().StartWith("hoshi-");
    }

    private async Task<FakeEngineProcess> WaitForProcessAsync()
    {
        await WaitUntil(() => _processes.Count > 0);
        return _processes[^1];
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        condition().Should().BeTrue();
    }
}

public sealed class MoveReviewTests
{
    private static TurnAnalysis Before(params (Point? P, double Lead, double Winrate)[] moves) => new(
        3, Stone.White, 0.4, moves[0].Lead, 400,
        [.. moves.Select((m, i) => new MoveCandidate(m.P, i, 100, m.Winrate, m.Lead, 0.1, []))], null);

    [Fact]
    public void The_engine_s_first_choice_is_best()
    {
        TurnAnalysis before = Before((new Point(3, 3), -2.0, 0.4), (new Point(15, 15), -1.5, 0.42));

        MoveAssessment a = MoveReview.Assess(before, null, new EngineMove(Stone.White, new Point(3, 3)))!;

        a.Quality.Should().Be(MoveQuality.Best);
        a.PointsLost.Should().Be(0);
        a.Rank.Should().Be(0);
    }

    [Fact]
    public void Losses_are_measured_from_the_mover_s_point_of_view()
    {
        // White to move; scores are from black's view, so a higher black lead is worse for white.
        TurnAnalysis before = Before((new Point(3, 3), -2.0, 0.4), (new Point(15, 15), -1.2, 0.43));

        MoveAssessment a = MoveReview.Assess(before, null, new EngineMove(Stone.White, new Point(15, 15)))!;

        a.PointsLost.Should().BeApproximately(0.8, 1e-9);
        a.Quality.Should().Be(MoveQuality.Good);
        a.Rank.Should().Be(1);
        a.WinrateLost.Should().BeApproximately(0.03, 1e-9);
    }

    [Theory]
    [InlineData(0.3, MoveQuality.Excellent)]
    [InlineData(2.5, MoveQuality.Inaccuracy)]
    [InlineData(5.0, MoveQuality.Mistake)]
    [InlineData(9.0, MoveQuality.Blunder)]
    public void Moves_outside_the_candidates_are_judged_by_the_position_after(double loss, MoveQuality quality)
    {
        TurnAnalysis before = Before((new Point(3, 3), -2.0, 0.4));
        var after = new TurnAnalysis(4, Stone.Black, 0.5, -2.0 + loss, 400, [], null);

        MoveAssessment a = MoveReview.Assess(before, after, new EngineMove(Stone.White, new Point(0, 0)))!;

        a.Quality.Should().Be(quality);
        a.Rank.Should().BeNull();
        a.Best.Point.Should().Be(new Point(3, 3));
    }

    [Fact]
    public void Exit_reasons_show_KataGo_s_last_error_line()
    {
        KataGoProcess.Describe(1, ["KataGo v1.16.0", "Loading model", "Uncaught exception: Could not open file model.bin.gz"])
            .Should().Be("código 1: Uncaught exception: Could not open file model.bin.gz");
        KataGoProcess.Describe(0, []).Should().Be("código 0, sin mensaje de KataGo.");
    }

    [Fact]
    public void Missing_dll_exit_codes_explain_the_CUDA_builds()
    {
        KataGoProcess.Describe(unchecked((int)0xC0000135), []).Should().StartWith("código 0xC0000135: falta una DLL");
    }

    [Fact]
    public void A_GTP_config_is_rejected_before_starting_KataGo()
    {
        string dir = Directory.CreateTempSubdirectory("hoshi-katago").FullName;
        string exe = Path.Combine(dir, "katago.exe");
        string model = Path.Combine(dir, "model.bin.gz");
        string gtp = Path.Combine(dir, "gtp_human5k_example.cfg");
        string analysis = Path.Combine(dir, "analysis_example.cfg");
        File.WriteAllText(exe, string.Empty);
        File.WriteAllText(model, string.Empty);
        File.WriteAllText(gtp, "numSearchThreads = 8\n");
        File.WriteAllText(analysis, "# Analysis\nnumAnalysisThreads = 2\nnumSearchThreadsPerAnalysisThread = 16\n");

        new KataGoOptions(exe, model, gtp).Validate().Should().Contain("gtp_human5k_example.cfg").And.Contain("analysis_example.cfg");
        new KataGoOptions(exe, model, analysis).Validate().Should().BeNull();
        Directory.Delete(dir, recursive: true);
    }
}
