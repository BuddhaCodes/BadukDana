using Avalonia.Controls;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

/// <summary>The library of played games, in a window on the desktop or a page on the phone.</summary>
public partial class ReplaysView : UserControl
{
    public ReplaysView()
    {
        InitializeComponent();
        ReplayList.DoubleTapped += (_, _) =>
        {
            if (DataContext is ReplaysViewModel vm)
            {
                vm.OpenCommand.Execute(null);
            }
        };
    }
}
