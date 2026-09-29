namespace Hoshi.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel()
        : this(new GameViewModel())
    {
    }

    public MainWindowViewModel(GameViewModel game)
    {
        Game = game;
    }

    public GameViewModel Game { get; }
}
