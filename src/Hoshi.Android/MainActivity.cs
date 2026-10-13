using Android.App;
using Android.Content.PM;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace Hoshi.Droid;

/// <summary>Hoshi's only activity: the Avalonia app in its phone layout.</summary>
[Activity(
    Label = "Hoshi",
    Theme = "@style/HoshiTheme",
    Icon = "@drawable/icon",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    WindowSoftInputMode = SoftInput.AdjustResize,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize
        | ConfigChanges.UiMode | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity<global::Hoshi.App.App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        AndroidPlatform.Initialize(this);
        return base.CustomizeAppBuilder(builder).WithInterFont();
    }

    protected override void OnNewIntent(Android.Content.Intent? intent)
    {
        base.OnNewIntent(intent);
        AndroidPlatform.OnIntent(intent);
    }

    protected override void OnPause()
    {
        base.OnPause();
        AndroidPlatform.OnPause();
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidPlatform.OnResume(this);
    }
}
