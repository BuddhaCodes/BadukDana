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
    private async Task RunAsync(Position pos, int[] turns, int visits, CancellationTokenSource cts, bool isGraph)
    {
        if (!isGraph)
        {
            _ui.Post(() => { IsBusy = true; Error = null; });
            try
            {
                await Task.Delay(120, cts.Token); // debounce fast navigation
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        try
        {
            AnalysisQuery query = pos.Query(turns, visits) with
            {
                Priority = isGraph ? GraphPriority : LivePriority,
                ReportDuringSearchEvery = isGraph ? null : LiveReportSeconds,
            };
            IReadOnlyList<TurnAnalysis> results = isGraph
                ? await _engine!.AnalyzeAsync(query, cts.Token)
                : await _engine!.AnalyzeLiveAsync(query, partial => _ui.Post(() => Store(pos, partial, complete: false, cts)), cts.Token);
            _ui.Post(() =>
            {
                foreach (TurnAnalysis t in results)
                {
                    Store(pos, t, complete: !isGraph, cts: null);
                }

                if (!cts.IsCancellationRequested)
                {
                    Position now = Position.Of(_game);
                    ShowCached(now);
                    if (!isGraph)
                    {
                        IsBusy = false;
                    }

                    if (IsAnalysisActive)
                    {
                        StartGraph(now); // the game graph is only filled while it is shown
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer position; its partial results stay in the cache.
        }
        catch (EngineException ex)
        {
            _ui.Post(() => { IsBusy = false; Error = ex.Message; });
        }
    }

    /// <summary>Caches a result (keeping the deeper one) and, for live updates of the shown position, redraws.</summary>
    private void Store(Position pos, TurnAnalysis t, bool complete, CancellationTokenSource? cts)
    {
        if (!_engineReady)
        {
            _engineReady = true;
            OnPropertyChanged(nameof(IsWarmingUp));
        }

        string key = pos.Key(t.Turn);
        if (!_cache.TryGetValue(key, out TurnAnalysis? old) || old.Visits <= t.Visits || complete)
        {
            _cache[key] = t;
        }

        if (complete)
        {
            _complete.Add(key);
        }

        if (cts is { IsCancellationRequested: false })
        {
            ShowCached(Position.Of(_game));
        }
    }

    private void StartGraph(Position pos)
    {
        UpdateGraph(pos);
        if (_graphCts is { IsCancellationRequested: false })
        {
            return; // a batch is already running; it will continue from the cache
        }

        Position track = Position.Of(_game, followMainLine: true);
        int[] missing = [.. Enumerable.Range(0, track.Moves.Count + 1).Where(t => !_cache.ContainsKey(track.Key(t))).Take(GraphBatch)];
        if (missing.Length == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _graphCts = cts;
        _ = RunGraphAsync(track, missing, cts);
    }

    private async Task RunGraphAsync(Position track, int[] turns, CancellationTokenSource cts)
    {
        await RunAsync(track, turns, Math.Max(Math.Min(20, _engine!.Visits), _engine.Visits / 4), cts, isGraph: true);
        _ui.Post(() =>
        {
            if (ReferenceEquals(_graphCts, cts))
            {
                _graphCts = null;
                if (!cts.IsCancellationRequested && IsAnalysisActive && IsEngineActive)
                {
                    StartGraph(Position.Of(_game));
                }
            }
        });
    }

    private void ShowCached(Position pos)
    {
        int n = pos.Moves.Count;
        _cache.TryGetValue(pos.Key(n), out TurnAnalysis? now);
        Analysis = now;
        Assessment = n > 0 && _cache.TryGetValue(pos.Key(n - 1), out TurnAnalysis? before)
            ? MoveReview.Assess(before, now, pos.Moves[n - 1])
            : null;
        Suggestions = now is null ? [] : [.. now.Candidates.Where(c => c.Point is not null).Take(3).Select((c, i) => Suggest(c, i, now))];
        Candidates = now is null ? [] : Rows(now, _game.Board.Height);
        UpdateGraph(pos);
        TryCelebrate(pos);
    }

    /// <summary>The engine's (trusted) verdict on a move the user just played.</summary>
    public event EventHandler<MoveAssessment>? MoveJudged;

    /// <summary>The verdict with how much the moment mattered; drives the adaptive music.</summary>
    public event EventHandler<MoveWeight>? MoveWeighed;

    /// <summary>The battle heat (0–5) after a move, when effects follow the fight instead of the engine.</summary>
    public event EventHandler<double>? BattleHeat;
}
