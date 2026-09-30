namespace Hoshi.App.Services.Music;

/// <summary>
/// Mixes the looping layers in sync: per-layer gains glide towards their targets (faster in than out), a master
/// two-pole low-pass follows the heat, and a tape-stop effect slows the playback to a halt and back. Output is
/// 16-bit stereo. Not thread-safe: one thread renders; targets are set through <see cref="SetHeat"/>.
/// </summary>
public sealed class MusicMixer
{
    private const double RiseSeconds = 1.2;
    private const double FallSeconds = 2.5;
    private const double TapeStopSeconds = 0.9;

    private readonly float[][] _layers;
    private readonly int _frames;
    private readonly double[] _gains;
    private readonly double[] _targets;
    private double _position;
    private double _cutoff = 900;
    private double _targetCutoff = 900;
    private double _l1;
    private double _l2;
    private double _r1;
    private double _r2;
    private double _tapeStop = -1; // seconds into the tape-stop, or -1

    public MusicMixer(float[][] layers)
    {
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        _frames = layers[0].Length / 2;
        _gains = new double[layers.Length];
        _targets = new double[layers.Length];
        _gains[0] = _targets[0] = 1;
    }

    /// <summary>Master volume, 0–1.</summary>
    public double Volume { get; set; } = 0.35;

    public IReadOnlyList<double> Gains => _gains;

    public void SetHeat(double heat)
    {
        for (int i = 0; i < _targets.Length; i++)
        {
            _targets[i] = MusicDirector.LayerGain(i, heat);
        }

        _targetCutoff = MusicDirector.Cutoff(heat);
    }

    public void StartTapeStop() => _tapeStop = 0;

    /// <summary>Fills <paramref name="output"/> (interleaved stereo) with the next frames.</summary>
    public void Render(Span<short> output)
    {
        int frames = output.Length / 2;
        const double dt = 1.0 / LofiComposer.SampleRate;
        double rise = 1 - Math.Exp(-dt / RiseSeconds * 4);
        double fall = 1 - Math.Exp(-dt / FallSeconds * 4);
        double cutoffGlide = 1 - Math.Exp(-dt / 1.5 * 4);
        for (int f = 0; f < frames; f++)
        {
            // Gains and cutoff glide per sample, so nothing clicks.
            for (int i = 0; i < _gains.Length; i++)
            {
                _gains[i] += (_targets[i] - _gains[i]) * (_targets[i] > _gains[i] ? rise : fall);
            }

            _cutoff += (_targetCutoff - _cutoff) * cutoffGlide;

            double rate = 1;
            double duck = 1;
            if (_tapeStop >= 0)
            {
                // Slow down to a stop, a beat of silence, then spin back up.
                double k = _tapeStop / TapeStopSeconds;
                rate = k < 0.6 ? 1 - (k / 0.6) : k < 0.75 ? 0 : (k - 0.75) / 0.25;
                duck = k < 0.6 ? 1 - (0.6 * k / 0.6) : k < 0.75 ? 0 : 0.4 + (0.6 * (k - 0.75) / 0.25);
                _tapeStop += dt;
                if (_tapeStop >= TapeStopSeconds)
                {
                    _tapeStop = -1;
                }
            }

            int i0 = (int)_position;
            int i1 = (i0 + 1) % _frames;
            double u = _position - i0;
            double left = 0;
            double right = 0;
            for (int layer = 0; layer < _layers.Length; layer++)
            {
                double g = _gains[layer];
                if (g < 1e-4)
                {
                    continue;
                }

                float[] x = _layers[layer];
                left += g * ((x[2 * i0] * (1 - u)) + (x[2 * i1] * u));
                right += g * ((x[(2 * i0) + 1] * (1 - u)) + (x[(2 * i1) + 1] * u));
            }

            // Two cascaded one-pole low-passes (12 dB/oct).
            double a = 1 - Math.Exp(-2 * Math.PI * _cutoff * dt);
            _l1 += a * (left - _l1);
            _l2 += a * (_l1 - _l2);
            _r1 += a * (right - _r1);
            _r2 += a * (_r1 - _r2);

            double outL = Math.Tanh(_l2 * 1.2) * Volume * duck;
            double outR = Math.Tanh(_r2 * 1.2) * Volume * duck;
            output[2 * f] = (short)(Math.Clamp(outL, -1, 1) * 32767);
            output[(2 * f) + 1] = (short)(Math.Clamp(outR, -1, 1) * 32767);

            _position += rate;
            if (_position >= _frames)
            {
                _position -= _frames;
            }
        }
    }
}
