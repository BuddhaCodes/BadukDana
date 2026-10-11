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

    public static void Initialize(Activity activity)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        Context context = activity.ApplicationContext ?? activity;
        PlatformInfo.IsPhone = true;

        // KataGo: the executable is installed with the app's native libraries; the network is copied out of the APK.
        string nativeDirectory = context.ApplicationInfo?.NativeLibraryDir ?? string.Empty;
        string modelDirectory = Path.Combine(AppPaths.DataDirectory, "katago-model");
        CopyAsset(context, "katago/b10c128.txt.gz", Path.Combine(modelDirectory, "b10c128.txt.gz"));
        KataGoLocator.PlatformBundle = (Path.Combine(nativeDirectory, "libkatago.so"), modelDirectory);

        PcmSinks.PlatformFactory = rate => new AudioTrackSink(rate);
        SecureTokenStore.PlatformFactory = () => new KeystoreTokenStore(Path.Combine(AppPaths.DataDirectory, "secrets"));

        global::Hoshi.App.App.ServiceFactory = () =>
        {
            IHost host = AppHost.Create([], services =>
            {
                services.AddPhoneShell();
                services.AddSingleton(_ => new AndroidSoundService(context));
                services.AddSingleton<ISoundService>(sp => new MutableSoundService(
                    sp.GetRequiredService<AndroidSoundService>().WithFiles(sp.GetRequiredService<SystemSoundService>()),
                    sp.GetRequiredService<ISettingsService>()));
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

    public static void OnResume()
    {
        if (_musicWasPlaying && global::Hoshi.App.App.Services?.GetService<IMusicService>() is { } music)
        {
            _musicWasPlaying = false;
            music.Start();
        }
    }

    /// <summary>Copies an asset once (again when the APK brings a different size, i.e. a new network).</summary>
    private static void CopyAsset(Context context, string asset, string target)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
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
