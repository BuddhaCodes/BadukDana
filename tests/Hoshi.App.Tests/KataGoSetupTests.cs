using System.IO.Compression;
using System.Runtime.InteropServices;
using Hoshi.App.Services;
using Hoshi.App.Services.KataGo;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Tests;

public sealed class KataGoLocatorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hoshi-katago").FullName;

    private string App => Directory.CreateDirectory(Path.Combine(_root, "app")).FullName;

    private string Data => Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Nothing_found_means_no_katago()
    {
        KataGoLocator.Resolve(new AppSettings(), App, Data, systemPaths: []).Should().BeNull();
    }

    [Fact]
    public void The_bundled_copy_is_used_with_hoshis_config_and_logs_in_the_data_folder()
    {
        string folder = Directory.CreateDirectory(Path.Combine(App, "katago")).FullName;
        File.WriteAllText(Path.Combine(folder, KataGoLocator.ExecutableName), "x");
        File.WriteAllText(Path.Combine(folder, "README.txt"), "not a model");
        File.WriteAllText(Path.Combine(folder, "b10c128.txt.gz"), "net");

        ResolvedKataGo k = KataGoLocator.Resolve(new AppSettings(), App, Data, systemPaths: [])!;

        k.Source.Should().Be(KataGoSource.Bundled);
        k.Options.Model.Should().EndWith("b10c128.txt.gz");
        k.Options.Config.Should().Be(Path.Combine(Data, "katago", KataGoLocator.ConfigName));
        File.ReadAllText(k.Options.Config).Should().Contain("numAnalysisThreads");
        k.Options.Validate().Should().BeNull();
        k.Options.OverrideArgument.Should().Be("reportAnalysisWinratesAs=BLACK,logDir=" + Path.Combine(Data, "katago", "logs"));
    }

    [Fact]
    public void An_installed_copy_comes_next_and_preferences_win_over_both()
    {
        string installed = Directory.CreateDirectory(Path.Combine(Data, "katago")).FullName;
        File.WriteAllText(Path.Combine(installed, KataGoLocator.ExecutableName), "x");
        File.WriteAllText(Path.Combine(installed, "net.bin.gz"), "net");
        KataGoLocator.Resolve(new AppSettings(), App, Data, systemPaths: [])!.Source.Should().Be(KataGoSource.Installed);

        string own = Directory.CreateDirectory(Path.Combine(_root, "own")).FullName;
        string exe = Path.Combine(own, "katago");
        string net = Path.Combine(own, "m.bin.gz");
        File.WriteAllText(exe, "x");
        File.WriteAllText(net, "net");
        var configured = new AppSettings { KataGoExecutable = exe, KataGoModel = net };
        ResolvedKataGo k = KataGoLocator.Resolve(configured, App, Data, systemPaths: [])!;
        k.Source.Should().Be(KataGoSource.Configured);
        k.Options.Executable.Should().Be(exe);
        k.Options.Model.Should().Be(net);
        k.Options.Config.Should().EndWith(KataGoLocator.ConfigName, "an empty config falls back to Hoshi's");
        k.Options.Overrides.Should().BeNull();
    }

    [Fact]
    public void Stale_preferences_paths_fall_back_to_the_bundled_copy()
    {
        string folder = Directory.CreateDirectory(Path.Combine(App, "katago")).FullName;
        File.WriteAllText(Path.Combine(folder, KataGoLocator.ExecutableName), "x");
        File.WriteAllText(Path.Combine(folder, "b10c128.txt.gz"), "net");
        var stale = new AppSettings
        {
            KataGoExecutable = Path.Combine(_root, "gone", "katago.exe"),
            KataGoModel = Path.Combine(_root, "gone", "model.txt"),
            KataGoConfig = Path.Combine(_root, "gone", "analysis_example.cfg"),
        };

        ResolvedKataGo k = KataGoLocator.Resolve(stale, App, Data, systemPaths: [])!;

        k.Source.Should().Be(KataGoSource.Bundled);
        k.Options.Model.Should().EndWith("b10c128.txt.gz");
        k.Options.Config.Should().EndWith(KataGoLocator.ConfigName);
    }

    [Fact]
    public void A_configured_executable_with_a_missing_model_uses_hoshis_network()
    {
        string folder = Directory.CreateDirectory(Path.Combine(App, "katago")).FullName;
        File.WriteAllText(Path.Combine(folder, "b10c128.txt.gz"), "net");
        string exe = Path.Combine(_root, "katago");
        File.WriteAllText(exe, "x");

        ResolvedKataGo k = KataGoLocator.Resolve(new AppSettings { KataGoExecutable = exe, KataGoModel = Path.Combine(_root, "gone.bin.gz") }, App, Data, systemPaths: [])!;

        k.Source.Should().Be(KataGoSource.Configured);
        k.Options.Model.Should().EndWith("b10c128.txt.gz");
    }

    [Fact]
    public void A_system_katago_is_used_once_hoshi_has_a_network()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string brew = Path.Combine(_root, "katago");
        File.WriteAllText(brew, "x");
        KataGoLocator.Resolve(new AppSettings(), App, Data, systemPaths: [brew]).Should().BeNull("no network yet");

        Directory.CreateDirectory(Path.Combine(Data, "katago"));
        File.WriteAllText(Path.Combine(Data, "katago", "b10c128.txt.gz"), "net");
        ResolvedKataGo k = KataGoLocator.Resolve(new AppSettings(), App, Data, systemPaths: [brew])!;
        k.Source.Should().Be(KataGoSource.System);
        k.Options.Executable.Should().Be(brew);
    }

    [Fact]
    public void Only_the_program_libraries_and_certificates_are_unpacked()
    {
        string zip = Path.Combine(_root, "k.zip");
        using (ZipArchive a = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (string name in new[] { "katago.exe", "zip.dll", "cacert.pem", "README.txt", "default_gtp.cfg", "analysis_example.cfg" })
            {
                using StreamWriter w = new(a.CreateEntry(name).Open());
                w.Write(name);
            }
        }

        string target = Directory.CreateDirectory(Path.Combine(_root, "out")).FullName;
        KataGoInstaller.Unpack(zip, target);

        Directory.EnumerateFiles(target).Select(Path.GetFileName).Should().BeEquivalentTo("katago.exe", "zip.dll", "cacert.pem", "KataGo-README.txt");
    }

    [Fact]
    public void Builds_exist_for_windows_and_linux_x64_only()
    {
        KataGoInstaller.EngineFor(OSPlatform.Windows, Architecture.X64).Should().Be(KataGoInstaller.WindowsX64);
        KataGoInstaller.EngineFor(OSPlatform.Linux, Architecture.X64).Should().Be(KataGoInstaller.LinuxX64);
        KataGoInstaller.EngineFor(OSPlatform.OSX, Architecture.Arm64).Should().BeNull();
        KataGoInstaller.EngineFor(OSPlatform.Linux, Architecture.Arm64).Should().BeNull();
        KataGoInstaller.Model.Sha256.Should().HaveLength(64);
    }
}

public sealed class KataGoSetupViewModelTests
{
    private sealed class FakeInstaller : IKataGoInstaller
    {
        public bool IsSupported { get; set; } = true;

        public int DownloadMegabytes => 20;

        public bool Fail { get; set; }

        public int Calls { get; private set; }

        public Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            progress?.Report(1);
            return Fail ? Task.FromException(new HttpRequestException("offline")) : Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Installs_and_reports_or_explains_the_failure()
    {
        var installer = new FakeInstaller();
        var vm = new KataGoSetupViewModel(installer, settings: new TestSettings());
        vm.InstallLabel.Should().Be("Instalar KataGo (≈20 MB)");

        await vm.InstallCommand.ExecuteAsync(null);
        installer.Calls.Should().Be(1);
        vm.StatusText.Should().Be("KataGo está listo. Se prepara en segundo plano.");

        installer.Fail = true;
        await vm.InstallCommand.ExecuteAsync(null);
        vm.StatusText.Should().Be("No se pudo instalar KataGo: offline");
    }

    [Fact]
    public void Not_now_is_remembered_and_unsupported_systems_get_the_homebrew_hint()
    {
        var settings = new TestSettings();
        var vm = new KataGoSetupViewModel(new FakeInstaller { IsSupported = false }, settings: settings);
        vm.CanInstallHere.Should().BeFalse();
        vm.Explanation.Should().Contain("brew install katago");

        vm.NotNowCommand.Execute(null);
        settings.Current.KataGoPromptDismissed.Should().BeTrue();
        vm.IsPromptVisible.Should().BeFalse();
    }
}
