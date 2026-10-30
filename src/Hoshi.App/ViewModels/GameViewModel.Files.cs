using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;
using Hoshi.Core;
using Hoshi.Core.Localization;
using Hoshi.Sgf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hoshi.App.ViewModels;

public sealed partial class GameViewModel
{
    // ---------- Files ----------

    [RelayCommand]
    private async Task NewGame(int size)
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        Online?.LeaveCommand.Execute(null);
        int n = size is >= BoardState.MinSize and <= BoardState.MaxSize ? size : 19;
        Load(GameTree.Create(n, Board.Rules), path: null);
    }

    [RelayCommand]
    private async Task Open()
    {
        if (_files is null || !await ConfirmDiscardAsync() || await _files.PickSgfToOpenAsync() is not { } path)
        {
            return;
        }

        await OpenFileAsync(path, confirm: false);
    }

    /// <summary>Opens a file by path (drag and drop, command line). Returns false when it could not be read.</summary>
    public async Task<bool> OpenFileAsync(string path, bool confirm = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (confirm && !await ConfirmDiscardAsync())
        {
            return false;
        }

        try
        {
            byte[] data = await File.ReadAllBytesAsync(path);
            SgfParseResult parsed = SgfParser.ParseCollection(data);
            if (parsed.Games.Count == 0)
            {
                throw new FormatException(Tr.T("Game.NoSgfGame"));
            }

            Online?.LeaveCommand.Execute(null);
            Hoshi.Sgf.Study.StudyStore.StripMirror(parsed.Games[0]); // the copy for other programs, rebuilt on save
            Load(parsed.Games[0], path);
            int warnings = parsed.Warnings.Count + _cursor.Warnings.Count;
            _logger.LogInformation("Opened {File} ({Nodes} nodes, {Warnings} warnings)",
                System.IO.Path.GetFileName(path), Tree.AllNodes().Count(), warnings);
            if (warnings > 0 || parsed.Games.Count > 1)
            {
                SetStatus(parsed.Games.Count > 1
                    ? Tr.F("Game.OpenedFirstOf", parsed.Games.Count)
                    : Tr.F("Game.OpenedWithWarnings", warnings));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Could not open {File}", System.IO.Path.GetFileName(path));
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Game.CouldNotOpenFile"), ex.Message);
            }

            SetStatus(Tr.T("Game.CouldNotOpenFile"));
            return false;
        }
    }

    /// <summary>
    /// Opens a game given as SGF text (downloaded from OGS, pasted): replaces the board's game like Open does.
    /// Returns false when it is not a game, or when the user keeps the unsaved one.
    /// </summary>
    public async Task<bool> OpenSgfTextAsync(string sgf, string description, bool confirm = true)
    {
        ArgumentNullException.ThrowIfNull(sgf);
        if (confirm && !await ConfirmDiscardAsync())
        {
            return false;
        }

        try
        {
            SgfParseResult parsed = SgfParser.ParseCollection(sgf);
            if (parsed.Games.Count == 0)
            {
                throw new FormatException(Tr.T("Game.NoSgfGame"));
            }

            Online?.LeaveCommand.Execute(null);
            Hoshi.Sgf.Study.StudyStore.StripMirror(parsed.Games[0]);
            Load(parsed.Games[0], path: null);
            SetStatus(Tr.F("Game.OpenedFrom", description));
            return true;
        }
        catch (FormatException ex)
        {
            if (_dialogs is not null)
            {
                await _dialogs.ShowErrorAsync(Tr.T("Game.CouldNotOpenFile"), ex.Message);
            }

            SetStatus(Tr.T("Game.CouldNotOpenFile"));
            return false;
        }
    }

    [RelayCommand]
    private async Task Save()
    {
        if (FilePath is null)
        {
            await SaveAs();
            return;
        }

        await SaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAs()
    {
        string suggested = FilePath is null ? SuggestedFileName() : System.IO.Path.GetFileName(FilePath);
        if (_files is not null && await _files.PickSgfToSaveAsync(suggested) is { } path)
        {
            await SaveToAsync(path);
        }
    }

    public async Task SaveToAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await File.WriteAllBytesAsync(path, SgfWriter.WriteBytes(Tree));
        if (_files is not null)
        {
            await _files.FinishSaveAsync(path);
        }

        FilePath = path;
        IsDirty = false;
        SetStatus(Tr.F("Game.Saved", System.IO.Path.GetFileName(path)));
    }

    [RelayCommand]
    private async Task EditGameInfo()
    {
        if (_dialogs is null)
        {
            return;
        }

        var info = new GameInfoViewModel(Tree.Info);
        if (await _dialogs.EditGameInfoAsync(info))
        {
            ApplyGameInfo(info);
        }
    }

    public void ApplyGameInfo(GameInfoViewModel info)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.ApplyTo(Tree.Info);
        IsDirty = true;
        _cursor.NotifyEdited();
    }
}
