using Hoshi.App.Services;
using Hoshi.App.Services.KataGo;
using Hoshi.App.Services.Music;
using Hoshi.Ogs.Auth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Tests;

/// <summary>The hooks Android uses to plug in its KataGo, audio and secure store (exercised here on the desktop).</summary>
[Collection("Language")] // static hooks: never in parallel with other tests
public sealed class PlatformHookTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-platform-" + Guid.NewGuid().ToString("N"));

    public PlatformHookTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        KataGoLocator.PlatformBundle = null;
        PcmSinks.PlatformFactory = null;
        SecureTokenStore.PlatformFactory = null;
        PlatformInfo.IsPhone = false;
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_KataGo_shipped_inside_the_app_is_used_with_the_phone_settings()
    {
        string native = Path.Combine(_dir, "lib");
        string models = Path.Combine(_dir, "files", "katago-model");
        Directory.CreateDirectory(native);
        Directory.CreateDirectory(models);
        File.WriteAllText(Path.Combine(native, "libkatago.so"), "elf");
        File.WriteAllText(Path.Combine(models, "b10c128.txt.gz"), "net");
        KataGoLocator.PlatformBundle = (Path.Combine(native, "libkatago.so"), models);
        PlatformInfo.IsPhone = true;

        ResolvedKataGo found = KataGoLocator.Resolve(new AppSettings(), Path.Combine(_dir, "app"), Path.Combine(_dir, "data"))!;
        found.Source.Should().Be(KataGoSource.Bundled);
        found.Options.Executable.Should().EndWith("libkatago.so");
        found.Options.Model.Should().EndWith("b10c128.txt.gz");
        File.ReadAllText(found.Options.Config).Should().Contain("numEigenThreadsPerModel = 4").And.Contain("numAnalysisThreads = 1");
        File.ReadAllText(KataGoLocator.EnsureGtpConfig(Path.Combine(_dir, "data"))).Should().Contain("maxTime = 5");
    }

    [Fact]
    public async Task Platform_audio_and_secure_store_replace_the_desktop_ones()
    {
        var sink = new NullSink();
        PcmSinks.PlatformFactory = rate => rate == 44100 ? sink : null;
        PcmSinks.Create(44100, out string? problem).Should().BeSameAs(sink);
        problem.Should().BeNull();
        PcmSinks.Create(22050, out problem).Should().BeNull();
        problem.Should().NotBeNull();

        var store = new MemoryTokenStore();
        SecureTokenStore.PlatformFactory = () => store;
        ITokenStore created = SecureTokenStore.Create(NullLoggerFactory.Instance);
        await created.WriteAsync("k", "v", CancellationToken.None);
        (await store.ReadAsync("k", CancellationToken.None)).Should().Be("v");
    }

    private sealed class NullSink : IPcmSink
    {
        public void Write(ReadOnlySpan<short> interleaved, CancellationToken cancellationToken)
        {
        }

        public void Dispose()
        {
        }
    }
}
