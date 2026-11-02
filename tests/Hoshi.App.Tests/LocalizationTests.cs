using Avalonia.Headless;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.Services;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;

namespace Hoshi.App.Tests;

[CollectionDefinition("Language", DisableParallelization = true)]
public sealed class LanguageCollection
{
}

public sealed partial class LocalizationTableTests
{
    [Fact]
    public void Every_text_exists_in_both_languages_with_the_same_placeholders()
    {
        Tr.Keys.Should().HaveCountGreaterThan(250);
        foreach (string key in Tr.Keys)
        {
            string en = Tr.T(key, Tr.English);
            string es = Tr.T(key, Tr.Spanish);
            en.Should().NotBeNullOrWhiteSpace(key);
            es.Should().NotBeNullOrWhiteSpace(key);
            Placeholders(en).Should().BeEquivalentTo(Placeholders(es), key);
        }
    }

    [Theory]
    [InlineData(Tr.Japanese)]
    [InlineData(Tr.Korean)]
    [InlineData(Tr.Chinese)]
    public void Table_languages_translate_every_key_with_the_same_placeholders(string language)
    {
        Tr.TableKeys(language).Where(k => !Tr.Has(k)).Should().BeEmpty($"{language} must not keep keys that no longer exist");
        foreach (string key in Tr.Keys)
        {
            string? text = Tr.Translated(key, language);
            text.Should().NotBeNullOrWhiteSpace($"{language} lacks {key}");
            Placeholders(text!).Should().BeEquivalentTo(Placeholders(Tr.T(key, Tr.English)), $"{language}: {key}");
        }
    }

    [Fact]
    public void Languages_are_recognised_from_system_tags()
    {
        Tr.Normalize("ja-JP").Should().Be(Tr.Japanese);
        Tr.Normalize("ko").Should().Be(Tr.Korean);
        Tr.Normalize("zh-Hans-CN").Should().Be(Tr.Chinese);
        Tr.Normalize("es-MX").Should().Be(Tr.Spanish);
        Tr.Normalize("fr").Should().Be(Tr.English);
    }

    [Fact]
    public void Every_key_used_in_the_views_exists()
    {
        string views = Path.Combine(RepoRoot(), "src", "Hoshi.App", "Views");
        var used = Directory.GetFiles(views, "*.axaml")
            .SelectMany(f => KeyUse().Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        used.Should().NotBeEmpty();
        used.Where(k => !Tr.Has(k)).Should().BeEmpty();
    }

    [Fact]
    public void English_is_the_default_language() => new AppSettings().Language.Should().Be(Tr.English);

    private static IEnumerable<string> Placeholders(string s) => PlaceholderPattern().Matches(s).Select(m => m.Groups[1].Value).Distinct();

    private static string RepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Hoshi.sln")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        return dir ?? throw new InvalidOperationException("Hoshi.sln not found");
    }

    [GeneratedRegex(@"\{l:T ([A-Za-z0-9_.]+)\}")]
    private static partial Regex KeyUse();

    [GeneratedRegex(@"\{(\d+)(?::[^}]*)?\}")]
    private static partial Regex PlaceholderPattern();
}

[Collection("Language")]
public sealed class LanguageSwitchTests
{
    [AvaloniaFact]
    public void The_window_switches_between_English_and_Spanish_live()
    {
        var window = new MainWindow(new MainWindowViewModel(new GameViewModel()));
        window.Show();
        try
        {
            Tr.SetLanguage(Tr.English);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.FindDeep<TextBlock>("PassLabel")!.Text.Should().Be("Pass");
            using (Avalonia.Media.Imaging.WriteableBitmap frame = window.CaptureRenderedFrame()!)
            {
                Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
                frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "main-en.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }

            var prefs = new PreferencesWindow { DataContext = new PreferencesViewModel(new Themes.ThemeService(new TestSettings()), new TestSettings()), Width = 900, Height = 700 };
            prefs.Show();
            using (Avalonia.Media.Imaging.WriteableBitmap frame = prefs.CaptureRenderedFrame()!)
            {
                frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "prefs-en.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            Tr.SetLanguage(Tr.Spanish);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.FindDeep<TextBlock>("PassLabel")!.Text.Should().Be("Pasar");
        }
        finally
        {
            Tr.SetLanguage(Tr.Spanish); // the other tests assert Spanish
        }
    }
}
