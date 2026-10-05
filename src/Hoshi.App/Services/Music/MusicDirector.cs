using Hoshi.Engines.KataGo;

namespace Hoshi.App.Services.Music;

/// <summary>
/// Decides how excited the music is. "Heat" (0–5) rises with good moves — more for better moves, for moves that
/// mattered (a routine good move barely counts, a turning point counts fully) and for streaks of good moves in quick
/// succession — stays low in the opening, drops on inaccuracies and mistakes, falls to zero on a blunder (with a
/// tape-stop), and cools down by itself when nothing good happens for a while.
/// Heat maps to layer gains (each layer fades in one heat step after the previous) and to a low-pass filter that
/// opens as the music heats up. Pure logic on an explicit clock, so it is fully testable.
/// </summary>
public sealed class MusicDirector
{
    public const double MaxHeat = 5.3;

    /// <summary>Seconds without a good move before the music starts cooling down.</summary>
    public const double CoolDelay = 10;

    /// <summary>Heat lost per second while cooling.</summary>
    public const double CoolRate = 0.08;

    /// <summary>The music stays this calm, at most, during the opening.</summary>
    public const double OpeningCap = 1.0;

    /// <summary>How much a good verdict counts for each importance (0 routine … 3 turning point).</summary>
    public static readonly double[] ImportanceWeight = [0.3, 0.55, 0.8, 1.0];

    /// <summary>Good moves closer together than this build a streak.</summary>
    public const double StreakWindow = 25;

    private double _heat;
    private double _lastUpdate;
    private double _lastGood = double.NegativeInfinity;
    private double _lastEvent = double.NegativeInfinity;

    public int Streak { get; private set; }

    /// <summary>Raised when a blunder should stop the tape.</summary>
    public event EventHandler? TapeStop;

    public double HeatAt(double now)
    {
        Advance(now);
        return _heat;
    }

    public void OnVerdict(MoveQuality quality, double now, int importance = 3, bool opening = false)
    {
        Advance(now);
        switch (quality)
        {
            case MoveQuality.Best or MoveQuality.Excellent or MoveQuality.Good:
                Streak = now - _lastGood <= StreakWindow ? Streak + 1 : 1;
                _lastGood = now;
                double gain = quality switch { MoveQuality.Best => 1.0, MoveQuality.Excellent => 0.75, _ => 0.5 };
                double weight = ImportanceWeight[Math.Clamp(importance, 0, 3)];
                _heat = Math.Min(opening ? Math.Max(_heat, OpeningCap) : MaxHeat, _heat + (weight * (gain + Math.Min(0.5, 0.12 * (Streak - 1)))));
                break;
            case MoveQuality.Inaccuracy:
                Streak = 0;
                _heat = Math.Max(0, _heat - 1.0);
                break;
            case MoveQuality.Mistake:
                Streak = 0;
                _heat = Math.Max(0, _heat - 2.0);
                break;
            default:
                Streak = 0;
                bool wasHot = _heat >= 1;
                _heat = 0;
                if (wasHot)
                {
                    TapeStop?.Invoke(this, EventArgs.Empty);
                }

                break;
        }

        _lastEvent = now;
    }

    /// <summary>
    /// Follows the battle on the board instead of the engine's verdicts (live OGS games, no KataGo): the music is as
    /// hot as the fight, and falls back gently when the fight moves away.
    /// </summary>
    public void OnBattleHeat(double battleHeat, double now)
    {
        Advance(now);
        _heat = Math.Clamp(Math.Max(battleHeat, _heat - 0.6), 0, MaxHeat);
        _lastEvent = now;
    }

    /// <summary>Target gain of each layer for a heat value (layer 0 always plays).</summary>
    public static double LayerGain(int layer, double heat) => layer == 0
        ? 1.0 - (0.15 * Math.Clamp(heat - 3, 0, 1))
        : Math.Clamp(heat - ((layer - 1) * LayerSpacing) - 0.3, 0, 1);

    /// <summary>Heat between one layer coming in and the next.</summary>
    public const double LayerSpacing = 0.85;

    /// <summary>Depth (0–0.45) of the whole-beat "sidechain" pump on the melodic layers once the music is hot.</summary>
    public static double PumpDepth(double heat) => 0.45 * Math.Clamp((heat - 3.2) / 1.8, 0, 1);

    /// <summary>Master low-pass cutoff in Hz: muffled and cosy when calm, fully open at full heat.</summary>
    public static double Cutoff(double heat) => Math.Min(18000, 900 * Math.Pow(2, heat * 1.15));

    private void Advance(double now)
    {
        if (now > _lastUpdate)
        {
            double coolingFrom = Math.Max(_lastUpdate, Math.Max(_lastEvent, _lastGood) + CoolDelay);
            if (now > coolingFrom)
            {
                _heat = Math.Max(0, _heat - ((now - coolingFrom) * CoolRate));
            }

            _lastUpdate = now;
        }
    }
}
