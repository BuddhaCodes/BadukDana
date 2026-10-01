using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hoshi.Core.Localization;

namespace Hoshi.App.Views;

/// <summary>Minimal themed message / confirmation dialog (returns true for "Sí"/"Aceptar").</summary>
public sealed class MessageWindow : Window
{
    public MessageWindow()
        : this(string.Empty, string.Empty, confirm: false)
    {
    }

    public MessageWindow(string title, string message, bool confirm)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button { Content = Tr.T(confirm ? "Dialog.Yes" : "Dialog.Ok"), IsDefault = true, MinWidth = 80 };
        ok.Click += (_, _) => Close(true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0),
        };
        if (confirm)
        {
            var no = new Button { Content = Tr.T("Dialog.No"), IsCancel = true, MinWidth = 80 };
            no.Click += (_, _) => Close(false);
            buttons.Children.Add(no);
        }

        buttons.Children.Add(ok);

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                buttons,
            },
        };
    }
}
