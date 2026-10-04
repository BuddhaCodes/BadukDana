using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Hoshi.App.ViewModels;

namespace Hoshi.App.Views;

public partial class GtpConsoleWindow : Window
{
    public GtpConsoleWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is GtpConsoleViewModel vm)
            {
                vm.RowAdded += (_, _) => Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
            }
        };
        Opened += (_, _) =>
        {
            Scroller.ScrollToEnd();
            CommandBox.Focus();
        };
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not GtpConsoleViewModel vm)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                vm.SendCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                vm.History(-1);
                e.Handled = true;
                break;
            case Key.Down:
                vm.History(1);
                e.Handled = true;
                break;
        }
    }
}
