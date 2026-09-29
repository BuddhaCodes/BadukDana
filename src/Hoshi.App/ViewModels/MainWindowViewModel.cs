using CommunityToolkit.Mvvm.Input;
using Hoshi.App.Services;

namespace Hoshi.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILobbyWindowService? _lobby;

    public MainWindowViewModel()
        : this(new GameViewModel())
    {
    }

    public MainWindowViewModel(GameViewModel game, ILobbyWindowService? lobby = null)
    {
        Game = game;
        _lobby = lobby;
    }

    public GameViewModel Game { get; }

    /// <summary>False in design/test contexts without OGS services.</summary>
    public bool IsOnlineAvailable => _lobby is not null;

    [RelayCommand(CanExecute = nameof(IsOnlineAvailable))]
    private void OpenLobby() => _lobby?.Show();
}
