using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Hoshi.App.Services;
using Hoshi.App.Services.Backup;
using Hoshi.Sgf;

namespace Hoshi.App.Tests;

/// <summary>The .hoshi backup: what goes in, what never does, and merging from another device without losses.</summary>
public sealed class BackupTests : IDisposable
{
    private const string Game = "(;GM[1]FF[4]SZ[9]PB[Ana]PW[Ben];B[ee];W[cc];B[gg];W[cg];B[gc];W[dd];B[ed];W[dc];B[eb];W[ce];B[ef])";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hoshi-backup-" + Guid.NewGuid().ToString("N"));

    public BackupTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Device(string name)
    {
        string dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Write(string dir, string relative, string text)
    {
        string path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void A_backup_carries_games_and_progress_but_never_secrets_logs_or_paths()
    {
        string desktop = Device("desktop");
        var store = new ReplayStore(dataDirectory: desktop);
        store.Save(SgfParser.Parse(Game), "local-1", "local").Should().NotBeNull();
        Write(desktop, "joseki/progress.json", "[]");
        Write(desktop, "secrets/ogs.bin", "token");
        Write(desktop, "logs/hoshi-20261030.log", "log");
        Write(desktop, "cache/oje/x.json", "{}");
        Write(desktop, "settings.json", """{ "theme": "mochi", "language": "es", "kataGoExecutable": "C:/katago.exe", "sidebarWidth": 320 }""");

        string zip = Path.Combine(_root, "backup.hoshi");
        BackupPackage.Export(desktop, zip, "0.1.300");

        using ZipArchive archive = ZipFile.OpenRead(zip);
        string[] names = [.. archive.Entries.Select(e => e.FullName)];
        names.Should().Contain(["hoshi-backup.json", "replays/index.json", "joseki/progress.json", "settings.json"]);
        names.Should().NotContain(n => n.StartsWith("secrets", StringComparison.Ordinal) || n.StartsWith("logs", StringComparison.Ordinal) || n.StartsWith("cache", StringComparison.Ordinal));
        using var reader = new StreamReader(archive.GetEntry("settings.json")!.Open());
        JsonObject prefs = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        prefs.Select(p => p.Key).Should().BeEquivalentTo(["theme", "language"]);
    }

    [Fact]
    public void Importing_merges_games_cards_and_reviews_with_what_the_other_device_has()
    {
        string phone = Device("phone");
        string desktop = Device("desktop");
        new ReplayStore(dataDirectory: phone).Save(SgfParser.Parse(Game), "phone-game", "local");
        new ReplayStore(dataDirectory: desktop).Save(SgfParser.Parse(Game.Replace("Ana", "Cleo", StringComparison.Ordinal)), "desktop-game", "local");

        DateTimeOffset old = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), recent = old.AddDays(10);
        Write(phone, "joseki/progress.json", $$"""[{"id":"a","box":3,"due":"{{recent:O}}","reviews":5,"lapses":0,"lastReview":"{{recent:O}}"},{"id":"b","box":1,"due":"{{old:O}}","reviews":1,"lapses":0,"lastReview":"{{old:O}}"}]""");
        Write(desktop, "joseki/progress.json", $$"""[{"id":"a","box":1,"due":"{{old:O}}","reviews":2,"lapses":1,"lastReview":"{{old:O}}"},{"id":"b","box":4,"due":"{{recent:O}}","reviews":9,"lapses":0,"lastReview":"{{recent:O}}"}]""");
        Write(phone, "tsumego-progress.json", $$$"""{"p1":{"Box":2,"Due":"{{{recent:O}}}","Solved":3,"Failed":0,"LastSolved":"{{{recent:O}}}"}}""");
        Write(phone, "tsumego/from-my-games.sgf", "(;GM[1]SZ[9]AB[ee];B[cc])");
        Write(desktop, "tsumego/from-my-games.sgf", "(;GM[1]SZ[9]AB[gg];B[dd])");
        Write(phone, "settings.json", """{ "theme": "kuro", "language": "es" }""");
        Write(desktop, "settings.json", """{ "theme": "mochi", "language": "en", "kataGoExecutable": "/opt/katago" }""");

        string zip = Path.Combine(_root, "phone.hoshi");
        BackupPackage.Export(phone, zip, "0.1.300");
        BackupPackage.Stage(zip, desktop, applyPreferences: true);
        BackupPackage.HasPending(desktop).Should().BeTrue();
        BackupSummary summary = BackupPackage.ApplyPending(desktop)!;

        BackupPackage.HasPending(desktop).Should().BeFalse();
        summary.Games.Should().Be(1);
        summary.JosekiCards.Should().Be(1, "only card a was reviewed later on the phone");
        summary.ProblemCards.Should().Be(1);
        summary.Preferences.Should().BeTrue();

        new ReplayStore(dataDirectory: desktop).List().Select(e => e.Id).Should().BeEquivalentTo(["phone-game", "desktop-game"]);
        string joseki = File.ReadAllText(Path.Combine(desktop, "joseki", "progress.json"));
        JsonSerializer.Deserialize<List<JsonElement>>(joseki)!.Select(c => (c.GetProperty("id").GetString(), c.GetProperty("box").GetInt32()))
            .Should().BeEquivalentTo([("a", 3), ("b", 4)]);
        SgfParser.ParseCollection(File.ReadAllText(Path.Combine(desktop, "tsumego", "from-my-games.sgf"))).Games.Should().HaveCount(2);
        JsonObject settings = JsonNode.Parse(File.ReadAllText(Path.Combine(desktop, "settings.json")))!.AsObject();
        settings["theme"]!.GetValue<string>().Should().Be("kuro");
        settings["kataGoExecutable"]!.GetValue<string>().Should().Be("/opt/katago", "paths stay the device's own");
    }

    [Fact]
    public void Importing_twice_changes_nothing_and_preferences_stay_unless_asked()
    {
        string a = Device("a");
        string b = Device("b");
        new ReplayStore(dataDirectory: a).Save(SgfParser.Parse(Game), "g", "local");
        Write(a, "settings.json", """{ "theme": "kuro" }""");
        Write(b, "settings.json", """{ "theme": "mochi" }""");
        string zip = Path.Combine(_root, "a.hoshi");
        BackupPackage.Export(a, zip, "1");

        BackupPackage.Stage(zip, b, applyPreferences: false);
        BackupPackage.ApplyPending(b)!.Games.Should().Be(1);
        BackupPackage.Stage(zip, b, applyPreferences: false);
        BackupPackage.ApplyPending(b)!.IsEmpty.Should().BeTrue();

        JsonNode.Parse(File.ReadAllText(Path.Combine(b, "settings.json")))!["theme"]!.GetValue<string>().Should().Be("mochi");
    }

    [Fact]
    public void Files_that_are_not_Hoshi_backups_are_refused_and_nothing_escapes_the_pending_folder()
    {
        string target = Device("target");
        string notBackup = Path.Combine(_root, "other.zip");
        using (ZipArchive z = ZipFile.Open(notBackup, ZipArchiveMode.Create))
        {
            z.CreateEntry("readme.txt");
        }

        FluentActions.Invoking(() => BackupPackage.Stage(notBackup, target, false)).Should().Throw<InvalidDataException>();

        string evil = Path.Combine(_root, "evil.hoshi");
        using (ZipArchive z = ZipFile.Open(evil, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("hoshi-backup.json").Open()))
            {
                w.Write("""{"app":"Hoshi","format":1}""");
            }

            z.CreateEntry("../../escaped.txt");
        }

        BackupPackage.Stage(evil, target, false);
        File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse();
        File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "escaped.txt")).Should().BeFalse();
    }
}
