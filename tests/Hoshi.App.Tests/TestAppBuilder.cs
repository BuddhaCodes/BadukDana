using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Hoshi.App.Tests.TestAppBuilder))]

namespace Hoshi.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
