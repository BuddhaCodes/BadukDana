namespace Hoshi.App.Services.Music;

/// <summary>
/// Synthesises Hoshi's adaptive lo-fi loop in code (no samples, no assets): 8 bars at 80 BPM over
/// Dm9 – G13 – Cmaj9 – Am9, as five layers that play in sync and are faded in as the game heats up:
/// <list type="number">
/// <item>keys: a warm, wobbly electric piano with vinyl crackle (always on);</item>
/// <item>bass and a soft kick;</item>
/// <item>drums: rim/snare on 2 and 4, swung hats;</item>
/// <item>a plucked pentatonic arpeggio with echo;</item>
/// <item>hype: pumping detuned-saw stabs, 16th hats and claps, a bright lead.</item>
/// </list>
/// Each layer is stereo interleaved float at <see cref="SampleRate"/>. Loops are seamless: every layer is rendered
/// twice in a row and the second pass is kept, so reverb and echo tails wrap around.
/// </summary>
public static class LofiComposer
{
    public const int SampleRate = 44100;
    public const double Bpm = 80;
    public const int Bars = 8;
    public const int LayerCount = 5;

    private const double Beat = 60.0 / Bpm;
    private const double Bar = 4 * Beat;

    public static double LoopSeconds => Bars * Bar;

    public static int LoopFrames => (int)Math.Round(LoopSeconds * SampleRate);

    // Two bars per chord. Voicings as MIDI notes; the bass plays the root.
    private static readonly int[][] Chords =
    [
        [50, 53, 57, 60, 64], // Dm9  (D F A C E)
        [43, 53, 57, 59, 64], // G13  (G F A B E)
        [48, 52, 55, 59, 62], // Cmaj9 (C E G B D)
        [45, 55, 60, 64, 71], // Am9  (A G C E B)
    ];

    private static readonly int[] Roots = [38, 31, 36, 33]; // D2 G1 C2 A1

    // D minor pentatonic-ish melody pool (fits all four chords).
    private static readonly int[] Pentatonic = [62, 65, 67, 69, 72, 74, 77, 79, 81];

    /// <summary>Renders the five layers (index 0 = calmest).</summary>
    public static float[][] Render(int seed = 7)
    {
        var layers = new float[LayerCount][];
        layers[0] = Keys(seed);
        layers[1] = Bass(seed);
        layers[2] = Drums(seed);
        layers[3] = Arp(seed);
        layers[4] = Hype(seed);
        return layers;
    }

    private static double Freq(double midi) => 440 * Math.Pow(2, (midi - 69) / 12);

    private static double Swing(int eighth) => eighth % 2 == 1 ? 0.62 : 0.5; // swung 8ths: long-short

    /// <summary>Two passes of the loop; <paramref name="write"/> adds events at absolute times in seconds.</summary>
    private sealed class Track
    {
        private readonly float[] _l;
        private readonly float[] _r;

        public Track()
        {
            _l = new float[LoopFrames * 2];
            _r = new float[LoopFrames * 2];
        }

        public int Length => _l.Length;

        public void Add(int i, double left, double right)
        {
            if ((uint)i < (uint)_l.Length)
            {
                _l[i] += (float)left;
                _r[i] += (float)right;
            }
        }

        public void Process(Func<float[], float[], (float[], float[])> fx)
        {
            (float[] l, float[] r) = fx(_l, _r);
            Array.Copy(l, _l, _l.Length);
            Array.Copy(r, _r, _r.Length);
        }

        /// <summary>The second pass, interleaved, scaled.</summary>
        public float[] Loop(double gain)
        {
            int n = LoopFrames;
            var output = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                output[2 * i] = (float)(_l[n + i] * gain);
                output[(2 * i) + 1] = (float)(_r[n + i] * gain);
            }

            return output;
        }
    }

    private static void ForEachPass(Action<double> body)
    {
        body(0);
        body(LoopSeconds);
    }

    // ---------- Layers ----------

    private static float[] Keys(int seed)
    {
        var rng = new Random(seed);
        var track = new Track();
        ForEachPass(offset =>
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int[] chord = Chords[bar / 2];
                // Comping: a full chord on 1, a softer re-strike on the "and" of 3.
                foreach ((double at, double vel) in new[] { (0.0, 0.9), (2.5 * Beat, 0.55) })
                {
                    for (int n = 0; n < chord.Length; n++)
                    {
                        double start = offset + (bar * Bar) + at + (n * 0.012) + (rng.NextDouble() * 0.01); // slight roll
                        double pan = -0.35 + (0.7 * n / (chord.Length - 1));
                        EPiano(track, start, at == 0 ? 2.9 : 1.4, chord[n], vel * (0.8 + (0.2 * rng.NextDouble())), pan);
                    }
                }
            }
        });

        // Tape wobble is applied in EPiano; add vinyl crackle and hiss on top.
        var noise = new Random(seed + 99);
        for (int i = 0; i < track.Length; i++)
        {
            double hiss = (noise.NextDouble() - 0.5) * 0.004;
            double crackle = noise.NextDouble() < 0.0006 ? (noise.NextDouble() - 0.5) * 0.25 : 0;
            track.Add(i, hiss + crackle, hiss + (crackle * 0.8));
        }

        track.Process((l, r) => Reverb(l, r, 0.28, 0.82));
        return track.Loop(0.55);
    }

    private static void EPiano(Track track, double start, double length, int midi, double vel, double pan)
    {
        double f = Freq(midi);
        int s0 = (int)(start * SampleRate);
        int n = (int)((length + 1.2) * SampleRate);
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double wobble = 1 + (0.0025 * Math.Sin(2 * Math.PI * 0.5 * (start + t))); // tape wow
            double phase = 2 * Math.PI * f * wobble * t;
            // FM "tine": index decays quickly, leaving a mellow sine body.
            double index = 1.3 * Math.Exp(-t * 6);
            double tone = Math.Sin(phase + (index * Math.Sin(phase))) + (0.18 * Math.Sin(2 * phase) * Math.Exp(-t * 3));
            double env = Math.Min(1, t / 0.006) * Math.Exp(-t / 1.4) * (t < length ? 1 : Math.Exp(-(t - length) / 0.25));
            double v = tone * env * vel * 0.12;
            double trem = 1 + (0.12 * Math.Sin(2 * Math.PI * 4.2 * t));
            track.Add(s0 + k, v * trem * (1 - pan) * 0.7, v * (2 - trem) * (1 + pan) * 0.7);
        }
    }

    private static float[] Bass(int seed)
    {
        var track = new Track();
        ForEachPass(offset =>
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int root = Roots[bar / 2];
                double b0 = offset + (bar * Bar);
                // Kick on 1 and 3 (and a ghost before 3 every other bar).
                Kick(track, b0, 0.9);
                Kick(track, b0 + (2 * Beat), 0.75);
                if (bar % 2 == 1)
                {
                    Kick(track, b0 + (1.75 * Beat), 0.35);
                }

                // Bass: root on 1 (long), fifth or octave on the "and" of 2, root pickup on 4.
                BassNote(track, b0, 1.4 * Beat, root, 0.9);
                BassNote(track, b0 + (1.5 * Beat), 0.9 * Beat, root + (bar % 2 == 0 ? 7 : 12), 0.6);
                BassNote(track, b0 + (3.5 * Beat), 0.45 * Beat, root, 0.5);
            }
        });
        return track.Loop(0.8);
    }

    private static void Kick(Track track, double start, double vel)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)(0.4 * SampleRate);
        double phase = 0;
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double f = 45 + (75 * Math.Exp(-t * 28));
            phase += 2 * Math.PI * f / SampleRate;
            double v = Math.Tanh(1.5 * Math.Sin(phase)) * Math.Exp(-t * 9) * vel * 0.5;
            track.Add(s0 + k, v, v);
        }
    }

    private static void BassNote(Track track, double start, double length, int midi, double vel)
    {
        double f = Freq(midi);
        int s0 = (int)(start * SampleRate);
        int n = (int)((length + 0.15) * SampleRate);
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double phase = 2 * Math.PI * f * t;
            double tone = Math.Tanh(1.8 * (Math.Sin(phase) + (0.25 * Math.Sin(2 * phase))));
            double env = Math.Min(1, t / 0.01) * (t < length ? 1 - (0.3 * t / length) : 0.7 * Math.Exp(-(t - length) / 0.05));
            double v = tone * env * vel * 0.22;
            track.Add(s0 + k, v, v);
        }
    }

    private static float[] Drums(int seed)
    {
        var rng = new Random(seed + 3);
        var track = new Track();
        ForEachPass(offset =>
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                double b0 = offset + (bar * Bar);
                Snare(track, rng, b0 + Beat, 0.7);
                Snare(track, rng, b0 + (3 * Beat), 0.75);
                for (int e = 0; e < 8; e++)
                {
                    double at = b0 + ((e / 2) * Beat) + (Swing(e) * Beat * (e % 2));
                    Hat(track, rng, at, (e % 2 == 0 ? 0.5 : 0.3) * (0.85 + (0.3 * rng.NextDouble())), 0.045, 0.25);
                }
            }
        });
        track.Process((l, r) => Reverb(l, r, 0.12, 0.6));
        return track.Loop(0.8);
    }

    private static void Snare(Track track, Random rng, double start, double vel)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)(0.3 * SampleRate);
        double lp = 0;
        double bp = 0;
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double white = (rng.NextDouble() * 2) - 1;
            // Cheap band-pass around ~2 kHz (state-variable, fixed coefficients).
            double hp = white - lp - (0.6 * bp);
            bp += 0.28 * hp;
            lp += 0.28 * bp;
            double body = Math.Sin(2 * Math.PI * 185 * t) * Math.Exp(-t * 30);
            double v = ((bp * 0.55 * Math.Exp(-t * 16)) + (body * 0.5)) * vel * 0.35;
            track.Add(s0 + k, v * 0.95, v);
        }
    }

    private static void Hat(Track track, Random rng, double start, double vel, double decay, double pan)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)((decay * 6) * SampleRate);
        double prev = 0;
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double white = (rng.NextDouble() * 2) - 1;
            double hp = white - prev; // crude high-pass
            prev = white;
            double v = hp * Math.Exp(-t / decay) * vel * 0.12;
            track.Add(s0 + k, v * (1 - pan), v * (1 + pan));
        }
    }

    private static float[] Arp(int seed)
    {
        var rng = new Random(seed + 5);
        var track = new Track();
        ForEachPass(offset =>
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int[] chord = Chords[bar / 2];
                // Chord tones an octave up, walked up and down in swung 8ths, with the odd rest.
                int[] tones = [.. chord.Skip(1).Select(m => m + 12)];
                for (int e = 0; e < 8; e++)
                {
                    if (rng.NextDouble() < 0.15)
                    {
                        continue;
                    }

                    int idx = e < 4 ? e : 7 - e;
                    double at = offset + (bar * Bar) + ((e / 2) * Beat) + (Swing(e) * Beat * (e % 2));
                    double pan = (e % 2 == 0 ? -0.3 : 0.3) * rng.NextDouble();
                    Pluck(track, rng, at, tones[idx % tones.Length], 0.55 + (0.3 * rng.NextDouble()), pan);
                }
            }
        });
        track.Process((l, r) => Echo(l, r, 0.75 * Beat, 0.38));
        track.Process((l, r) => Reverb(l, r, 0.25, 0.8));
        return track.Loop(0.7);
    }

    /// <summary>Karplus–Strong plucked string.</summary>
    private static void Pluck(Track track, Random rng, double start, int midi, double vel, double pan)
    {
        double f = Freq(midi);
        int period = Math.Max(2, (int)Math.Round(SampleRate / f));
        var line = new double[period];
        for (int i = 0; i < period; i++)
        {
            line[i] = (rng.NextDouble() * 2) - 1;
        }

        int s0 = (int)(start * SampleRate);
        int n = (int)(1.6 * SampleRate);
        int p = 0;
        double lp = 0;
        for (int k = 0; k < n; k++)
        {
            int next = (p + 1) % period;
            double y = 0.4985 * (line[p] + line[next]);
            line[p] = y;
            p = next;
            lp += 0.35 * (y - lp); // soften
            double v = lp * vel * 0.2 * Math.Min(1, k / 40.0);
            track.Add(s0 + k, v * (1 - pan), v * (1 + pan));
        }
    }

    private static float[] Hype(int seed)
    {
        var rng = new Random(seed + 11);
        var track = new Track();
        ForEachPass(offset =>
        {
            for (int bar = 0; bar < Bars; bar++)
            {
                int[] chord = Chords[bar / 2];
                double b0 = offset + (bar * Bar);

                // Pumping stabs: each beat the chord swells back in after a duck (sidechain feel).
                for (int beat = 0; beat < 4; beat++)
                {
                    SawStab(track, b0 + (beat * Beat), Beat, chord, 0.5);
                }

                // 16th hats, open hat on the off-beats, claps on 2 and 4.
                for (int s = 0; s < 16; s++)
                {
                    Hat(track, rng, b0 + (s * Beat / 4), (s % 4 == 0 ? 0.5 : 0.32) * (0.8 + (0.4 * rng.NextDouble())), 0.03, s % 2 == 0 ? -0.4 : 0.4);
                }

                for (int beat = 0; beat < 4; beat++)
                {
                    Hat(track, rng, b0 + ((beat + 0.5) * Beat), 0.35, 0.14, 0.1);
                }

                Clap(track, rng, b0 + Beat, 0.7);
                Clap(track, rng, b0 + (3 * Beat), 0.7);

                // Bright lead: a pentatonic phrase in 8ths on the second bar of each chord.
                if (bar % 2 == 1)
                {
                    int start = rng.Next(3);
                    for (int e = 0; e < 8; e++)
                    {
                        if (e is 3 or 7)
                        {
                            continue;
                        }

                        int note = Pentatonic[Math.Clamp(start + (e < 4 ? e : 8 - e) + rng.Next(2), 0, Pentatonic.Length - 1)] + 12;
                        Lead(track, b0 + (e * Beat / 2), Beat * 0.45, note, 0.5);
                    }
                }
            }
        });
        track.Process((l, r) => Echo(l, r, 0.5 * Beat, 0.28));
        track.Process((l, r) => Reverb(l, r, 0.18, 0.75));
        return track.Loop(0.55);
    }

    private static void SawStab(Track track, double start, double length, int[] chord, double vel)
    {
        int s0 = (int)(start * SampleRate);
        int n = (int)(length * SampleRate);
        var phases = new double[chord.Length * 2];
        double lpL = 0;
        double lpR = 0;
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double left = 0;
            double right = 0;
            for (int c = 0; c < chord.Length; c++)
            {
                double f = Freq(chord[c] + 12);
                phases[2 * c] = (phases[2 * c] + (f * 1.004 / SampleRate)) % 1;
                phases[(2 * c) + 1] = (phases[(2 * c) + 1] + (f * 0.996 / SampleRate)) % 1;
                left += (2 * phases[2 * c]) - 1;
                right += (2 * phases[(2 * c) + 1]) - 1;
            }

            double pump = Math.Min(1, t / (0.45 * Beat)); // ducked on the beat, swelling back
            pump *= pump;
            lpL += 0.12 * (left - lpL);
            lpR += 0.12 * (right - lpR);
            double g = pump * vel * 0.05 * (k > n - 200 ? (n - k) / 200.0 : 1);
            track.Add(s0 + k, lpL * g, lpR * g);
        }
    }

    private static void Clap(Track track, Random rng, double start, double vel)
    {
        // Three quick bursts, then a short tail.
        foreach (double d in (double[])[0, 0.011, 0.023])
        {
            Hat(track, rng, start + d, vel * 1.6, d < 0.02 ? 0.006 : 0.06, 0);
        }
    }

    private static void Lead(Track track, double start, double length, int midi, double vel)
    {
        double f = Freq(midi);
        int s0 = (int)(start * SampleRate);
        int n = (int)((length + 0.1) * SampleRate);
        for (int k = 0; k < n; k++)
        {
            double t = (double)k / SampleRate;
            double vib = 1 + (0.004 * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1, t / 0.15));
            double phase = 2 * Math.PI * f * vib * t;
            double tone = Math.Sin(phase) + (0.35 * Math.Sin(3 * phase)) + (0.15 * Math.Sin(5 * phase)); // soft square
            double env = Math.Min(1, t / 0.01) * (t < length ? 1 : Math.Exp(-(t - length) / 0.04)) * Math.Exp(-t * 1.5);
            double v = tone * env * vel * 0.07;
            track.Add(s0 + k, v * 0.85, v);
        }
    }

    // ---------- Effects ----------

    private static (float[], float[]) Echo(float[] l, float[] r, double seconds, double feedback)
    {
        int d = (int)(seconds * SampleRate);
        var ol = (float[])l.Clone();
        var or = (float[])r.Clone();
        for (int i = d; i < l.Length; i++)
        {
            // Ping-pong: left echoes to the right and back.
            ol[i] += (float)(or[i - d] * feedback);
            or[i] += (float)(ol[i - d] * feedback);
        }

        return (ol, or);
    }

    /// <summary>A small Schroeder reverb (four combs, two all-passes per channel).</summary>
    private static (float[], float[]) Reverb(float[] l, float[] r, double mix, double room)
    {
        return (Channel(l, 0), Channel(r, 23));

        float[] Channel(float[] x, int spread)
        {
            int[] combs = [1116 + spread, 1188 + spread, 1277 + spread, 1356 + spread];
            int[] allpasses = [556 + spread, 441 + spread];
            var wet = new double[x.Length];
            foreach (int c in combs)
            {
                var buf = new double[c];
                int p = 0;
                double damp = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double y = buf[p];
                    damp = (y * 0.6) + (damp * 0.4);
                    buf[p] = x[i] + (damp * room);
                    p = (p + 1) % c;
                    wet[i] += y;
                }
            }

            foreach (int a in allpasses)
            {
                var buf = new double[a];
                int p = 0;
                for (int i = 0; i < x.Length; i++)
                {
                    double b = buf[p];
                    double y = -wet[i] + b;
                    buf[p] = wet[i] + (b * 0.5);
                    p = (p + 1) % a;
                    wet[i] = y;
                }
            }

            var output = new float[x.Length];
            for (int i = 0; i < x.Length; i++)
            {
                output[i] = (float)((x[i] * (1 - mix)) + (wet[i] * mix * 0.25));
            }

            return output;
        }
    }
}
