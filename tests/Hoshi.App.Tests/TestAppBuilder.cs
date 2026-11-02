using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Hoshi.App.Tests.TestAppBuilder))]

// Some tests switch the (global) language or theme for a moment; with xUnit v3 test classes awaiting on the same UI
// dispatcher could interleave and see each other's language. One at a time keeps them independent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Hoshi.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false });
}
