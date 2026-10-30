using System.ComponentModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Engines.KataGo;
using Hoshi.Sgf;

namespace Hoshi.App.ViewModels;

public sealed partial class AnalysisViewModel
{
    /// <summary>The last fight reading (for tests and a possible indicator).</summary>
    public FightReading? LastFight { get; private set; }

    /// <summary>How big the last celebration was allowed to be (0–3), for tests and the music.</summary>
    public int LastImportance { get; private set; }

    private (GameNode Node, double Intensity)? _fightAt;

    private void OnMoveSettled(object? sender, PlacedMove move)
    {
        // The fight around the move, from the board alone: it decides how big a celebration may be (with the
        // engine) or drives the effects and music by itself (without it).
        FightReading reading = _fight.OnMove(move.Before, move.After, move.Point);
        LastFight = reading;
        _fightAt = (move.Node, reading.Intensity);
        if (IsEngineActive)
        {
            // Reviewing a replay: every step forward is judged by KataGo and celebrated like a move just played.
            if (_game.IsReview && _awaitingJudgement != move.Node)
            {
                _awaitingJudgement = move.Node;
                TryCelebrate(Position.Of(_game));
            }

            return;
        }

        // No engine (live OGS games, or KataGo not set up): effects and music follow the fight on the board —
        // contact, short liberties and captures — never whether a move was good. Opening skirmishes stay quiet.
        BattleHeat?.Invoke(this, reading.Heat);
        AppSettings settings = _settings?.Current ?? new AppSettings();
        int strength = MoveImportance.IsOpening(_game.MoveNumber, move.After.Width, move.After.Height)
            ? Math.Max(0, reading.Strength - 1)
            : reading.Strength;
        if (settings.MoveEffects && strength > 0)
        {
            Impact = new BoardImpact(move.Point, strength, ++_impacts);
            _sounds?.Play(
                strength switch { 3 => SoundEffect.ExplosionBig, 2 => SoundEffect.ExplosionMedium, _ => SoundEffect.ImpactSmall },
                settings.SoundVolume / 100.0);
        }
    }

    private void OnMovePlayed(object? sender, GameNode node)
    {
        if (node == _celebratedNode)
        {
            return; // already judged as it was settled
        }

        // Usually instant: the position before the move was being analysed while the user thought, so the move is
        // already among KataGo's candidates there.
        _awaitingJudgement = IsEngineActive ? node : null;
        if (_awaitingJudgement is not null && PlatformInfo.IsPhone)
        {
            // Phones: the verdict comes first and the score graph waits (with one analysis thread a graph turn in
            // progress would hold the move's search back). It resumes once the move is analysed.
            _graphCts?.Cancel();
            _graphCts = null;
        }

        TryCelebrate(Position.Of(_game));
        if (_awaitingJudgement == node && JudgeFallback is { } wait)
        {
            int fallback = ++_fallbacks;
            _ = Task.Delay(wait).ContinueWith(_ => _ui.Post(() => FallBack(node, fallback)), TaskScheduler.Default);
        }
    }

    /// <summary>KataGo is still thinking: the effect follows the fight on the board now, the verdict comes later.</summary>
    private void FallBack(GameNode node, int fallback)
    {
        if (fallback != _fallbacks || _awaitingJudgement != node || node != _game.CurrentNode || !IsEngineActive)
        {
            return;
        }

        _fellBackNode = node;
        AppSettings settings = _settings?.Current ?? new AppSettings();
        if (LastFight is not { } reading || _fightAt is not { } at || at.Node != node || !settings.MoveEffects
            || node.GetMove(_game.Cursor.BoardSize) is not { Point: { } point })
        {
            return;
        }

        int strength = MoveImportance.IsOpening(_game.MoveNumber, _game.Board.Width, _game.Board.Height)
            ? Math.Max(0, reading.Strength - 1)
            : reading.Strength;
        LastImportance = strength;
        if (strength > 0)
        {
            Impact = new BoardImpact(point, strength, ++_impacts);
            _sounds?.Play(
                strength switch { 3 => SoundEffect.ExplosionBig, 2 => SoundEffect.ExplosionMedium, _ => SoundEffect.ImpactSmall },
                settings.SoundVolume / 100.0);
        }
    }

    /// <summary>Celebrates the awaited move once its verdict rests on enough visits (partial results are fine).</summary>
    private void TryCelebrate(Position pos)
    {
        int n = pos.Moves.Count;
        if (Assessment is not { } judged || _awaitingJudgement is not { } node || node != _game.CurrentNode || n == 0
            || !_cache.TryGetValue(pos.Key(n - 1), out TurnAnalysis? before))
        {
            return;
        }

        int enough = Math.Min(JudgeThreshold, _engine?.Visits ?? JudgeThreshold);
        bool beforeSolid = before.Visits >= enough || _complete.Contains(pos.Key(n - 1));
        bool afterSolid = judged.Rank is not null
            || (_cache.TryGetValue(pos.Key(n), out TurnAnalysis? after) && (after.Visits >= enough || _complete.Contains(pos.Key(n))));
        if (beforeSolid && afterSolid)
        {
            _awaitingJudgement = null;
            _celebratedNode = node;
            MoveJudged?.Invoke(this, judged);
            Celebrate(judged, before, node, n);
        }
    }

    /// <summary>
    /// KataGo's verdicts on these moves, for the study report: cached results first, then a quick search for the
    /// rest. Empty when the engine is not set up, or during the player's live OGS games (no AI help there).
    /// </summary>
    public async Task<IReadOnlyDictionary<GameNode, Services.Study.StudyVerdict>> VerdictsAsync(IReadOnlyList<GameNode> nodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var verdicts = new Dictionary<GameNode, Services.Study.StudyVerdict>();
        if (!IsEngineActive || _engine is null)
        {
            return verdicts;
        }

        GameCursor cursor = _game.Cursor;
        foreach (GameNode node in nodes)
        {
            if (node.GetMove(cursor.BoardSize) is not { } move)
            {
                continue;
            }

            Position pos = Position.At(cursor, node);
            int n = pos.Moves.Count;
            if (n == 0)
            {
                continue;
            }

            var found = new Dictionary<int, TurnAnalysis>();
            foreach (int t in new[] { n - 1, n })
            {
                if (_cache.TryGetValue(pos.Key(t), out TurnAnalysis? cached))
                {
                    found[t] = cached;
                }
            }

            int[] missing = [.. new[] { n - 1, n }.Where(t => !found.ContainsKey(t))];
            if (missing.Length > 0)
            {
                try
                {
                    AnalysisQuery query = pos.Query(missing, Math.Max(30, _engine.Visits / 2)) with { Priority = GraphPriority };
                    foreach (TurnAnalysis t in await _engine.AnalyzeAsync(query, cancellationToken))
                    {
                        found[t.Turn] = t;
                        string key = pos.Key(t.Turn);
                        _ui.Post(() => _cache.TryAdd(key, t));
                    }
                }
                catch (EngineException)
                {
                    break; // the report goes out without the remaining verdicts
                }
            }

            if (found.TryGetValue(n - 1, out TurnAnalysis? before)
                && MoveReview.Assess(before, found.GetValueOrDefault(n), new EngineMove(move.Color, move.Point)) is { } judged)
            {
                verdicts[node] = new Services.Study.StudyVerdict(judged.Quality, judged.PointsLost, judged.Best.Point, MoveImportance.Criticality(before, move.Color));
            }
        }

        return verdicts;
    }

    /// <summary>Effect strength for a move quality: only good moves are celebrated.</summary>
    public static int ImpactStrength(MoveQuality quality) => quality switch
    {
        MoveQuality.Best => 3,
        MoveQuality.Excellent => 2,
        MoveQuality.Good => 1,
        _ => 0,
    };

    /// <summary>
    /// Sound and board impact for a move the user just played, once the engine has judged it — as big as the
    /// moment deserves (<see cref="MoveImportance"/>): a routine opening move only gets its stone sound.
    /// </summary>
    private void Celebrate(MoveAssessment judged, TurnAnalysis before, GameNode node, int moveNumber)
    {
        AppSettings settings = _settings?.Current ?? new AppSettings();
        double fight = _fightAt is { } f && f.Node == node ? f.Intensity : 0;
        bool opening = MoveImportance.IsOpening(moveNumber, _game.Board.Width, _game.Board.Height);
        int strength = MoveImportance.Strength(judged.Quality, MoveImportance.Criticality(before, judged.Played.Color), fight, opening);
        MoveWeighed?.Invoke(this, new MoveWeight(judged.Quality, strength, opening));
        if (node == _fellBackNode)
        {
            return; // its effect already went off, following the fight
        }

        LastImportance = strength;
        if (!settings.MoveEffects || strength == 0 || judged.Played.Point is not { } point || !IsEngineActive)
        {
            return;
        }

        Impact = new BoardImpact(point, strength, ++_impacts);
        _sounds?.Play(
            strength switch { 3 => SoundEffect.ExplosionBig, 2 => SoundEffect.ExplosionMedium, _ => SoundEffect.ImpactSmall },
            settings.SoundVolume / 100.0);
    }
}
