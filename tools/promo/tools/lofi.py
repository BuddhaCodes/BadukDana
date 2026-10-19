"""Hoshi promo: a calm lo-fi loop synthesised from scratch (no samples, no third-party audio).

80 BPM (one bar = 3 s = 90 frames at 30 fps), Fmaj7 - Em7 - Dm7 - Cmaj7, electric piano, sine bass, soft
drums with swing, a sparse pentatonic melody, vinyl crackle and a gentle low-pass. Deterministic (fixed seed).
Usage: python3 tools/lofi.py public/audio/lofi.wav 62
"""
import sys
import wave

import numpy as np

SR = 44100
BPM = 80.0
BEAT = 60.0 / BPM
BAR = 4 * BEAT
rng = np.random.default_rng(7)


def midi(n):
    return 440.0 * 2 ** ((n - 69) / 12)


def env_adsr(n, a, d, s, r, sustain_len):
    t = np.arange(n) / SR
    e = np.zeros(n)
    a_n, d_n, sl_n = int(a * SR), int(d * SR), int(sustain_len * SR)
    e[:a_n] = np.linspace(0, 1, a_n, endpoint=False)
    e[a_n:a_n + d_n] = np.linspace(1, s, d_n, endpoint=False)
    e[a_n + d_n:sl_n] = s
    rel = np.exp(-(t[sl_n:] - t[sl_n]) / r) * s if sl_n < n else np.array([])
    e[sl_n:] = rel
    return e


def epiano(freq, dur, vel=0.5):
    n = int((dur + 1.2) * SR)
    t = np.arange(n) / SR
    detune = 1.0025
    tone = (np.sin(2 * np.pi * freq * t) + 0.5 * np.sin(2 * np.pi * freq * detune * t)
            + 0.18 * np.sin(2 * np.pi * 2 * freq * t) * np.exp(-t * 3)
            + 0.06 * np.sin(2 * np.pi * 7 * freq * t) * np.exp(-t * 18))  # tine "bell"
    trem = 1 + 0.12 * np.sin(2 * np.pi * 4.2 * t)
    e = env_adsr(n, 0.008, 0.6, 0.35, 0.5, dur)
    return tone * trem * e * vel


def bass(freq, dur, vel=0.6):
    n = int((dur + 0.3) * SR)
    t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * freq * t) + 0.25 * np.sin(2 * np.pi * 2 * freq * t)
    return tone * env_adsr(n, 0.01, 0.2, 0.7, 0.12, dur) * vel


def kick(vel=0.9):
    n = int(0.45 * SR)
    t = np.arange(n) / SR
    f = 46 + 70 * np.exp(-t * 28)
    ph = 2 * np.pi * np.cumsum(f) / SR
    return np.sin(ph) * np.exp(-t * 9) * vel


def snare(vel=0.32):
    n = int(0.3 * SR)
    t = np.arange(n) / SR
    noise = rng.standard_normal(n)
    noise = np.convolve(noise, np.ones(6) / 6, mode="same")  # soften the top
    body = np.sin(2 * np.pi * 185 * t) * np.exp(-t * 30)
    return (noise * np.exp(-t * 16) * 0.7 + body * 0.5) * vel


def hat(vel=0.08, open_=False):
    n = int((0.18 if open_ else 0.05) * SR)
    t = np.arange(n) / SR
    noise = rng.standard_normal(n)
    noise = noise - np.convolve(noise, np.ones(3) / 3, mode="same")  # crude high-pass
    return noise * np.exp(-t * (14 if open_ else 70)) * vel


def place(buf, sig, at):
    i = int(at * SR)
    if i >= len(buf):
        return
    j = min(len(buf), i + len(sig))
    buf[i:j] += sig[: j - i]


def lowpass(x, cutoff):
    # one-pole low-pass, both directions (zero phase)
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for k in range(len(x)):
        acc = (1 - a) * x[k] + a * acc
        y[k] = acc
    acc = 0.0
    for k in range(len(x) - 1, -1, -1):
        acc = (1 - a) * y[k] + a * acc
        y[k] = acc
    return y


def main(out, seconds):
    n = int(seconds * SR)
    keys = np.zeros(n)
    low = np.zeros(n)
    drums = np.zeros(n)
    mel = np.zeros(n)

    chords = [  # Fmaj7, Em7, Dm7, Cmaj7 (voicings around middle C)
        (53, [57, 60, 64, 65]),
        (52, [55, 59, 62, 64]),
        (50, [53, 57, 60, 62]),
        (48, [52, 55, 59, 60]),
    ]
    swing = BEAT * 0.08
    bars = int(seconds / BAR) + 1
    for b in range(bars):
        root, notes = chords[b % 4]
        t0 = b * BAR
        # keys: chord on 1, a softer re-strike on the "and" of 3
        for k, note in enumerate(notes):
            place(keys, epiano(midi(note), BAR * 0.55, 0.16), t0 + k * 0.012)
            place(keys, epiano(midi(note), BAR * 0.3, 0.09), t0 + 2.5 * BEAT + swing + k * 0.01)
        # bass: root on 1, fifth on 3
        place(low, bass(midi(root - 12), BEAT * 1.6), t0)
        place(low, bass(midi(root - 5), BEAT * 1.2, 0.45), t0 + 2 * BEAT)
        # drums from bar 1 on (the opening bar is keys only)
        if b >= 1:
            place(drums, kick(), t0)
            place(drums, kick(0.6), t0 + 2.5 * BEAT + swing)
            place(drums, snare(), t0 + BEAT)
            place(drums, snare(), t0 + 3 * BEAT)
            for h in range(8):
                place(drums, hat(0.07 if h % 2 == 0 else 0.045, open_=(h == 7)), t0 + h * BEAT / 2 + (swing if h % 2 else 0))
        # a sparse pentatonic melody every other bar from bar 2
        if b >= 2 and b % 2 == 0:
            scale = [72, 74, 76, 79, 81]
            for k in range(3):
                note = scale[int(rng.integers(0, len(scale)))]
                place(mel, epiano(midi(note), BEAT * 0.8, 0.11), t0 + (0.5 + k * 1.25) * BEAT + swing)

    mix = 0.9 * keys + 0.9 * low + 0.8 * drums + 0.7 * mel
    mix = lowpass(mix, 5200)
    # vinyl: soft noise bed + sparse crackles
    bed = lowpass(rng.standard_normal(n) * 0.006, 2500)
    crack = np.zeros(n)
    for _ in range(int(seconds * 7)):
        i = int(rng.integers(0, n - 200))
        crack[i:i + 40] += rng.standard_normal(40) * np.exp(-np.arange(40) / 6) * rng.uniform(0.02, 0.08)
    mix = mix + bed + crack
    mix = np.tanh(mix * 1.4) / np.tanh(1.4)  # gentle tape saturation
    mix /= np.max(np.abs(mix)) * 1.12
    # stereo: keys slightly wide
    left = mix + 0.02 * np.roll(keys, 300)
    right = mix + 0.02 * np.roll(keys, -300)
    st = np.stack([left, right], axis=1)
    st /= np.max(np.abs(st)) * 1.05
    data = (st * 32767).astype(np.int16)
    with wave.open(out, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


if __name__ == "__main__":
    main(sys.argv[1], float(sys.argv[2]))
