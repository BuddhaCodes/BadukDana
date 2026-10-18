using System.Numerics;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Hoshi.App.Services.Updates;

namespace Hoshi.App.Views;

/// <summary>The small round card with Hoshi's name shown while the main window is being built (desktops).</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        SplashVersion.Text = UpdatePlatform.Display(UpdatePlatform.CurrentVersion);
        Opened += (_, _) => StartMotion();
    }

    /// <summary>
    /// The three stones breathe one after another and the two friends squish softly. Composition animations: they
    /// run on the render thread, so they keep moving while the UI thread builds the main window.
    /// </summary>
    private void StartMotion()
    {
        // Phases are built into the key frames (not DelayTime), so every stone and friend starts moving at once.
        int i = 0;
        foreach (Control dot in SplashDots.Children)
        {
            Breathe(dot, 0.12f + (0.14f * i++));
        }

        Squish(SplashKuro, squishFirst: false);
        Squish(SplashShiro, squishFirst: true);
    }

    private static readonly TimeSpan BreathLength = TimeSpan.FromMilliseconds(1300);
    private static readonly TimeSpan SquishLength = TimeSpan.FromMilliseconds(1800);

    /// <summary>A stone grows and lights up around <paramref name="peak"/> (a fraction of the loop), then rests.</summary>
    private static void Breathe(Control control, float peak)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
        {
            return;
        }

        Compositor compositor = visual.Compositor;
        visual.CenterPoint = new Vector3((float)control.Bounds.Width / 2, (float)control.Bounds.Height / 2, 0);
        var small = new Vector3(0.75f, 0.75f, 1);
        Vector3KeyFrameAnimation scale = compositor.CreateVector3KeyFrameAnimation();
        scale.Target = "Scale";
        scale.InsertKeyFrame(0f, small);
        scale.InsertKeyFrame(peak - 0.12f, small);
        scale.InsertKeyFrame(peak, new Vector3(1.25f, 1.25f, 1));
        scale.InsertKeyFrame(peak + 0.2f, small);
        scale.InsertKeyFrame(1f, small);
        Loop(scale, BreathLength);

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0f, 0.45f);
        fade.InsertKeyFrame(peak - 0.12f, 0.45f);
        fade.InsertKeyFrame(peak, 1f);
        fade.InsertKeyFrame(peak + 0.2f, 0.45f);
        fade.InsertKeyFrame(1f, 0.45f);
        Loop(fade, BreathLength);

        visual.StartAnimation("Scale", scale);
        visual.StartAnimation("Opacity", fade);
    }

    private static void Squish(Control control, bool squishFirst)
    {
        if (ElementComposition.GetElementVisual(control) is not { } visual)
        {
            return;
        }

        // Squash from the bottom, like a mochi settling; the two friends take turns.
        visual.CenterPoint = new Vector3((float)control.Bounds.Width / 2, (float)control.Bounds.Height, 0);
        var rest = new Vector3(1, 1, 1);
        var squashed = new Vector3(1.04f, 0.95f, 1);
        Vector3KeyFrameAnimation scale = visual.Compositor.CreateVector3KeyFrameAnimation();
        scale.Target = "Scale";
        scale.InsertKeyFrame(0f, squishFirst ? squashed : rest);
        scale.InsertKeyFrame(0.5f, squishFirst ? rest : squashed);
        scale.InsertKeyFrame(1f, squishFirst ? squashed : rest);
        Loop(scale, SquishLength);
        visual.StartAnimation("Scale", scale);
    }

    private static void Loop(KeyFrameAnimation animation, TimeSpan duration)
    {
        animation.Duration = duration;
        animation.IterationBehavior = AnimationIterationBehavior.Forever;
    }
}
