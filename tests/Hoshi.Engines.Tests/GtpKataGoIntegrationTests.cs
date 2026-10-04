using Hoshi.Core;
using Hoshi.Engines.Gtp;
using Hoshi.Engines.KataGo;

namespace Hoshi.Engines.Tests;

/// <summary>
/// Against a real KataGo in GTP mode, only when HOSHI_KATAGO_GTP is set to "katago|model|gtp config" (never in CI).
/// </summary>
public sealed class GtpKataGoIntegrationTests
{
    private static GtpEngineConfig? Config() =>
        Environment.GetEnvironmentVariable("HOSHI_KATAGO_GTP")?.Split('|') is [var exe, var model, var cfg]
            ? new GtpEngineConfig("KataGo", exe, $"gtp -model \"{model}\" -config \"{cfg}\"")
            : null;

    [Fact]
    public async Task KataGo_plays_and_analyses_over_gtp()
    {
        if (Config() is not { } config)
        {
            return;
        }

        var log = new List<GtpTraffic>();
        await using GtpEngine engine = await GtpEngine.StartAsync(config, traffic: (_, t) => { lock (log) { log.Add(t); } });
        engine.DisplayName.Should().StartWith("KataGo");
        engine.AnalysisKind.Should().Be(GtpAnalysisKind.KataGo);

        var position = new GtpPosition { Width = 9, Height = 9, Komi = 7, Moves = [new EngineMove(Stone.Black, new Point(4, 4))], ToMove = Stone.White };
        GtpMove move = await engine.GenMoveAsync(position).WaitAsync(TimeSpan.FromMinutes(2));
        move.Resign.Should().BeFalse();

        var updates = new List<TurnAnalysis>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        await engine.AnalyzeAsync(position with { Moves = [.. position.Moves, new EngineMove(Stone.White, move.Point)], ToMove = Stone.Black }, 2, TimeSpan.FromSeconds(0.5), a => { lock (updates) { updates.Add(a); } }, cts.Token);
        updates.Should().NotBeEmpty();
        TurnAnalysis last = updates[^1];
        last.ToMove.Should().Be(Stone.Black);
        last.Winrate.Should().BeInRange(0.05, 0.95, "an even 9×9 opening");
        last.Ownership.Should().HaveCount(81);
        last.Candidates.Should().OnlyContain(c => c.Point == null || position.Moves.All(m => m.Point != c.Point));

        // The engine is still in step after the stream: a console command answers normally.
        (await engine.SendRawAsync("showboard")).Success.Should().BeTrue();
        log.Should().Contain(t => t.Direction == GtpDirection.Sent && t.Text.StartsWith("kata-analyze", StringComparison.Ordinal));
    }
}
