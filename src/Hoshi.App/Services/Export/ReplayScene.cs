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

    /// <param name="effectSeconds">Where the moment's effect is (seconds after the move), or null for the still
    /// picture: a good move's impact (the app's own, with embers and a shaking board), a mistake's red shock and jolt.</param>
    public static RenderTargetBitmap Render(ReplayStep step, ReplayPlayers players, ReplayFormat format, bool coordinates, bool small = false, BoardStyle? style = null, double? effectSeconds = null)
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

        double? t = effectSeconds is { } e && step.Moment != ReplayMoment.None && step.LastMove is not null ? Math.Max(0, e) : null;
        ViewModels.BoardImpact? impact = t is not null && step.IsGood && step.LastMove is { } played
            ? new ViewModels.BoardImpact(played, step.Moment == ReplayMoment.Brilliant ? 3 : 2, step.MoveNumber)
            : null;
        RenderTargetBitmap board = BoardPicture.Render(new BoardShot(step.Board, step.LastMove, step.Marks, Impact: impact, ImpactSeconds: t ?? 0), boardPixels, coordinates, style);
        Control image = new Image
        {
            Source = board,
            Width = boardPixels,
            Height = boardPixels,
            Opacity = step.Kind == ReplayStepKind.Intro ? 0.55 : 1,
        };

        // A mistake: a red shock spreads from the stone and the board jolts (harder for a blunder).
        if (t is { } bad && !step.IsGood && step.LastMove is { } wrong)
        {
            bool blunder = step.Moment == ReplayMoment.Blunder;
            var geometry = Controls.BoardGeometry.Create(boardPixels, boardPixels, step.Board.Width, step.Board.Height, coordinates);
            var shock = new MistakeShock(geometry.Center(wrong), geometry.Cell, bad, blunder) { Width = boardPixels, Height = boardPixels };
            double amp = geometry.Cell * (blunder ? 0.28 : 0.16) * Math.Exp(-bad / 0.18);
            image = new Panel
            {
                Width = boardPixels,
                Height = boardPixels,
                Children = { image, shock },
                RenderTransform = new TranslateTransform(amp * Math.Sin(bad * 2 * Math.PI * 17), amp * 0.35 * Math.Cos(bad * 2 * Math.PI * 23)),
            };
        }

        IBrush gold = Brush("#F5B041"), soft = Brush("#C9CCD3"), white = Brushes.White;
        bool titleCard = step.Kind is ReplayStepKind.Intro or ReplayStepKind.Outro;
        var info = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(PlayersRow(players, step));
        info.Children.Add(Text(step.Title, titleCard ? 44 : 34, FontWeight.Bold, white, 2));
        if (step.Highlight is { Length: > 0 } highlight)
        {
            // Gold for a strong move, red for a mistake (and captures); it pops in with the effect.
            double pop = t is { } p ? 1 + (0.12 * Math.Exp(-p / 0.12) * Math.Cos(p * 14)) : 1;
            info.Children.Add(new Border
            {
                Background = step.IsGood ? Brush("#E8C36A") : Brush("#C8432F"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                RenderTransform = new ScaleTransform(pop, pop),
                Child = Text(highlight, 26, FontWeight.SemiBold, step.IsGood ? Brush("#1E1A14") : white, 2),
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
            foreach (TextBlock line in info.Children.OfType<TextBlock>())
            {
                line.FontSize *= 0.75;
            }
        }

        // The whole frame glows with the moment: gold light for a strong move, a red edge for a mistake.
        Control content = layout;
        if (t is { } glow)
        {
            double strength = step.Moment switch { ReplayMoment.Brilliant => 0.55, ReplayMoment.Blunder => 0.6, _ => 0.4 } * Math.Exp(-glow / 0.45);
            Color tint = step.IsGood ? Color.Parse("#E8C36A") : Color.Parse("#C8432F");
            content = new Panel
            {
                Children =
                {
                    layout,
                    new Border
                    {
                        IsHitTestVisible = false,
                        Background = new RadialGradientBrush
                        {
                            GradientStops =
                            {
                                new GradientStop(Color.FromArgb(0, tint.R, tint.G, tint.B), 0.55),
                                new GradientStop(Color.FromArgb((byte)(255 * Math.Clamp(strength, 0, 1)), tint.R, tint.G, tint.B), 1),
                            },
                        },
                    },
                },
            };
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
            Child = content,
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

    /// <summary>A mistake's shock: a red flash on the stone and rings that spread and fade (three for a blunder).</summary>
    private sealed class MistakeShock(Avalonia.Point centre, double cell, double seconds, bool blunder) : Control
    {
        public override void Render(DrawingContext context)
        {
            double flash = Math.Exp(-seconds / 0.12);
            if (flash > 0.02)
            {
                context.DrawEllipse(
                    new RadialGradientBrush
                    {
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb((byte)(220 * flash), 0xFF, 0x6A, 0x50), 0),
                            new GradientStop(Color.FromArgb(0, 0xC8, 0x43, 0x2F), 1),
                        },
                    },
                    null,
                    centre,
                    cell * 1.6,
                    cell * 1.6);
            }

            int rings = blunder ? 3 : 2;
            for (int i = 0; i < rings; i++)
            {
                double local = seconds - (i * 0.16);
                if (local <= 0)
                {
                    continue;
                }

                double life = blunder ? 1.4 : 1.0;
                double k = Math.Clamp(local / life, 0, 1);
                if (k >= 1)
                {
                    continue;
                }

                double radius = cell * (0.6 + ((blunder ? 7 : 5) * (1 - Math.Pow(1 - k, 3))));
                byte alpha = (byte)(230 * (1 - k));
                context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0xE0, 0x50, 0x3A)), cell * (blunder ? 0.22 : 0.15) * (1 - (0.6 * k))), centre, radius, radius);
            }
        }
    }
}
