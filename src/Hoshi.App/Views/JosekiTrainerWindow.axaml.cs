using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Hoshi.App.Views;

public partial class JosekiTrainerWindow : Window
{
    public JosekiTrainerWindow()
    {
        InitializeComponent();
        CloseCommand = new RelayCommand(Close);
    }

    public IRelayCommand CloseCommand { get; }
}
