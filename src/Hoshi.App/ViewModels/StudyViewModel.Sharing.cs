using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Controls;
using Hoshi.App.Services;
using Hoshi.App.Services.Study;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Hoshi.Sgf.Study;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class StudyViewModel
{
    // ---------- Sharing ----------

    /// <summary>Saves the game with its study as an SGF that Hoshi reopens and other programs can read.</summary>
    [RelayCommand]
    private async Task SaveStudy()
    {
        if (_files is null || await _files.PickSgfToSaveAsync(SuggestedName(Tr.T("Study.FileSuffix"), "sgf")) is not { } path)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, StudyStore.WriteWithMirror(_game.Tree), new UTF8Encoding(false));
            await _files.FinishSaveAsync(path);
            _game.ShowStatus(Tr.F("Study.Saved", Path.GetFileName(path)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save the study");
            await ShowError(Tr.T("Study.CouldNotSave"), ex.Message);
        }
    }

    /// <summary>Opens a study file: its notes are added to this game (same moves), or it opens as a new game.</summary>
    [RelayCommand]
    private async Task OpenStudy()
    {
        if (_files is null || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        await OpenStudyFileAsync(path);
    }

    public async Task OpenStudyFileAsync(string path)
    {
        GameTree source;
        try
        {
            SgfParseResult parsed = SgfParser.ParseCollection(await File.ReadAllBytesAsync(path));
            if (parsed.Games.Count == 0)
            {
                throw new FormatException(Tr.T("Game.NoSgfGame"));
            }

            source = parsed.Games[0];
            StudyStore.StripMirror(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not open the study {File}", Path.GetFileName(path));
            await ShowError(Tr.T("Game.CouldNotOpenFile"), ex.Message);
            return;
        }

        bool emptyHere = !_game.Tree.Root.Children.Any() && !_game.IsOnline;
        bool sameGame = SameGame(_game.Tree, source);
        if (emptyHere || (!sameGame && !_game.IsOnline && _dialogs is not null
            && await _dialogs.ConfirmAsync(Tr.T("Study.OtherGameTitle"), Tr.T("Study.OtherGame"))))
        {
            if (await _game.OpenFileAsync(path, confirm: !emptyHere))
            {
                IsActive = true;
            }

            return;
        }

        if (!sameGame)
        {
            _game.ShowStatus(Tr.T("Study.NotThisGame"));
            return;
        }

        StudyMergeResult result = StudyStore.Merge(_game.Tree, source, createMissingMoves: !_game.IsOnline);
        if (result.NewMoves > 0)
        {
            _game.Cursor.NotifyEdited();
        }

        _game.StudyEdited();
        IsActive = true;
        Sync();
        _game.ShowStatus(result.Items == 0
            ? Tr.T("Study.NothingNew")
            : Tr.F("Study.Merged", result.Items, Path.GetFileName(path)));
    }

    /// <summary>The same game when the first moves agree (up to the shorter of the two, at most 20).</summary>
    internal static bool SameGame(GameTree a, GameTree b)
    {
        if (a.Info.Width != b.Info.Width || a.Info.Height != b.Info.Height)
        {
            return false;
        }

        int size = Math.Max(a.Info.Width, a.Info.Height);
        var ma = MainLine(a).Select(n => n.GetMove(size)).Where(m => m is not null).Take(20).ToList();
        var mb = MainLine(b).Select(n => n.GetMove(size)).Where(m => m is not null).Take(20).ToList();
        int n = Math.Min(ma.Count, mb.Count);
        return n == 0 ? ma.Count == mb.Count || ma.Count == 0 : ma.Take(n).SequenceEqual(mb.Take(n));
    }

    private static IEnumerable<GameNode> MainLine(GameTree tree)
    {
        for (GameNode n = tree.Root; ; n = n.Children[0])
        {
            yield return n;
            if (n.Children.Count == 0)
            {
                yield break;
            }
        }
    }

    /// <summary>The game report (a web page): the pins, the drawings and, when allowed, KataGo's verdict on each moment.</summary>
    [RelayCommand]
    private async Task Report()
    {
        if (_files is null || await _files.PickHtmlToSaveAsync(SuggestedName(Tr.T("Study.ReportSuffix"), "html")) is not { } path)
        {
            return;
        }

        IsBusy = true;
        try
        {
            IReadOnlyDictionary<GameNode, StudyVerdict>? verdicts = null;
            if (_analysis is { IsEngineActive: true } analysis)
            {
                _game.ShowStatus(Tr.T("Study.ReportAnalysing"));
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                var nodes = StudyStore.All(_game.Tree).Where(x => x.Study.Pins.Count > 0).Select(x => x.Node).ToList();
                verdicts = await analysis.VerdictsAsync(nodes, cts.Token);
            }

            await File.WriteAllTextAsync(path, StudyReport.Html(_game.Tree, verdicts, _time.GetLocalNow()), new UTF8Encoding(false));
            await _files.FinishSaveAsync(path);
            _game.ShowStatus(Tr.F("Study.ReportSaved", Path.GetFileName(path)));
            if (_browser is not null)
            {
                await _browser.OpenAsync(new Uri(Path.GetFullPath(path)), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            _game.ShowStatus(Tr.T("Study.ReportTimeout"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write the report");
            await ShowError(Tr.T("Study.CouldNotSave"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string SuggestedName(string suffix, string extension)
    {
        string b = _game.Tree.Info.BlackPlayer ?? Tr.T("Common.Black");
        string w = _game.Tree.Info.WhitePlayer ?? Tr.T("Common.White");
        string name = $"{b} vs {w} — {suffix}.{extension}";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name;
    }

    private Task ShowError(string title, string message) => _dialogs?.ShowErrorAsync(title, message) ?? Task.CompletedTask;
}
