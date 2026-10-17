using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hoshi.App.Themes;

namespace Hoshi.App.Services.Export;

/// <summary>Who plays, for the replay's header (names already hidden when the player asked).</summary>
public sealed record ReplayPlayers(string Black, string White, bool Brand);

/// <summary>
/// A frame of the replay video: the board (the theme's goban and stones) with the players and captures, the move,
/// a red label on big moments and the study note, laid out for each <see cref="ReplayFormat"/>. Hoshi's own
/// layout drawn with Avalonia off screen; must run on the UI thread.
/// </summary>
public static class ReplayScene
{
    public static PixelSize Size(ReplayFormat format, bool small = false)
    {
        PixelSize full = format switch
        {
            ReplayFormat.Square => new PixelSize(1080, 1080),
            ReplayFormat.Vertical => new PixelSize(720, 1280),
            _ => new PixelSize(1280, 720),
        };
        return small ? new PixelSize(full.Width / 2, full.Height / 2) : full;
    }

    public static RenderTargetBitmap Render(ReplayStep step, ReplayPlayers players, ReplayFormat format, bool coordinates, bool small = false, BoardStyle? style = null)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(players);
        PixelSize size = Size(format);
        int boardPixels = format switch
        {
            ReplayFormat.Square => 800,
            ReplayFormat.Vertical => 680,
            _ => 660,
        };

        RenderTargetBitmap board = BoardPicture.Render(new BoardShot(step.Board, step.LastMove, step.Marks), boardPixels, coordinates, style);
        var image = new Image
        {
            Source = board,
            Width = boardPixels,
            Height = boardPixels,
            Opacity = step.Kind == ReplayStepKind.Intro ? 0.55 : 1,
        };

        IBrush gold = Brush("#F5B041"), soft = Brush("#C9CCD3"), white = Brushes.White;
        bool titleCard = step.Kind is ReplayStepKind.Intro or ReplayStepKind.Outro;
        var info = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(PlayersRow(players, step));
        info.Children.Add(Text(step.Title, titleCard ? 44 : 34, FontWeight.Bold, white, 2));
        if (step.Highlight is { Length: > 0 } highlight)
        {
            info.Children.Add(new Border
            {
                Background = Brush("#C8432F"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = Text(highlight, 26, FontWeight.SemiBold, white, 2),
            });
        }

        if (step.Caption is { Length: > 0 } caption)
        {
            info.Children.Add(Text(caption, titleCard ? 28 : 26, FontWeight.Normal, titleCard ? soft : Brush("#E8E6E3"), format == ReplayFormat.Vertical ? 7 : 5));
        }

        if (players.Brand)
        {
            info.Children.Add(Text("Hoshi", 22, FontWeight.SemiBold, Brush("#8A8F99"), 1));
        }

        Control layout = format switch
        {
            ReplayFormat.Wide => new DockPanel
            {
                Margin = new Thickness(30),
                Children = { Dock(image, Avalonia.Controls.Dock.Left), Pad(info, new Thickness(44, 0, 10, 0)) },
            },
            ReplayFormat.Vertical => new DockPanel
            {
                Margin = new Thickness(20, 90, 20, 40),
                Children = { Dock(image, Avalonia.Controls.Dock.Top), Pad(info, new Thickness(16, 40, 16, 0)) },
            },
            _ => new DockPanel
            {
                Margin = new Thickness(30, 26, 30, 20),
                Children = { Dock(image, Avalonia.Controls.Dock.Top), Pad(info, new Thickness(20, 18, 20, 0)) },
            },
        };

        // Square: little room under the board, so the text is smaller.
        if (format == ReplayFormat.Square)
        {
            info.Spacing = 6;
            foreach (TextBlock t in info.Children.OfType<TextBlock>())
            {
                t.FontSize *= 0.75;
            }
        }

        var frame = new Border
        {
            Width = size.Width,
            Height = size.Height,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#151823"), 0), new GradientStop(Color.Parse("#2A2D36"), 1) },
            },
            Child = layout,
        };
        frame.Measure(new Avalonia.Size(size.Width, size.Height));
        frame.Arrange(new Rect(0, 0, size.Width, size.Height));

        // A smaller copy (GIFs) is drawn with the same layout, scaled.
        double scale = small ? 0.5 : 1;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)(size.Width * scale), (int)(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(frame);
        return bitmap;
    }

    private static Control PlayersRow(ReplayPlayers players, ReplayStep step)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(StoneDot(Color.Parse("#111111"), Color.Parse("#777777")));
        row.Children.Add(Text($"{players.Black}  +{step.Board.BlackCaptures}", 22, FontWeight.SemiBold, Brushes.White, 1));
        row.Children.Add(new Border { Width = 14 });
        row.Children.Add(StoneDot(Color.Parse("#F2F0EA"), Color.Parse("#9A9A9A")));
        row.Children.Add(Text($"{players.White}  +{step.Board.WhiteCaptures}", 22, FontWeight.SemiBold, Brushes.White, 1));
        return row;
    }

    private static Control StoneDot(Color fill, Color stroke) => new Avalonia.Controls.Shapes.Ellipse
    {
        Width = 18,
        Height = 18,
        Fill = new SolidColorBrush(fill),
        Stroke = new SolidColorBrush(stroke),
        StrokeThickness = 1,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static TextBlock Text(string text, double size, FontWeight weight, IBrush brush, int lines) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = TextWrapping.Wrap,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = lines,
    };

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    private static Control Dock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.HorizontalAlignment = HorizontalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static Control Pad(Control control, Thickness margin)
    {
        control.Margin = margin;
        return control;
    }
}
