using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class LobbyWindow : Window
{
    public LobbyWindow()
    {
        InitializeComponent();
        CloseCommand = new RelayCommand(Close);
        Opened += async (_, _) =>
        {
            if (DataContext is LobbyViewModel vm)
            {
                await vm.InitializeAsync();
            }
        };
    }

    public IRelayCommand CloseCommand { get; }
}
