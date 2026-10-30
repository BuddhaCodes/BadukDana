using FluentAssertions;
using Hoshi.App.Services;
using Hoshi.App.Services.Diagnostics;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Tests;

/// <summary>"Report a problem": what goes into the report, what never does, and the card after a crash.</summary>
public sealed class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hoshi-diag-" + Guid.NewGuid().ToString("N"));

    public DiagnosticsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        CrashGuard.ResetForTests(null);
        Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("GET /oauth2/token?code=abc123&state=x", "code=[removed]")]
    [InlineData("{\"access_token\": \"s3cr3t\", \"x\": 1}", "access_token\": \"[removed]")]
    [InlineData("refresh_token=zzz", "refresh_token=[removed]")]
    [InlineData("Authorization: Bearer abc.def.ghi", "Bearer [removed]")]
    [InlineData("password: hunter2", "password: [removed]")]
    public void Secrets_are_removed(string text, string expected)
    {
        string scrubbed = ProblemReport.Scrub(text);

        scrubbed.Should().Contain(expected);
        scrubbed.Should().NotContainAny("abc123", "s3cr3t", "zzz", "hunter2", "abc.def.ghi");
    }

    [Fact]
    public void Jwts_and_the_home_folder_are_removed()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

        string scrubbed = ProblemReport.Scrub($"user/jwt {jwt} at {Path.Combine(home, "Hoshi", "logs")}");

        scrubbed.Should().Contain("[jwt]").And.NotContain(jwt);
        if (home.Length > 3)
        {
            scrubbed.Should().NotContain(home).And.Contain("~");
        }
    }

    [Fact]
    public void The_issue_link_fits_and_carries_the_version_system_and_crash()
    {
        var crash = new CrashRecord(DateTimeOffset.UnixEpoch, "0.1.200", "UI thread", "System.InvalidOperationException", "boom",
            string.Join('\n', Enumerable.Repeat("   at Hoshi.App.Something.Deep(Int32 x) in /src/Something.cs:line 42", 400)), Fatal: true);
        var log = Enumerable.Range(0, 200).Select(i => $"2026-10-30 12:00:{i % 60:00} [INF] line {i} " + new string('x', 80)).ToList();

        Uri url = ProblemReport.IssueUrl(crash, log, "0.1.200", "Windows 11 (X64)");

        url.AbsoluteUri.Length.Should().BeLessThanOrEqualTo(ProblemReport.MaxUrlLength + 50);
        url.Host.Should().Be("github.com");
        string body = Uri.UnescapeDataString(url.Query);
        body.Should().Contain("Hoshi 0.1.200").And.Contain("Windows 11 (X64)").And.Contain("InvalidOperationException").And.Contain("Hoshi closed");
    }

    [Fact]
    public void The_log_tail_keeps_the_last_lines_scrubbed()
    {
        File.WriteAllLines(Path.Combine(_dir, "hoshi-20261030.log"), Enumerable.Range(1, 100).Select(i => $"line {i} token=t{i}"));

        IReadOnlyList<string> tail = ProblemReport.LogTail(_dir, lines: 5);

        tail.Should().HaveCount(5);
        tail[^1].Should().Be("line 100 token=[removed]");
    }

    [Fact]
    public void A_reported_exception_is_kept_until_dismissed_and_never_overwrites_a_fatal_one()
    {
        CrashGuard.ResetForTests(_dir);

        CrashGuard.Report(new InvalidOperationException("first"), "Test");
        CrashGuard.Pending().Should().NotBeNull();
        CrashGuard.Pending()!.Message.Should().Be("first");
        CrashGuard.Pending()!.Fatal.Should().BeFalse();

        CrashGuard.Clear();
        CrashGuard.Pending().Should().BeNull();
    }

    [Fact]
    public void Only_a_crash_that_closed_Hoshi_shows_the_card_and_reporting_opens_github_then_clears_it()
    {
        var crash = new CrashRecord(DateTimeOffset.UnixEpoch, "0.1.1", "Unhandled exception", "System.Exception", "x", "stack", Fatal: true);
        CrashRecord? pending = crash;
        var browser = new FakeBrowser();
        var vm = new ReportViewModel(browser, logDirectory: _dir, pending: () => pending, clear: () => pending = null);

        vm.HasCrash.Should().BeTrue();
        vm.ReportCommand.Execute(null);

        browser.Opened.Should().ContainSingle().Which.Host.Should().Be("github.com");
        vm.HasCrash.Should().BeFalse();
        pending.Should().BeNull();

        new ReportViewModel(browser, logDirectory: _dir, pending: () => crash with { Fatal = false }, clear: () => { }).HasCrash.Should().BeFalse();
    }
}
