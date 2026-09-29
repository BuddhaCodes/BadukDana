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
        this.FindControl<ListBox>("ActiveGamesList")!.DoubleTapped += (_, _) =>
        {
            if (DataContext is LobbyViewModel vm && this.FindControl<ListBox>("ActiveGamesList")!.SelectedItem is ActiveGameItem item)
            {
                vm.OpenGameCommand.Execute(item);
            }
        };
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
