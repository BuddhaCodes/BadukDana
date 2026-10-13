using Avalonia.Controls;
using Hoshi.App.Services.Updates;

namespace Hoshi.App.Views;

/// <summary>The small window with Hoshi's logo shown while the main window is being built (desktops).</summary>
public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
        SplashVersion.Text = UpdatePlatform.Display(UpdatePlatform.CurrentVersion);
    }
}
