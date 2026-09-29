using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Hoshi.Ogs.Auth;
using Microsoft.Extensions.Logging;

namespace Hoshi.App.Services;

/// <summary>
/// Chooses the operating system's secure store for OAuth tokens (CLAUDE.md §2 <c>ISecureStore</c>):
/// DPAPI on Windows, the login Keychain on macOS and the Secret Service (<c>secret-tool</c>) on Linux.
/// When the platform store is unavailable, tokens are kept in memory only: the user signs in again next time,
/// but nothing is ever written to disk in clear text.
/// </summary>
public static class SecureTokenStore
{
    public const string ServiceName = "Hoshi";

    public static ITokenStore Create(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ILogger logger = loggerFactory.CreateLogger(typeof(SecureTokenStore));
        ITokenStore primary;
        if (OperatingSystem.IsWindows())
        {
            primary = new DpapiTokenStore(Path.Combine(AppPaths.DataDirectory, "secrets"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            primary = new KeychainTokenStore(new ProcessRunner());
        }
        else
        {
            primary = new SecretToolTokenStore(new ProcessRunner());
        }

        return new FallbackTokenStore(primary, logger);
    }
}

/// <summary>Volatile store: used in tests and as the fallback when no OS store is available.</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    private readonly Dictionary<string, string> _values = [];
    private readonly Lock _gate = new();

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_values.TryGetValue(key, out string? v) ? v : null);
        }
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _values[key] = value;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _values.Remove(key);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Uses <paramref name="primary"/> until it fails once, then falls back to memory for the session.</summary>
internal sealed class FallbackTokenStore(ITokenStore primary, ILogger logger) : ITokenStore
{
    private readonly MemoryTokenStore _memory = new();
    private volatile bool _degraded;

    public bool IsDegraded => _degraded;

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        if (!_degraded)
        {
            try
            {
                return await primary.ReadAsync(key, cancellationToken);
            }
            catch (SecureStoreUnavailableException ex)
            {
                Degrade(ex);
            }
        }

        return await _memory.ReadAsync(key, cancellationToken);
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (!_degraded)
        {
            try
            {
                await primary.WriteAsync(key, value, cancellationToken);
                return;
            }
            catch (SecureStoreUnavailableException ex)
            {
                Degrade(ex);
            }
        }

        await _memory.WriteAsync(key, value, cancellationToken);
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        await _memory.DeleteAsync(key, cancellationToken);
        if (!_degraded)
        {
            try
            {
                await primary.DeleteAsync(key, cancellationToken);
            }
            catch (SecureStoreUnavailableException ex)
            {
                Degrade(ex);
            }
        }
    }

    private void Degrade(SecureStoreUnavailableException ex)
    {
        _degraded = true;
        logger.LogWarning(
            "Secure credential store unavailable ({Reason}); the OGS session will not be remembered after closing Hoshi",
            ex.Message);
    }
}

public sealed class SecureStoreUnavailableException : Exception
{
    public SecureStoreUnavailableException()
    {
    }

    public SecureStoreUnavailableException(string message)
        : base(message)
    {
    }

    public SecureStoreUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Result of running an external command.</summary>
internal readonly record struct ProcessResult(int ExitCode, string StandardOutput);

/// <summary>Runs a command with its arguments passed as a list (no shell). Secrets travel over stdin only.</summary>
internal interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken);
}

internal sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string? standardInput, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in arguments)
        {
            info.ArgumentList.Add(a);
        }

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new SecureStoreUnavailableException($"{fileName} did not start");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new SecureStoreUnavailableException($"{fileName} is not installed", ex);
        }

        using (process)
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), cancellationToken);
            }

            process.StandardInput.Close();
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stderr; // Drained, never logged: it may echo input.
            return new ProcessResult(process.ExitCode, await stdout);
        }
    }
}

/// <summary>Linux Secret Service through libsecret's <c>secret-tool</c>. The secret is written to its stdin.</summary>
internal sealed class SecretToolTokenStore(IProcessRunner runner) : ITokenStore
{
    private const string Tool = "secret-tool";

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        ProcessResult r = await runner.RunAsync(Tool, ["lookup", .. Attributes(key)], null, cancellationToken);
        return r.ExitCode == 0 && r.StandardOutput.Length > 0 ? r.StandardOutput.TrimEnd('\n') : null;
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        ProcessResult r = await runner.RunAsync(
            Tool, ["store", $"--label={SecureTokenStore.ServiceName} ({key})", .. Attributes(key)], value, cancellationToken);
        if (r.ExitCode != 0)
        {
            throw new SecureStoreUnavailableException($"{Tool} store failed with exit code {r.ExitCode}");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await runner.RunAsync(Tool, ["clear", .. Attributes(key)], null, cancellationToken);

    private static string[] Attributes(string key) => ["application", "hoshi", "key", key];
}

/// <summary>
/// macOS login Keychain through <c>security</c>. Writes go through <c>security -i</c> (commands on stdin) so the
/// token never appears in the process list.
/// </summary>
internal sealed class KeychainTokenStore(IProcessRunner runner) : ITokenStore
{
    private const string Tool = "/usr/bin/security";
    private const int ItemNotFound = 44;

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        ProcessResult r = await runner.RunAsync(
            Tool, ["find-generic-password", "-s", SecureTokenStore.ServiceName, "-a", key, "-w"], null, cancellationToken);
        return r.ExitCode switch
        {
            0 => r.StandardOutput.TrimEnd('\n'),
            ItemNotFound => null,
            _ => throw new SecureStoreUnavailableException($"security find-generic-password failed with exit code {r.ExitCode}"),
        };
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        string command = $"add-generic-password -U -s {Quote(SecureTokenStore.ServiceName)} -a {Quote(key)} -w {Quote(value)}\n";
        ProcessResult r = await runner.RunAsync(Tool, ["-i"], command, cancellationToken);
        if (r.ExitCode != 0)
        {
            throw new SecureStoreUnavailableException($"security add-generic-password failed with exit code {r.ExitCode}");
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        await runner.RunAsync(Tool, ["delete-generic-password", "-s", SecureTokenStore.ServiceName, "-a", key], null, cancellationToken);

    internal static string Quote(string s)
    {
        if (s.Any(c => c is '\n' or '\r' or '\0'))
        {
            throw new ArgumentException("Keychain values cannot contain line breaks.", nameof(s));
        }

        return "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }
}

/// <summary>Windows DPAPI (current user scope): one encrypted file per key under the app data folder.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DpapiTokenStore(string directory) : ITokenStore
{
    private static readonly byte[] Entropy = "Hoshi.OGS.v1"u8.ToArray();

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        string path = PathFor(key);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] data = await File.ReadAllBytesAsync(path, cancellationToken);
        try
        {
            return Encoding.UTF8.GetString(Dpapi.Unprotect(data, Entropy));
        }
        catch (SecureStoreUnavailableException)
        {
            // Encrypted for another user or machine: treat as absent.
            File.Delete(path);
            return null;
        }
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        byte[] data = Dpapi.Protect(Encoding.UTF8.GetBytes(value), Entropy);
        string path = PathFor(key);
        string temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, data, cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        File.Delete(PathFor(key));
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        string safe = new(key.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return Path.Combine(directory, safe + ".bin");
    }
}

[SupportedOSPlatform("windows")]
internal static class Dpapi
{
    private const int CryptProtectUiForbidden = 0x1;

    public static byte[] Protect(byte[] data, byte[] entropy) => Run(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => Run(data, entropy, protect: false);

    private static byte[] Run(byte[] data, byte[] entropy, bool protect)
    {
        GCHandle dataHandle = GCHandle.Alloc(data, GCHandleType.Pinned);
        GCHandle entropyHandle = GCHandle.Alloc(entropy, GCHandleType.Pinned);
        var output = default(DataBlob);
        try
        {
            var input = new DataBlob { Size = data.Length, Data = dataHandle.AddrOfPinnedObject() };
            var extra = new DataBlob { Size = entropy.Length, Data = entropyHandle.AddrOfPinnedObject() };
            bool ok = protect
                ? NativeMethods.CryptProtectData(ref input, null, ref extra, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output)
                : NativeMethods.CryptUnprotectData(ref input, IntPtr.Zero, ref extra, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out output);
            if (!ok)
            {
                throw new SecureStoreUnavailableException($"DPAPI failed with error {Marshal.GetLastPInvokeError()}");
            }

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        finally
        {
            dataHandle.Free();
            entropyHandle.Free();
            if (output.Data != IntPtr.Zero)
            {
                NativeMethods.LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    private static class NativeMethods
    {
        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptProtectData(
            ref DataBlob dataIn, string? description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CryptUnprotectData(
            ref DataBlob dataIn, IntPtr description, ref DataBlob entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern IntPtr LocalFree(IntPtr handle);
    }
}
