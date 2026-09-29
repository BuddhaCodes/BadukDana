using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoshi.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Hoshi";

    [ObservableProperty]
    private string _statusText = "Ready";
}
