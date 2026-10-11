using Avalonia;
using Avalonia.Controls;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

/// <summary>The OGS lobby, in a window on the desktop or a page on the phone.</summary>
public partial class LobbyView : UserControl
{
    public LobbyView()
    {
        InitializeComponent();
        ActiveGamesList.DoubleTapped += (_, _) =>
        {
            if (DataContext is LobbyViewModel vm && ActiveGamesList.SelectedItem is ActiveGameItem item)
            {
                vm.OpenGameCommand.Execute(item);
            }
        };
    }

    /// <summary>Each time the lobby is shown: sign-in state, challenges and active games.</summary>
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LobbyViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
