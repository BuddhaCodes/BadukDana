using System.Text;
using System.Text.RegularExpressions;

namespace Hoshi.App.Services.Diagnostics;

/// <summary>
/// "Report a problem": a GitHub issue pre-filled with the version, the system, the last crash and the end of the
/// log. It only opens in the player's browser; they read it, edit it and decide whether to send it. Secrets
/// (tokens, JWTs, codes, passwords) and the home folder (which holds the account name) are removed first.
/// </summary>
public static partial class ProblemReport
{
    /// <summary>Browsers and GitHub cope with this; longer bodies are cut (the stack and the log first).</summary>
    public const int MaxUrlLength = 7000;

    private static readonly Uri NewIssue = new($"https://github.com/{Updates.GitHubReleaseSource.Repository}/issues/new");

    /// <summary>Removes anything that could identify the player or give access to their accounts.</summary>
    public static string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string s = Jwt().Replace(text, "[jwt]");
        s = Secret().Replace(s, m => m.Groups["key"].Value + m.Groups["sep"].Value + "[removed]");
        s = Bearer().Replace(s, "Bearer [removed]");
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 3)
        {
            s = s.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
        }

        return s;
    }

    /// <summary>The last <paramref name="lines"/> lines of the newest log file, scrubbed (empty when there is none).</summary>
    public static IReadOnlyList<string> LogTail(string logDirectory, int lines = 40)
    {
        try
        {
            FileInfo? newest = new DirectoryInfo(logDirectory).Exists
                ? new DirectoryInfo(logDirectory).GetFiles("hoshi-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (newest is null)
            {
                return [];
            }

            // Serilog keeps the file open: share it.
            using var stream = new FileStream(newest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var tail = new Queue<string>(lines);
            while (reader.ReadLine() is { } line)
            {
                if (tail.Count == lines)
                {
                    tail.Dequeue();
                }

                tail.Enqueue(Scrub(line));
            }

            return [.. tail];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The issue's text (Markdown). Used for the URL and for "Copy the report".</summary>
    public static string Body(CrashRecord? crash, IReadOnlyList<string> logTail, string version, string system, int maxStack = 3000)
    {
        var b = new StringBuilder();
        b.AppendLine("**What happened?**").AppendLine().AppendLine("<!-- What were you doing? What did you expect? -->").AppendLine();
        b.AppendLine("**System**").AppendLine();
        b.AppendLine($"- Hoshi {version}");
        b.AppendLine($"- {system}");
        if (crash is not null)
        {
            b.AppendLine().AppendLine($"**Last error** ({crash.When:yyyy-MM-dd HH:mm} UTC, {crash.Context}{(crash.Fatal ? ", Hoshi closed" : string.Empty)})").AppendLine();
            b.AppendLine("```");
            string stack = crash.Stack.Length > maxStack ? crash.Stack[..maxStack] + "\n…" : crash.Stack;
            b.AppendLine(stack);
            b.AppendLine("```");
        }

        if (logTail.Count > 0)
        {
            b.AppendLine().AppendLine("<details><summary>End of the log</summary>").AppendLine().AppendLine("```");
            foreach (string line in logTail)
            {
                b.AppendLine(line);
            }

            b.AppendLine("```").AppendLine("</details>");
        }

        return b.ToString();
    }

    /// <summary>The "new issue" link, shortened until it fits <see cref="MaxUrlLength"/>.</summary>
    public static Uri IssueUrl(CrashRecord? crash, IReadOnlyList<string> logTail, string version, string system)
    {
        string title = crash is null ? "Problem report" : $"Crash: {crash.Type.Split('.')[^1]} ({crash.Context})";
        int stack = 3000;
        var log = logTail.ToList();
        while (true)
        {
            string url = $"{NewIssue}?labels=bug&title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(Body(crash, log, version, system, stack))}";
            if (url.Length <= MaxUrlLength || (log.Count == 0 && stack <= 200))
            {
                return new Uri(url);
            }

            if (log.Count > 0)
            {
                log.RemoveAt(0);
            }
            else
            {
                stack /= 2;
            }
        }
    }

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<key>access_token|refresh_token|id_token|token|code_verifier|code|password|client_secret|jwt)(?<sep>""?\s*[=:]\s*""?)[^\s&"",;]+", RegexOptions.IgnoreCase)]
    private static partial Regex Secret();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();
}
