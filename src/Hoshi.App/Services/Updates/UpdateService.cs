using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.Services.Updates;

/// <summary>
/// Puts a downloaded release in place of the running copy without an installer: files that are replaced are first
/// renamed to <c>*.old</c> (allowed even for the running executable on Windows, Linux and macOS), the new ones are
/// moved in, and the leftovers are removed on the next start. A Mac bundle is swapped as a whole.
/// </summary>
public static class UpdateInstaller
{
    public const string OldSuffix = ".old";
    public const string StagingName = ".hoshi-update";

    /// <summary>Installs <paramref name="archive"/> over <paramref name="target"/>; returns what to launch.</summary>
    public static string Install(string archive, InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Kind switch
        {
            InstallKind.Folder => InstallFolder(archive, target),
            InstallKind.MacBundle => InstallBundle(archive, target),
            _ => throw new InvalidOperationException("A development build cannot update itself."),
        };
    }

    /// <summary>Removes what a previous update left behind (old files, staging folder). Never throws.</summary>
    public static void CleanUp(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            switch (target.Kind)
            {
                case InstallKind.Folder:
                    TryDeleteDirectory(Path.Combine(target.Root, StagingName));
                    foreach (string old in Directory.EnumerateFiles(target.Root, "*" + OldSuffix, SearchOption.AllDirectories))
                    {
                        TryDeleteFile(old);
                    }

                    break;
                case InstallKind.MacBundle:
                    TryDeleteDirectory(target.Root + OldSuffix);
                    TryDeleteDirectory(Path.Combine(Path.GetDirectoryName(target.Root)!, StagingName));
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: they are retried on the next start.
        }
    }

    /// <summary>True when Hoshi may write where it is installed (otherwise the update page is opened instead).</summary>
    public static bool CanWrite(InstallTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Kind == InstallKind.Development)
        {
            return false;
        }

        string folder = target.Kind == InstallKind.MacBundle ? Path.GetDirectoryName(target.Root)! : target.Root;
        string probe = Path.Combine(folder, ".hoshi-write-test");
        try
        {
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string InstallFolder(string archive, InstallTarget target)
    {
        string staging = Path.Combine(target.Root, StagingName);
        TryDeleteDirectory(staging);
        Directory.CreateDirectory(staging);
        Extract(archive, staging);
        string source = Directory.Exists(Path.Combine(staging, "Hoshi")) ? Path.Combine(staging, "Hoshi") : staging;
        string exeName = Path.GetFileName(target.Executable);
        if (!File.Exists(Path.Combine(source, exeName)))
        {
            throw new InvalidDataException($"The update does not contain {exeName}.");
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target.Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (File.Exists(destination))
            {
                File.Move(destination, destination + OldSuffix, overwrite: true);
            }

            File.Move(file, destination);
        }

        string executable = Path.Combine(target.Root, exeName);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }

        TryDeleteDirectory(staging);
        return executable;
    }

    private static string InstallBundle(string archive, InstallTarget target)
    {
        string parent = Path.GetDirectoryName(target.Root)!;
        string staging = Path.Combine(parent, StagingName);
        TryDeleteDirectory(staging);
        Directory.CreateDirectory(staging);

        // ditto keeps the bundle's permissions, symlinks and signature; .NET's zip reader would not.
        RunOrThrow("/usr/bin/ditto", ["-x", "-k", archive, staging]);
        string app = Directory.EnumerateDirectories(staging, "*.app").FirstOrDefault()
            ?? throw new InvalidDataException("The update does not contain Hoshi.app.");

        string old = target.Root + OldSuffix;
        TryDeleteDirectory(old);
        Directory.Move(target.Root, old);
        try
        {
            Directory.Move(app, target.Root);
        }
        catch
        {
            Directory.Move(old, target.Root); // put the running copy back
            throw;
        }

        TryDeleteDirectory(staging);
        return target.Root;
    }

    private static void Extract(string archive, string destination)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, destination, overwriteFiles: true);
            return;
        }

        using FileStream file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: true);
    }

    private static void RunOrThrow(string program, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardError = true };
        foreach (string a in args)
        {
            info.ArgumentList.Add(a);
        }

        using Process process = Process.Start(info) ?? throw new IOException($"Could not start {program}.");
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"{Path.GetFileName(program)} failed ({process.ExitCode}): {error.Trim()}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retried on the next start.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still in use (the old executable while it runs): retried on the next start.
        }
    }
}

/// <summary>Checking for, downloading and installing new Hoshi releases.</summary>
public interface IUpdateService
{
    InstallTarget Target { get; }

    Version CurrentVersion { get; }

    /// <summary>True when this copy can replace itself (a published build in a writable place).</summary>
    bool CanInstall { get; }

    /// <summary>The latest release when it is newer than this build (and has a file for this system), else null.</summary>
    Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads this system's file and checks it against the release's SHA256SUMS.txt; returns its path.</summary>
    Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Installs a downloaded file; returns what to start.</summary>
    string Install(string archive);

    /// <summary>Starts the new version, which waits for this process to exit before it opens.</summary>
    void Launch(string installed);
}

public sealed class UpdateService : IUpdateService
{
    public const string ChecksumsAsset = "SHA256SUMS.txt";
    public const string WaitArgument = "--wait-for-pid";

    private readonly IReleaseSource _source;
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _downloads;
    private bool? _canInstall;

    public UpdateService(IReleaseSource source, HttpClient http, ILogger<UpdateService>? logger = null, InstallTarget? target = null, Version? current = null, string? downloadDirectory = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? (ILogger)NullLogger.Instance;
        Target = target ?? UpdatePlatform.DetectCurrent();
        CurrentVersion = current ?? UpdatePlatform.CurrentVersion;
        _downloads = downloadDirectory ?? Path.Combine(Path.GetTempPath(), "hoshi-update");
        UpdateInstaller.CleanUp(Target);
    }

    public InstallTarget Target { get; }

    public Version CurrentVersion { get; }

    public bool CanInstall => _canInstall ??= UpdateInstaller.CanWrite(Target);

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        ReleaseInfo? latest = await _source.GetLatestAsync(cancellationToken);
        if (latest is null || latest.Version <= CurrentVersion)
        {
            return null;
        }

        if (latest.Asset(Target.AssetName) is null)
        {
            _logger.LogInformation("Hoshi {Version} is out but has no {Asset}", latest.Tag, Target.AssetName);
            return null;
        }

        _logger.LogInformation("Hoshi {Version} is available (this is {Current})", latest.Tag, UpdatePlatform.Display(CurrentVersion));
        return latest;
    }

    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ReleaseAsset asset = release.Asset(Target.AssetName) ?? throw new InvalidOperationException($"The release has no {Target.AssetName}.");
        Directory.CreateDirectory(_downloads);
        string path = Path.Combine(_downloads, asset.Name);
        string partial = path + ".part";

        using (HttpResponseMessage response = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? asset.Size;
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream output = File.Create(partial);
            byte[] buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                if (total > 0)
                {
                    progress?.Report(Math.Min(1, (double)done / total));
                }
            }
        }

        if (release.Asset(ChecksumsAsset) is { } sums)
        {
            string listing = await _http.GetStringAsync(sums.Url, cancellationToken);
            string expected = UpdatePlatform.ChecksumFor(listing, asset.Name)
                ?? throw new InvalidDataException($"{ChecksumsAsset} does not list {asset.Name}.");
            string actual;
            await using (FileStream check = File.OpenRead(partial))
            {
                actual = Convert.ToHexString(await SHA256.HashDataAsync(check, cancellationToken)).ToLowerInvariant();
            }

            if (actual != expected)
            {
                File.Delete(partial);
                throw new InvalidDataException("The download is damaged (its checksum does not match). Please try again.");
            }
        }

        File.Move(partial, path, overwrite: true);
        return path;
    }

    public string Install(string archive)
    {
        string installed = UpdateInstaller.Install(archive, Target);
        _logger.LogInformation("Installed the update from {Archive}", Path.GetFileName(archive));
        TryDelete(archive);
        return installed;
    }

    public void Launch(string installed)
    {
        string pid = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ProcessStartInfo info;
        if (Target.Kind == InstallKind.MacBundle)
        {
            info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
            foreach (string a in new[] { "-n", installed, "--args", WaitArgument, pid })
            {
                info.ArgumentList.Add(a);
            }
        }
        else
        {
            info = new ProcessStartInfo(installed) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(installed)! };
            info.ArgumentList.Add(WaitArgument);
            info.ArgumentList.Add(pid);
        }

        Process.Start(info)?.Dispose();
    }

    /// <summary>
    /// Called first thing in Main: when started by an update, waits (up to 20 s) for the previous version to exit,
    /// so the two never run at the same time.
    /// </summary>
    public static void WaitForPreviousInstance(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        int i = Array.IndexOf(args, WaitArgument);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pid))
        {
            return;
        }

        try
        {
            using Process previous = Process.GetProcessById(pid);
            previous.WaitForExit(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp file; the OS cleans it up eventually.
        }
    }
}
