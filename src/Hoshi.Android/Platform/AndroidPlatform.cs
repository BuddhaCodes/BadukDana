using Android.App;
using Android.Content;
using Hoshi.App;
using Hoshi.App.Services;
using Hoshi.App.Services.KataGo;
using Hoshi.App.Services.Music;
using Hoshi.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hoshi.Droid;

/// <summary>
/// Wires Hoshi.App to Android before the app starts: KataGo from the APK, AudioTrack for the music, SoundPool
/// for the effects, the Android Keystore for OGS tokens, and the phone layout instead of windows.
/// </summary>
internal static class AndroidPlatform
{
    private static bool _initialized;
    private static bool _musicWasPlaying;
    private static Activity? _activity;
    private static bool _keepScreenOn;
    private static bool _immersive;
    private static bool _lightBars;
    private static Android.Graphics.Color? _barColor;

    public static void Initialize(Activity activity)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _activity = activity;
        Context context = activity.ApplicationContext ?? activity;
        PlatformInfo.IsPhone = true;

        // A light tick with each stone (follows the system's touch feedback setting; needs no permission).
        PlatformInfo.Vibrate = () => _activity?.Window?.DecorView?.PerformHapticFeedback(Android.Views.FeedbackConstants.VirtualKey);
        PlatformInfo.Notify = (title, text, gameId) => ShowNotification(context, title, text, gameId);
        PlatformInfo.ShareDirectory = Path.Combine(context.CacheDir?.AbsolutePath ?? Path.GetTempPath(), "share");
        PlatformInfo.ShareFile = (path, mime, title) => Share(context, path, mime, null, title);
        PlatformInfo.ShareText = (text, title) => Share(context, null, "text/plain", text, title);
        PlatformInfo.KeepScreenOn = on =>
        {
            _keepScreenOn = on;
            ApplyKeepScreenOn();
        };
        PlatformInfo.Immersive = on =>
        {
            _immersive = on;
            ApplySystemBars();
        };
        PlatformInfo.SystemBars = (color, light) =>
        {
            _barColor = Android.Graphics.Color.Argb(255, color.R, color.G, color.B);
            _lightBars = light;
            ApplySystemBars();
        };

        // KataGo: the executable is installed with the app's native libraries; the network is copied out of the APK.
        string nativeDirectory = context.ApplicationInfo?.NativeLibraryDir ?? string.Empty;
        string modelDirectory = Path.Combine(AppPaths.DataDirectory, "katago-model");
        CopyAsset(context, "katago/b10c128.txt.gz", Path.Combine(modelDirectory, "b10c128.txt.gz"));
        KataGoLocator.PlatformBundle = (Path.Combine(nativeDirectory, "libkatago.so"), modelDirectory);

        PcmSinks.PlatformFactory = rate => new AudioTrackSink(rate);
        SecureTokenStore.PlatformFactory = () => new KeystoreTokenStore(Path.Combine(AppPaths.DataDirectory, "secrets"));

        global::Hoshi.App.App.ServiceFactory = () =>
        {
            // After the OGS sign-in in the browser, its "signed in" page brings Hoshi back (MainActivity's hoshi:// filter).
            IHost host = AppHost.Create(["--Ogs:AppReturnUri=" + MainActivity.SignedInIntentUri], services =>
            {
                services.AddPhoneShell();
                services.AddSingleton(_ => new AndroidSoundService(context));
                services.AddSingleton<ISoundService>(sp => new MutableSoundService(
                    sp.GetRequiredService<AndroidSoundService>().WithFiles(sp.GetRequiredService<SystemSoundService>()),
                    sp.GetRequiredService<ISettingsService>(),
                    sp.GetRequiredService<ZenMode>()));
            });
            host.Start();
            return host.Services;
        };
    }

    /// <summary>Android may stop the app at any time once it is in the background: keep the game, pause the music.</summary>
    public static void OnPause()
    {
        IServiceProvider? services = global::Hoshi.App.App.Services;
        if (services is null)
        {
            return;
        }

        try
        {
            services.GetService<MainWindowViewModel>()?.SaveCurrentGame();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Android.Util.Log.Warn("Hoshi", "Could not save the game: " + ex.Message);
        }

        if (services.GetService<IMusicService>() is { IsPlaying: true } music)
        {
            _musicWasPlaying = true;
            music.Stop();
        }
    }

    public static void OnResume(Activity activity)
    {
        _activity = activity;
        ApplyKeepScreenOn();
        ApplySystemBars();

        if (_musicWasPlaying && global::Hoshi.App.App.Services?.GetService<IMusicService>() is { } music)
        {
            _musicWasPlaying = false;
            music.Start();
        }
    }

    /// <summary>Extra of the intent a notification opens: the OGS game to show.</summary>
    public const string GameIdExtra = "hoshi.ogs.game";

    private const string TurnChannel = "turns";
    private static bool _askedNotifications;

    /// <summary>Android's share sheet for a file of the share folder (through the FileProvider) or for text.</summary>
    private static Task Share(Context context, string? path, string mime, string? text, string title)
    {
        try
        {
            var send = new Intent(Intent.ActionSend);
            send.SetType(mime);
            if (path is not null)
            {
                Android.Net.Uri uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, ShareAuthority, new Java.IO.File(path))!;
                send.PutExtra(Intent.ExtraStream, uri);
                send.AddFlags(ActivityFlags.GrantReadUriPermission);
            }
            else
            {
                send.PutExtra(Intent.ExtraText, text ?? string.Empty);
            }

            Intent chooser = Intent.CreateChooser(send, title)!;
            if (_activity is { } activity)
            {
                activity.StartActivity(chooser);
            }
            else
            {
                chooser.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(chooser);
            }
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException or InvalidOperationException)
        {
            Android.Util.Log.Warn("Hoshi", "Share failed: " + ex.Message);
        }

        return Task.CompletedTask;
    }

    private const string ShareAuthority = "io.github.buddhacodes.hoshi.share";

    private static void ShowNotification(Context context, string title, string text, long gameId)
    {
        try
        {
            var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
            if (manager is null)
            {
                return;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(33)
                && context.CheckSelfPermission(Android.Manifest.Permission.PostNotifications) != Android.Content.PM.Permission.Granted)
            {
                if (!_askedNotifications && _activity is { } activity)
                {
                    _askedNotifications = true;
                    activity.RunOnUiThread(() => activity.RequestPermissions([Android.Manifest.Permission.PostNotifications], 7));
                }

                return;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(26) && manager.GetNotificationChannel(TurnChannel) is null)
            {
                manager.CreateNotificationChannel(new NotificationChannel(TurnChannel, title, NotificationImportance.Default));
            }

            var open = new Intent(context, typeof(MainActivity));
            open.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            open.PutExtra(GameIdExtra, gameId);
            PendingIntent? pending = PendingIntent.GetActivity(context, (int)(gameId % int.MaxValue), open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
            Notification notification = new Notification.Builder(context, TurnChannel)
                .SetSmallIcon(Resource.Drawable.icon)!
                .SetContentTitle(title)!
                .SetContentText(text)!
                .SetContentIntent(pending)!
                .SetAutoCancel(true)!
                .Build()!;
            manager.Notify((int)(gameId % int.MaxValue), notification);
        }
        catch (Java.Lang.Exception ex)
        {
            Android.Util.Log.Warn("Hoshi", "Could not notify: " + ex.Message);
        }
    }

    /// <summary>A notification was tapped: open its game.</summary>
    public static void OnIntent(Intent? intent)
    {
        long gameId = intent?.GetLongExtra(GameIdExtra, 0) ?? 0;
        if (gameId > 0 && global::Hoshi.App.App.Services?.GetService<MainWindowViewModel>() is { } main)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await main.OpenOnlineGameAsync(gameId));
        }
    }

    /// <summary>
    /// The status and navigation bars: in the theme's bar colour with icons that read on it, and hidden in zen
    /// (a swipe from the edge shows them for a moment).
    /// </summary>
    private static void ApplySystemBars()
    {
        if (_activity is not { } activity)
        {
            return;
        }

        activity.RunOnUiThread(() =>
        {
            if (activity.Window is not { } window)
            {
                return;
            }

            if (_barColor is { } c)
            {
#pragma warning disable CA1422, CS0618 // the colours are ignored, not wrong, on Android 15's edge-to-edge
                window.SetStatusBarColor(c);
                window.SetNavigationBarColor(c);
#pragma warning restore CA1422, CS0618
            }

            var insets = AndroidX.Core.View.WindowCompat.GetInsetsController(window, window.DecorView);
            insets.AppearanceLightStatusBars = _lightBars;
            insets.AppearanceLightNavigationBars = _lightBars;
            if (_immersive)
            {
                insets.SystemBarsBehavior = AndroidX.Core.View.WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
                insets.Hide(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
            }
            else
            {
                insets.Show(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars());
            }
        });
    }

    private static void ApplyKeepScreenOn()
    {
        if (_activity is not { } activity)
        {
            return;
        }

        activity.RunOnUiThread(() =>
        {
            if (_keepScreenOn)
            {
                activity.Window?.AddFlags(Android.Views.WindowManagerFlags.KeepScreenOn);
            }
            else
            {
                activity.Window?.ClearFlags(Android.Views.WindowManagerFlags.KeepScreenOn);
            }
        });
    }

    /// <summary>Copies an asset once (again when the APK brings a different size, i.e. a new network).</summary>
    private static void CopyAsset(Context context, string asset, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // Fast path on every start after the first: the .gz asset is stored uncompressed in the APK, so its
            // length is known without reading it (the network is ~10 MB; reading it each time slowed the start).
            try
            {
                using Android.Content.Res.AssetFileDescriptor fd = context.Assets!.OpenFd(asset);
                if (File.Exists(target) && new FileInfo(target).Length == fd.Length)
                {
                    return;
                }
            }
            catch (Java.IO.IOException)
            {
                // Compressed in the APK: compare after reading it below.
            }

            using Stream source = context.Assets!.Open(asset);
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            if (File.Exists(target) && new FileInfo(target).Length == buffer.Length)
            {
                return;
            }

            File.WriteAllBytes(target + ".tmp", buffer.ToArray());
            File.Move(target + ".tmp", target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or Java.IO.IOException or UnauthorizedAccessException)
        {
            Android.Util.Log.Warn("Hoshi", $"Could not copy {asset}: {ex.Message}");
        }
    }
}
