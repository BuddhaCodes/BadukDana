using System.Globalization;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;
using Hoshi.Core;
using Hoshi.Sgf;
using AvPoint = Avalonia.Point;
using Point = Hoshi.Core.Point;

namespace Hoshi.App.Controls;

public sealed partial class GoBoardControl
{
    // ---------- Weak groups ----------

    /// <summary>Seconds per breath of a group in serious danger.</summary>
    private const double PulseSeconds = 2.6;

    /// <summary>
    /// A quiet halo under the stones of groups that are not safe: a cool blue for unsettled and weak groups (stronger
    /// for weak), a muted red for groups in serious danger that slowly breathes with full effects. Strong groups,
    /// and every group with effects off, are drawn as usual.
    /// </summary>
    private void DrawGroupHalos(DrawingContext context, BoardGeometry g, BoardState board)
    {
        if (GroupStatuses is not { Count: > 0 } groups || Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        double t = GroupPulseTime ?? _groupClock.Elapsed.TotalSeconds;
        bool breathe = Animate && Effects == Services.EffectsLevel.Full;
        double r = g.Cell * StoneRadius;
        foreach (GroupStatus group in groups)
        {
            (Color color, double alpha) = group.Health switch
            {
                GroupHealth.Unsettled => (Color.FromRgb(70, 130, 220), 0.32),
                GroupHealth.Weak => (Color.FromRgb(70, 130, 220), 0.58),
                _ => (Color.FromRgb(205, 72, 60), breathe ? 0.5 + (0.3 * (0.5 - (0.5 * Math.Cos(2 * Math.PI * t / PulseSeconds)))) : 0.65),
            };
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B), 0.55),
                    new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1),
                },
            };
            foreach (Point p in group.Stones)
            {
                if (board.IsOnBoard(p) && board[p] != Stone.Empty)
                {
                    context.DrawEllipse(brush, null, StoneCenter(g, p), r * 1.6, r * 1.6);
                }
            }
        }
    }

    /// <summary>The red halo breathes only while a group in serious danger is shown with full effects.</summary>
    private void UpdateGroupTimer()
    {
        bool needed = GroupStatuses is { } groups && groups.Any(x => x.Health == GroupHealth.Critical)
            && Animate && Effects == Services.EffectsLevel.Full;
        if (needed)
        {
            if (!_groupClock.IsRunning)
            {
                _groupClock.Restart();
            }

            _groupTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(50), Avalonia.Threading.DispatcherPriority.Render, (_, _) => InvalidateVisual());
            _groupTimer.Start();
        }
        else
        {
            _groupTimer?.Stop();
            _groupClock.Reset();
        }

        InvalidateVisual();
    }

    private void StartCapture(Point origin, List<(Point, Stone)> captured)
    {
        if (Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        _capture = new CaptureEffect(origin, captured, unchecked((origin.X * 31) + (origin.Y * 17) + captured.Count), Effects == Services.EffectsLevel.Subtle);
        _captureClock.Restart();
        _captureTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_capture is null || _capture.IsDone(_captureClock.Elapsed.TotalSeconds))
            {
                _capture = null;
                _captureTimer!.Stop();
            }

            InvalidateVisual();
        });
        _captureTimer.Start();
    }

    /// <summary>The strength the running impact is drawn with (1 when subtle), or null; for tests.</summary>
    internal int? RunningImpactStrength => _impact?.Strength;

    /// <summary>True when the running capture is the subtle fade; for tests.</summary>
    internal bool IsCaptureSubtle => _capture?.IsSubtle == true;

    /// <summary>The captured stones being animated, for tests.</summary>
    internal int CapturingStones => _capture?.Count ?? 0;

    /// <summary>Freezes the capture animation at this many seconds (tests).</summary>
    internal double? CaptureTime { get; set; }

    /// <summary>
    /// Draws <paramref name="impact"/> as it is <paramref name="seconds"/> after the move, without a timer: the replay
    /// maker renders the effect frame by frame off screen.
    /// </summary>
    internal void FreezeImpact(ViewModels.BoardImpact impact, double seconds)
    {
        _impact = impact.Strength > 0 ? new ImpactEffect(impact.Point, impact.Strength, impact.Id) : null;
        ImpactTime = seconds;
        InvalidateVisual();
    }

    private void StartImpact(ViewModels.BoardImpact impact)
    {
        if (!Animate || impact.Strength <= 0 || Effects == Services.EffectsLevel.Off)
        {
            return;
        }

        _impact = new ImpactEffect(impact.Point, impact.Strength, impact.Id, Effects == Services.EffectsLevel.Subtle);
        _impactClock.Restart();
        _impactTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_impact is null || _impact.IsDone(_impactClock.Elapsed.TotalSeconds))
            {
                _impact = null;
                _impactTimer!.Stop();
            }

            InvalidateVisual();
        });
        _impactTimer.Start();
        InvalidateVisual();
    }

    private void StartPlacementAnimation(Point p)
    {
        _animPoint = p;
        _animClock.Restart();
        _animTimer ??= new Avalonia.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(16), Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
        {
            if (_animClock.Elapsed > EffectDuration && _animClock.Elapsed > SettleDuration)
            {
                _animPoint = null;
                _animTimer!.Stop();
            }

            InvalidateVisual();
        });
        _animTimer.Start();
    }
}
