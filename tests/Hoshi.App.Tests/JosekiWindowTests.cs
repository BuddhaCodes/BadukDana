using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hoshi.App.Services.Joseki;
using Hoshi.App.ViewModels;
using Hoshi.App.Views;
using Hoshi.Sgf.Joseki;

namespace Hoshi.App.Tests;

public sealed class JosekiWindowTests
{
    [AvaloniaFact]
    public async Task The_trainer_window_shows_the_line_and_the_feedback()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hoshi-joseki-ui-" + Guid.NewGuid().ToString("N"));
        try
        {
            var vm = new JosekiTrainerViewModel(new JosekiLibrary(dataDirectory: dir), random: new Random(3));
            vm.Start();
            JosekiDrill drill = vm.Drill!;
            for (int i = 0; i < 4; i++)
            {
                vm.PlayCommand.Execute(drill.Expected!.Value);
            }

            vm.PlayCommand.Execute(new Hoshi.Core.Point(9, 9));
            vm.PlayCommand.Execute(new Hoshi.Core.Point(9, 9));

            var window = new JosekiTrainerWindow { DataContext = vm, Width = 980, Height = 680 };
            window.Show();
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(20);
            }

            window.FindControl<TextBlock>("LineName")!.Text.Should().Be("Invasión en 3-3 bajo el hoshi");
            window.FindControl<TextBlock>("StatusText")!.Classes.Should().Contain("bad");
            window.FindControl<TextBlock>("ProgressText")!.Text.Should().StartWith("Jugada 5 de 8");
            using WriteableBitmap frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered");
            Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "screenshots"));
            frame.Save(Path.Combine(AppContext.BaseDirectory, "screenshots", "joseki.png"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
