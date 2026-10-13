using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services.Engines;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;

namespace Hoshi.App.Services.Phone;

/// <summary>A question or a message shown over the phone layout (instead of a dialog window).</summary>
public sealed class PhoneMessage(string title, string text, bool confirm)
{
    private readonly TaskCompletionSource<bool> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Title { get; } = title;

    public string Text { get; } = text;

    public bool IsConfirm { get; } = confirm;

    public string OkLabel => Tr.T(IsConfirm ? "Dialog.Yes" : "Dialog.Ok");

    public string NoLabel => Tr.T("Dialog.No");

    public Task<bool> Answer => _answer.Task;

    internal void Complete(bool yes) => _answer.TrySetResult(yes);
}

/// <summary>
/// The phone's windows: Preferences, the lobby, the game library, game info and "Play against KataGo" open as
/// full-screen pages over the board, and dialogs as a card. Android's back button closes the top one.
/// </summary>
public sealed partial class PhoneNavigation : ObservableObject,
    IPreferencesWindowService, ILobbyWindowService, IReplaysWindowService, ITsumegoWindowService, IEngineWindows, IDialogService
{
    private readonly PreferencesViewModel? _preferences;
    private readonly LobbyViewModel? _lobby;
    private readonly ReplaysViewModel? _replays;
    private readonly TsumegoViewModel? _tsumego;
    private Action<bool>? _onClose;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    private Control? _page;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private PhoneMessage? _message;

    public PhoneNavigation(PreferencesViewModel? preferences = null, LobbyViewModel? lobby = null, ReplaysViewModel? replays = null, TsumegoViewModel? tsumego = null)
    {
        _tsumego = tsumego;
        _preferences = preferences;
        _lobby = lobby;
        _replays = replays;

        // Opening a game from the lobby or the library goes back to the board.
        if (lobby is not null)
        {
            lobby.GameStarted += (_, _) => Close(true);
        }

        if (replays is not null)
        {
            replays.OpenRequested += (_, _) => Close(true);
        }
    }

    public bool HasPage => Page is not null;

    public bool HasMessage => Message is not null;

    /// <summary>Shows a page (replacing the current one); <paramref name="onClose"/> runs when it closes.</summary>
    public void Show(Control page, string title, Action<bool>? onClose = null)
    {
        Close(false);
        Title = title;
        Page = page;
        _onClose = onClose;
    }

    /// <summary>Closes the page; <paramref name="result"/> is what a dialog page reports (OK = true).</summary>
    public void Close(bool result)
    {
        if (Page is null)
        {
            return;
        }

        Action<bool>? onClose = _onClose;
        _onClose = null;
        Page = null;
        Title = string.Empty;
        onClose?.Invoke(result);
    }

    [RelayCommand]
    private void GoBack() => Back();

    /// <summary>Android's back button: the message, then the page. False when there is nothing to close.</summary>
    public bool Back()
    {
        if (Message is { } message)
        {
            Message = null;
            message.Complete(false);
            return true;
        }

        if (Page is PreferencesView preferences)
        {
            preferences.TryDone(); // saves what was typed; stays when the engine list has a problem
            return true;
        }

        if (Page is not null)
        {
            Close(false);
            return true;
        }

        return false;
    }

    [RelayCommand]
    private void Yes() => AnswerMessage(true);

    [RelayCommand]
    private void No() => AnswerMessage(false);

    private void AnswerMessage(bool yes)
    {
        if (Message is { } message)
        {
            Message = null;
            message.Complete(yes);
        }
    }

    // ---------- Windows as pages ----------

    public Task ShowAsync()
    {
        var view = new PreferencesView { DataContext = _preferences };
        view.DoneRequested += (_, _) => Close(true);
        Show(view, Tr.T("Main.Preferences"));
        return Task.CompletedTask;
    }

    void ILobbyWindowService.Show() => Show(new LobbyView { DataContext = _lobby }, Tr.T("Lobby.Title"));

    void IReplaysWindowService.Show() => Show(new ReplaysView { DataContext = _replays }, Tr.T("Replays.Title"));

    void ITsumegoWindowService.Show() => Show(new TsumegoView { DataContext = _tsumego }, Tr.T("Tsumego.Title"));

    public Task<bool> AskNewGameAsync(NewEngineGameViewModel options)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new NewEngineGameView { DataContext = options };
        view.Completed += (_, ok) => Close(ok);
        Show(view, Tr.T("Engines.NewGameTitle"), ok => done.TrySetResult(ok));
        return done.Task;
    }

    /// <summary>No GTP console on phones.</summary>
    public void ShowConsole()
    {
    }

    // ---------- Dialogs ----------

    public Task<bool> EditGameInfoAsync(GameInfoViewModel info)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new GameInfoView { DataContext = info };
        view.Completed += (_, ok) => Close(ok);
        Show(view, Tr.T("GameInfo.Title"), ok => done.TrySetResult(ok));
        return done.Task;
    }

    public Task<bool> ConfirmAsync(string title, string message) => Ask(new PhoneMessage(title, message, confirm: true));

    public Task ShowErrorAsync(string title, string message) => Ask(new PhoneMessage(title, message, confirm: false));

    private Task<bool> Ask(PhoneMessage message)
    {
        Message?.Complete(false);
        Message = message;
        return message.Answer;
    }
}
