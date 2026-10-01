using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class ReplaysWindow : Window
{
    public ReplaysWindow()
    {
        InitializeComponent();
        CloseCommand = new RelayCommand(Close);
        this.FindControl<ListBox>("ReplayList")!.DoubleTapped += (_, _) =>
        {
            if (DataContext is ReplaysViewModel vm)
            {
                vm.OpenCommand.Execute(null);
            }
        };
    }

    public IRelayCommand CloseCommand { get; }
}
