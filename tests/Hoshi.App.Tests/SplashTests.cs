using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hoshi.App.Views;

namespace Hoshi.App.Tests;

public sealed class SplashTests
{
    [AvaloniaFact]
    public void The_splash_shows_the_logo_and_the_version()
    {
        var splash = new SplashWindow();
        splash.Show();
        splash.FindDeep<TextBlock>("SplashVersion")!.Text.Should().MatchRegex(@"^\d+\.\d+\.\d+$");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        using (Avalonia.Media.Imaging.WriteableBitmap frame = splash.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame"))
        {
            frame.Save(Path.Combine(dir, "splash.png"));
        }

        splash.Close();
    }
}
