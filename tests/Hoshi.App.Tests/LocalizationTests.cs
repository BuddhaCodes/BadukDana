using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Core.Localization;

namespace Hoshi.App.Tests;

public sealed class LocalizationProbe
{
    [AvaloniaFact]
    public void Xaml_text_follows_the_language()
    {
        var window = new MainWindow(new MainWindowViewModel(new GameViewModel()));
        window.Show();
        window.FindControl<TextBlock>("PassLabel")!.Text.Should().Be(Tr.T("Main.Pass"));
    }
}
