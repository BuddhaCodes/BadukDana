using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Hoshi.App.Services.Updates;

/// <summary>A downloadable file of a release.</summary>
public sealed record ReleaseAsset(string Name, Uri Url, long Size);

/// <summary>The newest published Hoshi release (only announced to copies that cannot update themselves).</summary>
public sealed record ReleaseInfo(Version Version, string Tag, Uri PageUrl, IReadOnlyList<ReleaseAsset> Assets, string? Notes)
{
    public ReleaseAsset? Asset(string name) => Assets.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
}

public interface IReleaseSource
{
    /// <summary>The latest release, or null when there is none yet.</summary>
    Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Reads the latest release of the Hoshi repository from the public GitHub API (no account, nothing sent but the
/// user agent): <c>GET https://api.github.com/repos/BuddhaCodes/BadukDana/releases/latest</c>.
/// </summary>
public sealed class GitHubReleaseSource(HttpClient http) : IReleaseSource
{
    public const string Repository = "BuddhaCodes/BadukDana";

    public static Uri LatestUrl { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static readonly Uri ReleasesPage = new($"https://github.com/{Repository}/releases/latest");

    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // no release published yet
        }

        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>Reads a GitHub release; drafts, pre-releases and tags that are not versions give null.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (Bool(root, "draft") || Bool(root, "prerelease")
            || Text(root, "tag_name") is not { } tag || UpdatePlatform.ParseVersion(tag) is not { } version)
        {
            return null;
        }

        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement a in list.EnumerateArray())
            {
                if (Text(a, "name") is { } name && Text(a, "browser_download_url") is { } url && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                {
                    assets.Add(new ReleaseAsset(name, uri, a.TryGetProperty("size", out JsonElement s) && s.TryGetInt64(out long size) ? size : 0));
                }
            }
        }

        Uri page = Text(root, "html_url") is { } html && Uri.TryCreate(html, UriKind.Absolute, out Uri? p) ? p : GitHubReleaseSource.ReleasesPage;
        return new ReleaseInfo(version, tag, page, assets, Text(root, "body"));
    }

    private static string? Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}

public static class UpdatePlatform
{
    public static OSPlatform CurrentOS =>
        OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;

    /// <summary>"v0.1.7", "0.1.7" or "0.1.7+abc" → 0.1.7.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string s = text.Trim().TrimStart('v', 'V');
        int cut = s.IndexOfAny(['+', '-', ' ']);
        if (cut >= 0)
        {
            s = s[..cut];
        }

        return Version.TryParse(s, out Version? v) ? Normalize(v) : null;
    }

    /// <summary>This build's version (the release workflow sets it, e.g. 0.1.7).</summary>
    public static Version CurrentVersion
    {
        get
        {
            Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(UpdatePlatform).Assembly;
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return ParseVersion(informational) ?? Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));
        }
    }

    public static string Display(Version v) => string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}");

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
