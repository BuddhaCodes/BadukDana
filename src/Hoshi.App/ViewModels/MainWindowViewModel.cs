using CommunityToolkit.Mvvm.ComponentModel;

namespace Hoshi.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Hoshi";

    public MainWindowViewModel()
        : this(new LocalGameViewModel())
    {
    }

    public MainWindowViewModel(LocalGameViewModel game)
    {
        Game = game;
    }

    public LocalGameViewModel Game { get; }
}
