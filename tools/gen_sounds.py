#!/usr/bin/env python3
"""Synthesises Hoshi's cinematic impact sounds (our own work, no third-party samples).

Writes 16-bit stereo 44.1 kHz WAVs to src/Hoshi.App/Assets/Sounds/:

  impact_small.wav     a good move: a soft wooden thump with a short bloom
  explosion_medium.wav an excellent move: a boom with debris and a short tail
  explosion_big.wav    the engine's best move: a cinematic hit (sub drop, low "braam",
                       crackling body, rumbling debris and a long hall reverb)

Deterministic (fixed seeds), so regenerating gives the same files.
Usage: python3 tools/gen_sounds.py
"""
from __future__ import annotations

import os
import wave

import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "src", "Hoshi.App", "Assets", "Sounds")


def t_axis(seconds: float) -> np.ndarray:
    return np.arange(int(seconds * SR)) / SR


def env(t: np.ndarray, attack: float, decay: float) -> np.ndarray:
    """Fast linear attack, exponential decay (decay = time constant in seconds)."""
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-np.maximum(t - attack, 0) / decay)


def lowpass(x: np.ndarray, hz: float, order: int = 4) -> np.ndarray:
    return signal.sosfilt(signal.butter(order, hz, "low", fs=SR, output="sos"), x)


def highpass(x: np.ndarray, hz: float, order: int = 2) -> np.ndarray:
    return signal.sosfilt(signal.butter(order, hz, "high", fs=SR, output="sos"), x)


def bandpass(x: np.ndarray, lo: float, hi: float) -> np.ndarray:
    return signal.sosfilt(signal.butter(2, [lo, hi], "band", fs=SR, output="sos"), x)


def sweeping_lowpass(x: np.ndarray, start: float, end: float, tau: float) -> np.ndarray:
    """Low-pass whose cutoff glides from start to end Hz (block-wise, smooth enough for noise)."""
    out = np.zeros_like(x)
    block = 512
    zi = None
    for i in range(0, len(x), block):
        tt = i / SR
        fc = end + (start - end) * np.exp(-tt / tau)
        sos = signal.butter(2, min(fc, SR / 2 - 100), "low", fs=SR, output="sos")
        if zi is None:
            zi = np.zeros((sos.shape[0], 2))
        out[i:i + block], zi = signal.sosfilt(sos, x[i:i + block], zi=zi)
    return out


def sub_drop(t: np.ndarray, f0: float, f1: float, glide: float, decay: float) -> np.ndarray:
    freq = f1 + (f0 - f1) * np.exp(-t / glide)
    phase = 2 * np.pi * np.cumsum(freq) / SR
    return np.tanh(1.6 * np.sin(phase)) * env(t, 0.004, decay)


def crackle(rng: np.random.Generator, t: np.ndarray, rate: float, decay: float) -> np.ndarray:
    """Sparse random clicks (burning debris), band-limited."""
    x = np.zeros_like(t)
    density = rate * np.exp(-t / decay) / SR
    hits = rng.random(len(t)) < density
    x[hits] = rng.uniform(-1, 1, hits.sum())
    return bandpass(x, 1500, 7000) * 3


def reverb(rng: np.random.Generator, x: np.ndarray, seconds: float, mix: float) -> np.ndarray:
    """Stereo hall: convolution with decaying, darkening noise (different per channel)."""
    n = int(seconds * SR)
    tt = np.arange(n) / SR
    taper = int(len(x) * 0.3)
    x = x.copy()
    x[-taper:] *= np.cos(np.linspace(0, np.pi / 2, taper)) ** 2  # no abrupt end before the tail
    chans = []
    for _ in range(2):
        ir = rng.standard_normal(n) * np.exp(-tt * (6.9 / seconds))
        ir = lowpass(ir, 3500)
        ir[: int(0.012 * SR)] = 0  # pre-delay
        ir /= np.sqrt(np.sum(ir**2))
        wet = np.pad(signal.fftconvolve(x, ir), (0, 1))[: len(x) + n]
        dry = np.pad(x, (0, n))
        chans.append((1 - mix) * dry + mix * wet)
    return np.stack(chans, axis=1)


def finish(stereo: np.ndarray, peak: float = 0.89) -> np.ndarray:
    stereo = np.stack([highpass(c, 28, 4) for c in stereo.T], axis=1)  # no DC or sub-audible drift
    # Gentle limiter, then normalise; short fade to silence at the end.
    stereo = np.tanh(stereo / np.max(np.abs(stereo)) * 1.4)
    stereo *= peak / np.max(np.abs(stereo))
    # Trim the reverb tail once it is inaudible (-50 dB over 50 ms windows).
    win = int(0.05 * SR)
    rms = np.sqrt(np.convolve(np.mean(stereo**2, axis=1), np.ones(win) / win, "same"))
    loud = np.nonzero(rms > peak * 0.012)[0]
    stereo = stereo[: min(len(stereo), loud[-1] + win)]
    fade = min(int(0.25 * SR), len(stereo) // 2)
    stereo[-fade:] *= np.linspace(1, 0, fade)[:, None]
    return stereo


def write(name: str, stereo: np.ndarray) -> None:
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    data = (np.clip(stereo, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"{name}: {len(stereo) / SR:.2f} s, {os.path.getsize(path) // 1024} KiB")


def impact_small() -> np.ndarray:
    rng = np.random.default_rng(11)
    t = t_axis(1.1)
    thump = sub_drop(t, 160, 70, 0.05, 0.16)
    knock = bandpass(rng.standard_normal(len(t)), 300, 2400) * env(t, 0.001, 0.025)
    bloom = lowpass(rng.standard_normal(len(t)), 900) * env(t, 0.01, 0.18) * 0.5
    return finish(reverb(rng, thump + 0.7 * knock + bloom, 0.9, 0.22), 0.7)


def explosion_medium() -> np.ndarray:
    rng = np.random.default_rng(23)
    t = t_axis(2.6)
    noise = rng.standard_normal(len(t))
    boom = sub_drop(t, 120, 42, 0.12, 0.55)
    transient = highpass(noise, 2000) * env(t, 0.0005, 0.012)
    body = sweeping_lowpass(noise, 5000, 250, 0.25) * env(t, 0.003, 0.4)
    rumble = lowpass(np.cumsum(rng.standard_normal(len(t))) * 0.02, 180) * env(t, 0.05, 0.6)
    debris = crackle(rng, t, 900, 0.5)
    mix = 1.0 * boom + 0.6 * transient + 0.9 * body + 0.5 * rumble / (np.max(np.abs(rumble)) + 1e-9) + 0.35 * debris
    return finish(reverb(rng, mix, 1.8, 0.3), 0.82)


def explosion_big() -> np.ndarray:
    rng = np.random.default_rng(42)
    t = t_axis(4.8)
    noise = rng.standard_normal(len(t))
    boom = sub_drop(t, 95, 30, 0.2, 1.3)
    transient = highpass(noise, 1800) * env(t, 0.0005, 0.018)
    body = sweeping_lowpass(noise, 7000, 180, 0.35) * env(t, 0.004, 0.75)
    # Low cinematic "braam": detuned saws, filtered, swelling in just after the hit.
    braam = sum(signal.sawtooth(2 * np.pi * f * t) for f in (54.0, 54.6, 81.3, 108.9))
    braam = lowpass(braam, 420) * env(t, 0.06, 1.6) * 0.22
    rumble = lowpass(np.cumsum(rng.standard_normal(len(t))) * 0.02, 150)
    rumble = rumble / (np.max(np.abs(rumble)) + 1e-9) * env(t, 0.1, 1.2)
    debris = crackle(rng, t, 1400, 0.9) + 0.5 * crackle(rng, t, 300, 2.2)
    mix = 1.1 * boom + 0.7 * transient + 1.0 * body + braam + 0.6 * rumble + 0.35 * debris
    return finish(reverb(rng, mix, 2.8, 0.35), 0.89)


def stone(variant: int) -> np.ndarray:
    """A stone set down on the board, in the spirit of "setting a phone down": a soft, dull, close-up knock on a
    solid surface — the edge touches first, then the stone settles flat a few milliseconds later, with a tiny
    rattle. Muffled (no bright ring, no chime), so it stays pleasant after hundreds of moves. Three variants."""
    rng = np.random.default_rng(100 + variant)
    t = t_axis(0.5)
    out = np.zeros(len(t))

    def contact(at: float, gain: float, brightness: float) -> np.ndarray:
        tt = np.clip(t - at, 0, None)
        gate = (t >= at).astype(float)
        burst = lowpass(rng.standard_normal(len(t)), brightness) * np.exp(-tt / 0.004) * np.clip(tt / 0.0004, 0, 1)
        body = sum(a * np.sin(2 * np.pi * f * (1 + 0.04 * (variant - 1)) * tt) * np.exp(-tt / d)
                   for f, a, d in ((185, 1.0, 0.035), (345, 0.6, 0.024), (910, 0.25, 0.011), (1650, 0.12, 0.006)))
        return (0.8 * burst + body) * gate * gain

    land = 0.012 + 0.004 * variant
    out += contact(0.0, 0.35, 1800)            # edge touches
    out += contact(land, 1.0, 2400)            # settles flat
    out += contact(land + 0.03 + 0.01 * rng.random(), 0.12, 2000)  # tiny rattle
    out = lowpass(out, 4500)
    return finish(reverb(rng, out, 0.25, 0.08), 0.6)


def capture(big: bool) -> np.ndarray:
    """Stones captured: a crisp crack as they shatter, a little debris, the clack of the stones being gathered into
    the lid, and a soft rising chime (two notes, or a three-note arpeggio plus a low thock for a big capture)."""
    rng = np.random.default_rng(300 + big)
    t = t_axis(1.6 if big else 1.1)
    n = len(t)
    noise = rng.standard_normal(n)
    mix = bandpass(noise, 2000, 9000) * env(t, 0.0002, 0.006) * 1.1  # crack
    debris = np.zeros(n)
    for _ in range(14 if big else 7):
        at = int(rng.uniform(0.005, 0.14) * SR)
        burst = bandpass(rng.standard_normal(int(0.01 * SR)), 2500, 8000) * np.exp(-np.arange(int(0.01 * SR)) / (0.002 * SR))
        debris[at:at + len(burst)] += burst * rng.uniform(0.15, 0.4)
    mix += debris
    whoosh = bandpass(noise, 700, 3200)
    mix += whoosh * env(t, 0.03, 0.08) * 0.12
    # Gathered stones: short clacks, slightly different pitches, getting softer.
    for i in range(5 if big else 2):
        at = 0.08 + i * 0.055 + rng.uniform(0, 0.012)
        tt = np.clip(t - at, 0, None)
        gate = (t >= at).astype(float)
        f = rng.uniform(2600, 3600)
        clack = (np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.01) + 0.6 * np.sin(2 * np.pi * f * 1.52 * tt) * np.exp(-tt / 0.006)
                 + 0.5 * np.sin(2 * np.pi * rng.uniform(500, 800) * tt) * np.exp(-tt / 0.03))
        mix += clack * gate * (0.55 - i * 0.07)
    # Reward chime in D minor pentatonic.
    notes = (880.0, 1174.66, 1396.91) if big else (880.0, 1174.66)
    for i, f in enumerate(notes):
        at = 0.14 + i * 0.09
        tt = np.clip(t - at, 0, None)
        gate = (t >= at).astype(float)
        chime = (np.sin(2 * np.pi * f * tt) + 0.3 * np.sin(2 * np.pi * 2 * f * tt)) * np.exp(-tt / 0.35) * np.clip(tt / 0.004, 0, 1)
        mix += chime * gate * 0.16
    if big:
        mix += sub_drop(t, 140, 60, 0.04, 0.12) * 0.5
    return finish(reverb(rng, mix, 0.9, 0.2), 0.7 if big else 0.62)


def atari() -> np.ndarray:
    """A group in atari: a soft, comic "uh-oh" — two bouncy, round notes (a falling minor third), each sagging in
    pitch at the end like a cartoon shrug, with a hint of wobble. Quiet and short."""
    rng = np.random.default_rng(500)
    t = t_axis(0.75)
    out = np.zeros(len(t))
    for start, f0, length in ((0.0, 740.0, 0.13), (0.17, 622.25, 0.24)):
        tt = np.clip(t - start, 0, None)
        gate = ((t >= start) & (t < start + length + 0.08)).astype(float)
        sag = 1 - 0.18 * np.clip((tt - length * 0.45) / (length * 0.55), 0, 1) ** 2  # pitch droops at the end
        wobble = 1 + 0.012 * np.sin(2 * np.pi * 9 * tt)
        phase = 2 * np.pi * np.cumsum(f0 * sag * wobble) / SR
        tone = np.sin(phase) + 0.25 * np.sin(2 * phase) + 0.08 * np.sin(3 * phase)
        amp = np.clip(tt / 0.012, 0, 1) * np.clip((length + 0.08 - tt) / 0.08, 0, 1)
        out += tone * amp * gate
    # A tiny wooden "tok" under the first note for the bounce.
    out += np.sin(2 * np.pi * 300 * t) * env(t, 0.001, 0.02) * 0.4
    return finish(reverb(rng, lowpass(out, 5000), 0.5, 0.15), 0.42)


if __name__ == "__main__":
    write("atari.wav", atari())
    write("capture_small.wav", capture(False))
    write("capture_big.wav", capture(True))
    for i in range(3):
        write(f"stone_{i + 1}.wav", stone(i))
    write("impact_small.wav", impact_small())
    write("explosion_medium.wav", explosion_medium())
    write("explosion_big.wav", explosion_big())
