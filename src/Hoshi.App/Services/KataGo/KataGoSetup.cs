using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Hoshi.Engines.KataGo;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.KataGo;

/// <summary>Where the KataGo in use comes from.</summary>
public enum KataGoSource
{
    /// <summary>The paths chosen in Preferences.</summary>
    Configured,

    /// <summary>Shipped inside Hoshi's download (the <c>katago</c> folder next to the app).</summary>
    Bundled,

    /// <summary>Downloaded by Hoshi into its data folder ("Install KataGo").</summary>
    Installed,

    /// <summary>A katago found on the system (e.g. Homebrew on macOS) with Hoshi's network.</summary>
    System,
}

public sealed record ResolvedKataGo(KataGoOptions Options, KataGoSource Source);

/// <summary>
/// Finds a usable KataGo without asking the user: the Preferences paths when set, otherwise the copy bundled with
/// Hoshi, otherwise one installed by Hoshi, otherwise a system katago (Homebrew, /usr/bin) with Hoshi's network.
/// Bundled and installed copies run with Hoshi's own analysis config and log into the data folder.
/// </summary>
public static class KataGoLocator
{
    public const string FolderName = "katago";
    public const string ConfigName = "hoshi_analysis.cfg";

    /// <summary>Hoshi's analysis settings: two positions at a time, enough threads for integrated GPUs too.</summary>
    public const string DefaultConfig = """
        # Written by Hoshi. KataGo analysis engine settings used when Hoshi runs its own KataGo.
        # (Pick another analysis .cfg in Preferences → Analysis to override.)
        numAnalysisThreads = 2
        numSearchThreadsPerAnalysisThread = 8
        nnMaxBatchSize = 32
        nnCacheSizePowerOfTwo = 21
        nnMutexPoolSizePowerOfTwo = 17
        nnRandomize = true
        maxVisits = 500
        reportAnalysisWinratesAs = BLACK
        """;

    public const string GtpConfigName = "hoshi_gtp.cfg";

    /// <summary>
    /// Hoshi's GTP settings for playing against its KataGo: KataGo's defaults (500 visits per move, resigns when
    /// hopeless) with winrates from the side to move, as GTP analysis expects. Edit the file to change its strength.
    /// </summary>
    public const string DefaultGtpConfig = """
        # Written by Hoshi. KataGo GTP settings used when you play against Hoshi's KataGo (Preferences → Engines).
        # Change maxVisits (or add maxTime = seconds per move) to make it weaker or stronger.
        logAllGTPCommunication = false
        logSearchInfo = false
        logToStderr = false
        rules = japanese
        allowResignation = true
        resignThreshold = -0.90
        resignConsecTurns = 3
        maxVisits = 500
        ponderingEnabled = false
        numSearchThreads = 8
        nnCacheSizePowerOfTwo = 20
        reportAnalysisWinratesAs = SIDETOMOVE
        """;

    /// <summary>Phones: one analysis at a time on the CPU (Eigen), with fewer threads and a smaller cache.</summary>
    public const string PhoneConfig = """
        # Written by Hoshi for phones. KataGo analysis engine settings (CPU).
        numAnalysisThreads = 1
        numSearchThreadsPerAnalysisThread = 4
        numEigenThreadsPerModel = 4
        nnMaxBatchSize = 8
        nnCacheSizePowerOfTwo = 18
        nnMutexPoolSizePowerOfTwo = 14
        nnRandomize = true
        maxVisits = 200
        reportAnalysisWinratesAs = BLACK
        """;

    /// <summary>Phones: playing against KataGo answers within a few seconds.</summary>
    public const string PhoneGtpConfig = """
        # Written by Hoshi for phones. KataGo GTP settings used when you play against KataGo.
        # Change maxVisits or maxTime (seconds per move) to make it weaker or stronger.
        logAllGTPCommunication = false
        logSearchInfo = false
        logToStderr = false
        rules = japanese
        allowResignation = true
        resignThreshold = -0.90
        resignConsecTurns = 3
        maxVisits = 300
        maxTime = 5
        ponderingEnabled = false
        numSearchThreads = 4
        numEigenThreadsPerModel = 4
        nnCacheSizePowerOfTwo = 18
        reportAnalysisWinratesAs = SIDETOMOVE
        """;

    /// <summary>
    /// Set by platforms that ship KataGo inside the app (Android: <c>libkatago.so</c> in the native library folder,
    /// the network copied out of the APK). Used as the bundled copy.
    /// </summary>
    public static (string Executable, string ModelFolder)? PlatformBundle { get; set; }

    private static readonly string[] SystemPaths = ["/opt/homebrew/bin/katago", "/usr/local/bin/katago", "/usr/bin/katago"];

    public static string ExecutableName => OperatingSystem.IsWindows() ? "katago.exe" : "katago";

    /// <summary>Hoshi's own KataGo folder in the data directory (downloads, config, logs).</summary>
    public static string DataFolder(string dataDirectory) => Path.Combine(dataDirectory, FolderName);

    public static ResolvedKataGo? Resolve(AppSettings settings, string baseDirectory, string dataDirectory, IEnumerable<string>? systemPaths = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // Preferences win, but only while the executable is still there: stale paths (a folder moved or deleted)
        // fall through to the bundled / installed copy instead of leaving Hoshi without an engine.
        if (!string.IsNullOrWhiteSpace(settings.KataGoExecutable) && File.Exists(settings.KataGoExecutable))
        {
            string exe = settings.KataGoExecutable;
            string model = settings.KataGoModel is { Length: > 0 } m && File.Exists(m) ? m : string.Empty;
            string config = settings.KataGoConfig is { Length: > 0 } c && File.Exists(c) ? c : EnsureConfig(dataDirectory);
            if (string.IsNullOrWhiteSpace(model))
            {
                model = FindModel(Path.Combine(baseDirectory, FolderName)) ?? FindModel(DataFolder(dataDirectory)) ?? string.Empty;
            }

            return new ResolvedKataGo(new KataGoOptions(exe, model, config), KataGoSource.Configured);
        }

        string logs = "logDir=" + Path.Combine(DataFolder(dataDirectory), "logs");
        if (PlatformBundle is { } bundle && File.Exists(bundle.Executable) && FindModel(bundle.ModelFolder) is { } bundledModel)
        {
            return new ResolvedKataGo(new KataGoOptions(bundle.Executable, bundledModel, EnsureConfig(dataDirectory), logs), KataGoSource.Bundled);
        }

        foreach ((string folder, KataGoSource source) in new[] { (Path.Combine(baseDirectory, FolderName), KataGoSource.Bundled), (DataFolder(dataDirectory), KataGoSource.Installed) })
        {
            string exe = Path.Combine(folder, ExecutableName);
            if (File.Exists(exe) && (FindModel(folder) ?? FindModel(DataFolder(dataDirectory))) is { } model)
            {
                return new ResolvedKataGo(new KataGoOptions(exe, model, EnsureConfig(dataDirectory), logs), source);
            }
        }

        if (!OperatingSystem.IsWindows()
            && (systemPaths ?? SystemPaths).FirstOrDefault(File.Exists) is { } system
            && (FindModel(Path.Combine(baseDirectory, FolderName)) ?? FindModel(DataFolder(dataDirectory))) is { } systemModel)
        {
            return new ResolvedKataGo(new KataGoOptions(system, systemModel, EnsureConfig(dataDirectory), logs), KataGoSource.System);
        }

        return null;
    }

    /// <summary>A system katago Hoshi could use once it has a network (macOS Homebrew, Linux packages), or null.</summary>
    public static string? FindSystemExecutable(IEnumerable<string>? systemPaths = null) =>
        OperatingSystem.IsWindows() ? null : (systemPaths ?? SystemPaths).FirstOrDefault(File.Exists);

    /// <summary>The network in a folder: a .bin.gz, .txt.gz or model .txt (largest first), or null.</summary>
    public static string? FindModel(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder)
            .Where(f =>
            {
                string name = Path.GetFileName(f).ToLowerInvariant();
                return name.EndsWith(".bin.gz", StringComparison.Ordinal) || name.EndsWith(".txt.gz", StringComparison.Ordinal)
                    || (name.EndsWith(".txt", StringComparison.Ordinal) && name != "readme.txt" && !name.StartsWith("license", StringComparison.Ordinal));
            })
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    /// <summary>Writes Hoshi's analysis config into the data folder (once) and returns its path.</summary>
    public static string EnsureConfig(string dataDirectory) => EnsureFile(dataDirectory, ConfigName, PlatformInfo.IsPhone ? PhoneConfig : DefaultConfig);

    /// <summary>Writes Hoshi's GTP config into the data folder (once) and returns its path.</summary>
    public static string EnsureGtpConfig(string dataDirectory) => EnsureFile(dataDirectory, GtpConfigName, PlatformInfo.IsPhone ? PhoneGtpConfig : DefaultGtpConfig);

    /// <summary>
    /// The resolved KataGo as a GTP engine: <c>katago gtp -model … -config hoshi_gtp.cfg</c>, logging into the data folder.
    /// </summary>
    public static Hoshi.Engines.Gtp.GtpEngineConfig GtpEngine(ResolvedKataGo kataGo, string dataDirectory, string name)
    {
        ArgumentNullException.ThrowIfNull(kataGo);
        string logs = Path.Combine(DataFolder(dataDirectory), "logs");
        string arguments = $"gtp -model {Quote(kataGo.Options.Model)} -config {Quote(EnsureGtpConfig(dataDirectory))} -override-config {Quote("logDir=" + logs)}";
        return new Hoshi.Engines.Gtp.GtpEngineConfig(name, kataGo.Options.Executable, arguments);
    }

    /// <summary>Hoshi's GTP config for KataGo's human-style model (after KataGo's gtp_human5k_example.cfg).</summary>
    public const string HumanGtpConfigName = "hoshi_gtp_human.cfg";

    public const string HumanGtpConfig = """
        # Written by Hoshi, after KataGo's gtp_human5k_example.cfg: KataGo playing like a human of a given rank with
        # its human model (-human-model). The rank (humanSLProfile) is set by Hoshi when a game starts.
        logAllGTPCommunication = false
        logSearchInfo = false
        logToStderr = false
        rules = japanese
        allowResignation = true
        resignThreshold = -0.99
        resignConsecTurns = 20
        resignMinScoreDifference = 40
        resignMinMovesPerBoardArea = 0.4
        maxVisits = 40
        numSearchThreads = 1
        humanSLProfile = rank_10k
        humanSLChosenMoveProp = 1.0
        humanSLChosenMoveIgnorePass = true
        humanSLChosenMovePiklLambda = 100000000
        humanSLRootExploreProbWeightless = 0.0
        humanSLRootExploreProbWeightful = 0.0
        humanSLPlaExploreProbWeightless = 0.0
        humanSLPlaExploreProbWeightful = 0.0
        humanSLOppExploreProbWeightless = 0.0
        humanSLOppExploreProbWeightful = 0.0
        humanSLCpuctExploration = 0.50
        humanSLCpuctPermanent = 0.2
        chosenMoveTemperatureEarly = 0.85
        chosenMoveTemperature = 0.70
        chosenMoveTemperatureHalflife = 80
        chosenMoveTemperatureOnlyBelowProb = 0.01
        ignorePreRootHistory = false
        rootNumSymmetriesToSample = 2
        useLcbForSelection = false
        staticScoreUtilityFactor = 0.30
        dynamicScoreUtilityFactor = 0.00
        useUncertainty = false
        subtreeValueBiasFactor = 0.0
        useNoisePruning = false
        """;

    /// <summary>
    /// KataGo with the human model: <c>katago gtp -model … -human-model … -config hoshi_gtp_human.cfg</c>; the rank
    /// goes in <c>-override-config humanSLProfile=rank_10k</c>.
    /// </summary>
    public static Hoshi.Engines.Gtp.GtpEngineConfig HumanGtpEngine(ResolvedKataGo kataGo, string humanModel, string dataDirectory, string name)
    {
        ArgumentNullException.ThrowIfNull(kataGo);
        string logs = Path.Combine(DataFolder(dataDirectory), "logs");
        string config = EnsureFile(dataDirectory, HumanGtpConfigName, HumanGtpConfig);
        string arguments = $"gtp -model {Quote(kataGo.Options.Model)} -human-model {Quote(humanModel)} -config {Quote(config)} -override-config {Quote("logDir=" + logs + ",humanSLProfile=rank_" + Hoshi.App.Services.Engines.GtpEngineHost.DefaultHumanLevel)}";
        return new Hoshi.Engines.Gtp.GtpEngineConfig(name, kataGo.Options.Executable, arguments);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string EnsureFile(string dataDirectory, string name, string content)
    {
        string folder = DataFolder(dataDirectory);
        string path = Path.Combine(folder, name);
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, content.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Validation will report the missing config.
        }

        return path;
    }
}

/// <summary>A file Hoshi downloads to set KataGo up, pinned by size and SHA-256.</summary>
public sealed record KataGoDownload(Uri Url, string Sha256, long Size, bool IsArchive);

/// <summary>
/// "Install KataGo": downloads the official KataGo build for this system from KataGo's GitHub releases and Hoshi's
/// default network from Hoshi's own release, checks both against pinned SHA-256 hashes, and unpacks them into the
/// data folder. macOS has no official build: a Homebrew katago (<c>brew install katago</c>) is used with the network.
/// </summary>
public sealed class KataGoInstaller(HttpClient http, string dataDirectory, ILogger<KataGoInstaller>? logger = null)
{
    public const string KataGoVersion = "v1.17.1";
    public const string ModelAsset = "Hoshi-katago-b10c128.txt.gz";

    /// <summary>KataGo's own builds (OpenCL: works on NVIDIA, AMD and Intel GPUs).</summary>
    public static readonly KataGoDownload WindowsX64 = new(
        new Uri($"https://github.com/lightvector/KataGo/releases/download/{KataGoVersion}/katago-{KataGoVersion}-opencl-windows-x64.zip"),
        "68d0a9b11ef7e3c1ddfc5bcd400306ca66c3770dd67a22cb377d3aaaf32e8c66", 5_347_873, IsArchive: true);

    public static readonly KataGoDownload LinuxX64 = new(
        new Uri($"https://github.com/lightvector/KataGo/releases/download/{KataGoVersion}/katago-{KataGoVersion}-opencl-linux-x64.zip"),
        "be537295868c0b8ff6985e62e411fff67cbba2dc872343c74896063de1ef51e9", 38_644_908, IsArchive: true);

    /// <summary>The default network (g170e b10c128, small and quick), published with every Hoshi release.</summary>
    public static readonly KataGoDownload Model = new(
        new Uri($"https://github.com/{Updates.GitHubReleaseSource.Repository}/releases/latest/download/{ModelAsset}"),
        ModelSha256, ModelSize, IsArchive: false);

    // engines/katago/b10c128.txt.gz (see engines/katago/README.md); the release workflow publishes it as ModelAsset.
    internal const string ModelSha256 = "abfa3600b8a47dc186343af82e435b3dae09df385744f85b1076ca9f0ffb2fd7";
    internal const long ModelSize = 14_269_470;

    private readonly ILogger _logger = logger ?? (ILogger)NullLogger.Instance;

    /// <summary>The KataGo build for this machine, or null when there is none (macOS, ARM: use a system katago).</summary>
    public static KataGoDownload? EngineFor(OSPlatform os, Architecture arch) =>
        arch != Architecture.X64 ? null
        : os == OSPlatform.Windows ? WindowsX64
        : os == OSPlatform.Linux ? LinuxX64
        : null;

    public static KataGoDownload? CurrentEngine =>
        EngineFor(Updates.UpdatePlatform.CurrentOS, RuntimeInformation.OSArchitecture);

    /// <summary>True when "Install KataGo" can work here (a build exists, or a system katago only needs the network).</summary>
    public static bool IsSupported => CurrentEngine is not null || KataGoLocator.FindSystemExecutable() is not null;

    /// <summary>Approximate download size in MB (for the button).</summary>
    public static int DownloadMegabytes =>
        (int)Math.Ceiling(((CurrentEngine?.Size ?? 0) + Math.Max(ModelSize, 14_300_000)) / 1_000_000.0);

    /// <summary>Downloads and unpacks; progress goes from 0 to 1 over both files.</summary>
    public async Task InstallAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string folder = KataGoLocator.DataFolder(dataDirectory);
        Directory.CreateDirectory(folder);
        KataGoDownload? engine = CurrentEngine;
        if (engine is null && KataGoLocator.FindSystemExecutable() is null)
        {
            throw new PlatformNotSupportedException(Core.Localization.Tr.T("KataGo.NoBuild"));
        }

        long total = (engine?.Size ?? 0) + Math.Max(Model.Size, 1);
        long before = 0;
        if (engine is not null)
        {
            string zip = await DownloadAsync(engine, folder, done => progress?.Report((double)done / total), cancellationToken);
            Unpack(zip, folder);
            File.Delete(zip);
            before = engine.Size;
        }

        if (KataGoLocator.FindModel(folder) is null)
        {
            string model = await DownloadAsync(Model, folder, done => progress?.Report((double)(before + done) / total), cancellationToken);
            File.Move(model, Path.Combine(folder, "b10c128.txt.gz"), overwrite: true);
        }

        progress?.Report(1);
        _logger.LogInformation("KataGo installed in {Folder}", folder);
    }

    private async Task<string> DownloadAsync(KataGoDownload file, string folder, Action<long> report, CancellationToken cancellationToken)
    {
        string path = Path.Combine(folder, Path.GetFileName(file.Url.LocalPath) + ".part");
        using (HttpResponseMessage response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream output = File.Create(path);
            byte[] buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                report(done);
            }
        }

        if (file.Sha256.Length == 64)
        {
            string actual;
            await using (FileStream check = File.OpenRead(path))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken)).ToLowerInvariant();
            }

            if (actual != file.Sha256)
            {
                File.Delete(path);
                throw new InvalidDataException(Core.Localization.Tr.T("KataGo.Damaged"));
            }
        }

        return path;
    }

    /// <summary>Takes the program, its libraries and certificates from KataGo's zip (not the example configs).</summary>
    internal static void Unpack(string zip, string folder)
    {
        using ZipArchive archive = ZipFile.OpenRead(zip);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = Path.GetFileName(entry.FullName);
            string lower = name.ToLower(CultureInfo.InvariantCulture);
            bool wanted = lower is "katago" or "katago.exe" or "cacert.pem" or "readme.txt" || lower.EndsWith(".dll", StringComparison.Ordinal);
            if (!wanted || name.Length == 0)
            {
                continue;
            }

            string target = Path.Combine(folder, lower == "readme.txt" ? "KataGo-README.txt" : name);
            entry.ExtractToFile(target, overwrite: true);
            if (!OperatingSystem.IsWindows() && lower == "katago")
            {
                File.SetUnixFileMode(target, File.GetUnixFileMode(target) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
        }
    }
}
