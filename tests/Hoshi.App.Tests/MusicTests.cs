using Hoshi.App.Services.Music;
using Hoshi.Engines.KataGo;

namespace Hoshi.App.Tests;

public sealed class MusicDirectorTests
{
    private readonly MusicDirector _director = new();

    [Fact]
    public void Good_moves_in_quick_succession_heat_up_the_music_more_than_spaced_ones()
    {
        _director.OnVerdict(MoveQuality.Good, 0);
        _director.OnVerdict(MoveQuality.Excellent, 5);
        _director.OnVerdict(MoveQuality.Best, 10);

        _director.Streak.Should().Be(3);
        _director.HeatAt(10).Should().BeApproximately(0.6 + (0.9 + 0.15) + (1.2 + 0.3), 1e-9);

        var spaced = new MusicDirector();
        spaced.OnVerdict(MoveQuality.Good, 0);
        spaced.OnVerdict(MoveQuality.Excellent, 60);
        spaced.Streak.Should().Be(1, "a minute apart is not a streak");
    }

    [Fact]
    public void Heat_cools_down_after_a_quiet_while()
    {
        _director.OnVerdict(MoveQuality.Best, 0);
        _director.OnVerdict(MoveQuality.Best, 1);
        double hot = _director.HeatAt(1);

        _director.HeatAt(1 + MusicDirector.CoolDelay).Should().Be(hot, "no cooling during the grace period");
        _director.HeatAt(1 + MusicDirector.CoolDelay + 10).Should().BeApproximately(hot - (10 * MusicDirector.CoolRate), 1e-9);
        _director.HeatAt(1000).Should().Be(0);
    }

    [Fact]
    public void Mistakes_calm_it_down_and_a_blunder_stops_the_tape()
    {
        int stops = 0;
        _director.TapeStop += (_, _) => stops++;
        for (int i = 0; i < 4; i++)
        {
            _director.OnVerdict(MoveQuality.Best, i);
        }

        double hot = _director.HeatAt(4);
        _director.OnVerdict(MoveQuality.Inaccuracy, 4);
        _director.HeatAt(4).Should().BeApproximately(hot - 1, 1e-9);
        _director.Streak.Should().Be(0);

        _director.OnVerdict(MoveQuality.Blunder, 5);
        _director.HeatAt(5).Should().Be(0);
        stops.Should().Be(1);

        _director.OnVerdict(MoveQuality.Blunder, 6);
        stops.Should().Be(1, "no tape-stop when the music is already calm");
    }

    [Fact]
    public void Layers_fade_in_one_after_another_and_the_filter_opens()
    {
        MusicDirector.LayerGain(0, 0).Should().Be(1);
        Enumerable.Range(1, 4).Select(l => MusicDirector.LayerGain(l, 0)).Should().OnlyContain(g => g == 0);
        MusicDirector.LayerGain(1, 1.5).Should().Be(1);
        MusicDirector.LayerGain(2, 1.5).Should().BeInRange(0.2, 0.4);
        MusicDirector.LayerGain(4, 1.5).Should().Be(0);
        Enumerable.Range(1, 4).Select(l => MusicDirector.LayerGain(l, MusicDirector.MaxHeat)).Should().OnlyContain(g => g == 1);
        MusicDirector.Cutoff(0).Should().Be(900);
        MusicDirector.Cutoff(4).Should().BeGreaterThan(15000);
    }
}

public sealed class LofiMusicTests
{
    private static readonly Lazy<float[][]> Layers = new(() => LofiComposer.Render());

    [Fact]
    public void Five_layers_of_one_seamless_eight_bar_loop()
    {
        float[][] layers = Layers.Value;
        layers.Should().HaveCount(LofiComposer.LayerCount);
        LofiComposer.LoopSeconds.Should().BeApproximately(24, 1e-9);
        foreach (float[] layer in layers)
        {
            layer.Length.Should().Be(LofiComposer.LoopFrames * 2);
            double rms = Math.Sqrt(layer.Average(v => (double)v * v));
            rms.Should().BeInRange(0.003, 0.35, "each layer is audible but leaves headroom");
            layer.Max(Math.Abs).Should().BeLessThan(1.5f);

            // Seamless: the jump from the last frame to the first is no bigger than ordinary sample steps.
            double wrap = Math.Abs(layer[0] - layer[^2]);
            double typical = Enumerable.Range(1, 5000).Average(i => Math.Abs(layer[2 * i] - layer[2 * (i - 1)]));
            wrap.Should().BeLessThan((typical * 20) + 0.02);
        }
    }

    [Fact]
    public void The_mixer_glides_layers_in_and_saves_a_demo_of_a_hot_streak()
    {
        var mixer = new MusicMixer(Layers.Value) { Volume = 0.6 };
        var director = new MusicDirector();
        director.TapeStop += (_, _) => mixer.StartTapeStop();
        const int rate = LofiComposer.SampleRate;
        const int block = 2048;
        double seconds = 96;
        var pcm = new short[(int)(seconds * rate) * 2];
        var buffer = new short[block * 2];
        double[] verdictTimes = [16, 20, 23, 27, 30, 33, 36, 40, 44];
        int nextVerdict = 0;
        bool blundered = false;
        for (int frame = 0; frame + block <= pcm.Length / 2; frame += block)
        {
            double now = (double)frame / rate;
            if (nextVerdict < verdictTimes.Length && now >= verdictTimes[nextVerdict])
            {
                director.OnVerdict(nextVerdict % 3 == 2 ? MoveQuality.Best : MoveQuality.Excellent, now);
                nextVerdict++;
            }

            if (!blundered && now >= 70)
            {
                blundered = true;
                director.OnVerdict(MoveQuality.Blunder, now);
            }

            mixer.SetHeat(director.HeatAt(now));
            mixer.Render(buffer);
            buffer.CopyTo(pcm.AsSpan(frame * 2));

            if (Math.Abs(now - 60) < 0.03)
            {
                mixer.Gains.Skip(1).Should().OnlyContain(g => g > 0.9, "a streak of strong moves brings every layer in");
            }
        }

        mixer.Gains.Skip(1).Should().OnlyContain(g => g < 0.2, "after the blunder the music is calm again");
        pcm.Max(s => Math.Abs((int)s)).Should().BeLessThan(32767).And.BeGreaterThan(2000);

        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        WriteWav(Path.Combine(dir, "music-demo.wav"), pcm, rate);
    }

    private static void WriteWav(string path, short[] pcm, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + bytes);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)2);
        w.Write(rate);
        w.Write(rate * 4);
        w.Write((short)4);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(bytes);
        foreach (short s in pcm)
        {
            w.Write(s);
        }
    }
}
