#!/usr/bin/env python3
"""Hoshi's own board, stone and background art, rendered procedurally (no third-party images).

    python3 tools/art/generate.py            # writes src/Hoshi.App/Assets/Art/...
    python3 tools/art/generate.py --sheet x.png   # also a contact sheet to look at

Everything is deterministic (fixed seeds), so re-running gives the same files. Requires numpy and Pillow.
Boards: 1024 px JPEG (grain runs top to bottom). Stones: 192 px RGBA PNG, several variants per colour.
Backgrounds: 1024 px seamless JPEG tiles.
"""
from __future__ import annotations

import argparse
import math
import os

import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "Hoshi.App", "Assets", "Art")

# --------------------------------------------------------------------------------------------- noise


def smooth(t):
    return t * t * (3 - 2 * t)


def vnoise(h, w, gy, gx, rng):
    """Value noise with gy×gx cells over h×w pixels; periodic (seamless) in both directions."""
    lat = rng.random((gy, gx))
    y = np.arange(h) * gy / h
    x = np.arange(w) * gx / w
    y0 = np.floor(y).astype(int)
    x0 = np.floor(x).astype(int)
    fy = smooth(y - y0)[:, None]
    fx = smooth(x - x0)[None, :]
    y1 = (y0 + 1) % gy
    x1 = (x0 + 1) % gx
    y0 %= gy
    x0 %= gx
    a = lat[y0[:, None], x0[None, :]]
    b = lat[y0[:, None], x1[None, :]]
    c = lat[y1[:, None], x0[None, :]]
    d = lat[y1[:, None], x1[None, :]]
    return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy


def fbm(h, w, gy, gx, octaves, rng, gain=0.5):
    total = np.zeros((h, w))
    amp, norm = 1.0, 0.0
    for i in range(octaves):
        total += amp * vnoise(h, w, gy * 2**i, gx * 2**i, rng)
        norm += amp
        amp *= gain
    return total / norm


def sstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def hexc(s):
    s = s.lstrip("#")
    return np.array([int(s[i:i + 2], 16) for i in (0, 2, 4)], dtype=float)


def mix(c1, c2, t):
    t = np.asarray(t)[..., None]
    return c1 * (1 - t) + c2 * t


def save_rgb(arr, path, quality=90):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB").save(path, quality=quality, optimize=True, progressive=True)


def save_rgba(arr, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGBA").save(path, optimize=True)


def ring_hash(idx, seed):
    v = np.sin(idx * 12.9898 + seed * 78.233) * 43758.5453
    return v - np.floor(v)


# --------------------------------------------------------------------------------------------- boards

N = 1024


def wood_rings(t, latewood=(0.70, 0.86), seed=1):
    """Annual-ring profile: light earlywood rising sharply into a dark latewood band, intensity varying by ring."""
    f = t - np.floor(t)
    late = sstep(latewood[0], latewood[1], f) * (1 - sstep(0.93, 1.0, f)) ** 0.7
    strength = 0.55 + 0.45 * ring_hash(np.floor(t), seed)
    return late * strength


def fibres(rng, gx=320, gy=6, amount=1.0):
    return (fbm(N, N, gy, gx, 3, rng) - 0.5) * amount


def finish(col, rng, pores=0.0, pore_color=None):
    """Large soft colour blotches, fine fibre noise and optional elongated pores."""
    blotch = fbm(N, N, 3, 3, 4, rng) - 0.5
    col = col * (1 + 0.07 * blotch)[..., None]
    col = col * (1 + 0.035 * fibres(rng))[..., None]
    if pores > 0:
        p = fbm(N, N, 40, 900, 1, rng)
        mask = sstep(1 - pores, 1.0, p)
        col = mix(col, pore_color if pore_color is not None else col * 0.6, mask * 0.6)
    return col


def masame(early, late, rings=34, contrast=1.0, seed=1, wobble=1.0):
    """Quarter-sawn (straight grain): rings run top to bottom with irregular spacing and a slow wobble."""
    rng = np.random.default_rng(seed)
    x = np.arange(N)[None, :] / N
    spacing = (fbm(1, N, 1, 9, 3, rng) - 0.5) * 2.6  # irregular ring widths
    warp = (fbm(N, N, 2, 5, 3, rng) - 0.5) * 0.9 * wobble
    t = x * rings + spacing + warp
    late_amt = wood_rings(t, latewood=(0.80, 0.9), seed=seed) * contrast
    col = mix(hexc(early), hexc(late), late_amt)
    return finish(col, rng)


def itame(early, late, rings=16, contrast=1.0, seed=2, centre=0.48, pores=0.0, pore_color=None):
    """Flat-sawn: cathedral arches (parabolic ring contours) rising up the board."""
    rng = np.random.default_rng(seed)
    y = np.arange(N)[:, None] / N
    x = np.arange(N)[None, :] / N
    warp = (fbm(N, N, 3, 3, 4, rng) - 0.5) * 0.35
    dx = x - centre + warp * 0.4
    t = (dx * dx) * 9.0 * rings / 4 + (1 - y) * rings * 0.35 + warp * 2.0
    t += (fbm(N, N, 1, 14, 2, rng) - 0.5) * 0.8
    late_amt = wood_rings(t, latewood=(0.55, 0.93), seed=seed) * contrast * 0.8
    col = mix(hexc(early), hexc(late), late_amt)
    return finish(col, rng, pores, None if pore_color is None else hexc(pore_color))


def bamboo(seed=5):
    """Laminated bamboo: vertical strips of slightly different tone, fine fibres and staggered nodes."""
    rng = np.random.default_rng(seed)
    strips = 13
    w = N / strips
    x = np.arange(N)
    y = np.arange(N)
    idx = np.minimum((x / w).astype(int), strips - 1)
    tone = 0.96 + 0.06 * rng.random(strips)
    hue = rng.random(strips)
    base = mix(hexc("#E2C089"), hexc("#D8B073"), hue[idx][None, :] * np.ones((N, 1)))
    col = base * tone[idx][None, :, None]
    # fibres: very fine vertical streaks
    col = col * (1 + 0.06 * (fbm(N, N, 4, 520, 2, rng) - 0.5))[..., None]
    col = col * (1 + 0.04 * (fbm(N, N, 3, 60, 3, rng) - 0.5))[..., None]
    # nodes: a darker band with a pale lip, at a different height on each strip
    node = np.zeros((N, N))
    for s in range(strips):
        x0, x1 = int(s * w), int((s + 1) * w)
        for k in range(2):
            ny = (rng.random() * 0.5 + k * 0.5) * N
            d = (y - ny)[:, None]
            band = np.exp(-(d / 5.0) ** 2) * 0.07 + np.exp(-((d - 6) / 2.0) ** 2) * -0.04
            node[:, x0:x1] += band
    col = col * (1 - node)[..., None]
    # glue lines between strips
    edge = np.abs(((x / w) - np.round(x / w)) * w)
    seam = np.exp(-(edge / 1.2) ** 2)[None, :] * 0.25
    col = col * (1 - seam)[..., None]
    return col


BOARDS = {
    # id: (render, line colour, coordinates, border colour, border width, average wood colour)
    "kaya-masame": lambda: masame("#ECCB8C", "#C99550", rings=72, contrast=0.55, seed=11),
    "kaya-itame": lambda: itame("#E7C283", "#C38C48", rings=18, contrast=0.5, seed=12),
    "shin-kaya": lambda: masame("#F2DDAA", "#DDB676", rings=80, contrast=0.5, seed=13, wobble=0.6),
    "katsura": lambda: itame("#E3AE76", "#C98C52", rings=14, contrast=0.35, seed=14, centre=0.55),
    "walnut": lambda: itame("#6B4A33", "#3E2A1B", rings=16, contrast=0.6, seed=15, pores=0.05, pore_color="#22160E"),
    "bamboo": lambda: bamboo(seed=16),
}

# --------------------------------------------------------------------------------------------- stones

S = 192          # output size
SS = 3           # supersampling
LIGHT = np.array([-0.42, -0.58, 0.70])
LIGHT /= np.linalg.norm(LIGHT)


def stone_geometry(height=0.30, size=S * SS):
    c = (np.arange(size) + 0.5) / size * 2 - 1
    u, v = np.meshgrid(c, c)
    r = np.sqrt(u * u + v * v)
    inside = r < 1
    rr = np.clip(r, 0, 0.9999)
    # Biconvex lens seen from above: domed top with a rounded edge.
    z = height * np.power(1 - np.power(rr, 3.2), 0.62)
    dzdu = np.gradient(z, axis=1) * size / 2
    dzdv = np.gradient(z, axis=0) * size / 2
    n = np.stack([-dzdu, -dzdv, np.ones_like(z)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return u, v, r, inside, n


def shade(albedo, n, inside, kd=0.85, ambient=0.28, ks=0.2, shininess=24, spec_color=(255, 255, 255),
          env=0.0, env_color=(255, 255, 255), rim=0.0, rim_color=(255, 255, 255), size=S * SS):
    view = np.array([0, 0, 1.0])
    h = LIGHT + view
    h /= np.linalg.norm(h)
    ndl = np.clip((n * LIGHT).sum(-1), 0, 1)
    ndh = np.clip((n * h).sum(-1), 0, 1)
    ndv = np.clip(n[..., 2], 0, 1)
    col = albedo * (ambient + kd * ndl)[..., None]
    col += ks * np.power(ndh, shininess)[..., None] * np.array(spec_color)
    if env > 0:
        # Reflection of a bright window overhead: a soft band on the upper-left of glossy stones.
        refl = 2 * ndv[..., None] * n - view
        sky = sstep(0.15, 0.55, -refl[..., 1] * 0.8 - refl[..., 0] * 0.4)
        col += env * sky[..., None] * np.array(env_color) * (1 - ndv[..., None]) ** 0.6
    if rim > 0:
        fres = np.power(1 - ndv, 3)
        col += rim * fres[..., None] * np.array(rim_color)
    return col


def to_sprite(col, inside, r):
    alpha = np.clip((1 - r) * S * SS * 0.5, 0, 1) * inside
    rgba = np.concatenate([np.clip(col, 0, 255), alpha[..., None] * 255], -1)
    img = Image.fromarray(rgba.astype(np.uint8), "RGBA")
    # premultiply-safe downscale
    pre = np.asarray(img).astype(float)
    pre[..., :3] *= pre[..., 3:4] / 255
    small = Image.fromarray(pre.astype(np.uint8), "RGBA").resize((S, S), Image.LANCZOS)
    arr = np.asarray(small).astype(float)
    a = arr[..., 3:4]
    arr[..., :3] = np.where(a > 0, arr[..., :3] * 255 / np.maximum(a, 1), 0)
    return arr


def local_noise(rng, cells, octaves=4, size=S * SS):
    return fbm(size, size, cells, cells, octaves, rng)


def shell_white(seed):
    """Clam shell: creamy white with fine curved growth bands at a random angle."""
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.27)
    ang = rng.uniform(0, math.pi)
    ru = u * math.cos(ang) + v * math.sin(ang)
    rv = -u * math.sin(ang) + v * math.cos(ang)
    curve = rng.uniform(0.15, 0.35)
    s = rv + curve * ru * ru + (local_noise(rng, 3) - 0.5) * 0.08
    freq = rng.uniform(9, 14)
    t = s * freq + rng.uniform(0, 10)
    f = t - np.floor(t)
    band = np.exp(-((f - 0.5) / 0.13) ** 2) * (0.35 + 0.65 * ring_hash(np.floor(t), seed))
    fine = np.abs(np.sin(s * freq * 4.1 * math.pi)) ** 8 * 0.25
    albedo = mix(hexc("#F8F5EE"), hexc("#CFCFCB"), np.clip(band * 0.75 + fine * 0.45, 0, 1))
    albedo *= (1 + 0.03 * (local_noise(rng, 6) - 0.5))[..., None]
    col = shade(albedo, n, inside, kd=0.78, ambient=0.36, ks=0.22, shininess=30, rim=0.05, rim_color=(200, 205, 215))
    return to_sprite(col, inside, r)


def slate_black(seed, gloss=0.10):
    """Nachiguro slate: soft matte black with a broad sheen and a hint of mineral speckle."""
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.28)
    speck = local_noise(rng, 40, 2)
    mottle = local_noise(rng, 4, 3)
    albedo = mix(hexc("#1B1B1C"), hexc("#2E2E30"), np.clip((mottle - 0.45) * 1.6, 0, 1) * 0.5)
    albedo *= (1 + 0.10 * (speck - 0.5))[..., None]
    col = shade(albedo, n, inside, kd=0.9, ambient=0.5, ks=gloss * 255 / 255, shininess=10, spec_color=(150, 152, 158),
                rim=0.04, rim_color=(90, 92, 100))
    col += (gloss * 60) * np.power(np.clip((n * ((LIGHT + [0, 0, 1]) / np.linalg.norm(LIGHT + [0, 0, 1]))).sum(-1), 0, 1), 6)[..., None]
    return to_sprite(col, inside, r)


def yunzi_white(seed):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.30)
    mottle = local_noise(rng, 5, 3)
    albedo = mix(hexc("#EFE7CF"), hexc("#E4D9BC"), np.clip((mottle - 0.4) * 1.5, 0, 1) * 0.6)
    albedo *= (1 + 0.06 * np.power(r, 4))[..., None]  # a little translucence at the edge
    col = shade(albedo, n, inside, kd=0.75, ambient=0.40, ks=0.16, shininess=18, rim=0.06, rim_color=(255, 244, 214))
    return to_sprite(col, inside, r)


def yunzi_black(seed):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.30)
    mottle = local_noise(rng, 5, 3)
    albedo = mix(hexc("#121511"), hexc("#1D2419"), np.clip((mottle - 0.4) * 1.5, 0, 1) * 0.7)
    col = shade(albedo, n, inside, kd=0.9, ambient=0.5, ks=0.12, shininess=16, spec_color=(160, 175, 150),
                rim=0.22, rim_color=(46, 68, 40))
    return to_sprite(col, inside, r)


def glass(seed, black):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.30)
    if black:
        albedo = np.ones(r.shape + (3,)) * hexc("#0B0C10")
        col = shade(albedo, n, inside, kd=0.5, ambient=0.6, ks=0.95, shininess=140, env=0.38, env_color=(190, 200, 220),
                    rim=0.10, rim_color=(80, 90, 120))
    else:
        cloud = local_noise(rng, 4, 3)
        albedo = mix(hexc("#EEF1F4"), hexc("#DCE3EA"), cloud * 0.6)
        col = shade(albedo, n, inside, kd=0.62, ambient=0.42, ks=0.85, shininess=110, env=0.18, env_color=(255, 255, 255),
                    rim=0.08, rim_color=(170, 190, 215))
    return to_sprite(col, inside, r)


def ceramic(seed, black):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.18)
    grain = local_noise(rng, 30, 2)
    base = hexc("#2A2A2D") if black else hexc("#F3F0EA")
    albedo = np.ones(r.shape + (3,)) * base
    albedo *= (1 + 0.025 * (grain - 0.5))[..., None]
    col = shade(albedo, n, inside, kd=0.55, ambient=0.62 if black else 0.55, ks=0.05, shininess=8)
    return to_sprite(col, inside, r)


def jade(seed):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.30)
    warp = local_noise(rng, 3, 4)
    vein = 1 - 0.55 * np.abs(np.sin((u * 1.2 + v * 0.6 + warp * 1.6) * math.pi)) ** 3
    albedo = mix(hexc("#A9CDA3"), hexc("#D3E8CB"), vein)
    albedo *= (1 + 0.10 * np.power(r, 3))[..., None]
    col = shade(albedo, n, inside, kd=0.6, ambient=0.48, ks=0.55, shininess=70, env=0.10, rim=0.12, rim_color=(210, 245, 205))
    return to_sprite(col, inside, r)


def obsidian(seed):
    rng = np.random.default_rng(seed)
    u, v, r, inside, n = stone_geometry(0.30)
    warp = local_noise(rng, 3, 4)
    swirl = np.abs(np.sin((u * 1.0 - v * 1.4 + warp * 2.2) * math.pi)) ** 10
    albedo = mix(hexc("#08080A"), hexc("#26262C"), swirl * 0.5)
    col = shade(albedo, n, inside, kd=0.6, ambient=0.55, ks=0.9, shininess=120, env=0.30, env_color=(200, 205, 220),
                rim=0.08, rim_color=(110, 110, 130))
    return to_sprite(col, inside, r)


STONES = {
    # id: (white variants, black variants)
    "clam-slate": (lambda i: shell_white(100 + i), 8, lambda i: slate_black(200 + i), 4),
    "yunzi": (lambda i: yunzi_white(300 + i), 4, lambda i: yunzi_black(400 + i), 4),
    "glass": (lambda i: glass(500 + i, False), 3, lambda i: glass(600 + i, True), 3),
    "ceramic": (lambda i: ceramic(700 + i, False), 2, lambda i: ceramic(800 + i, True), 2),
    "jade": (lambda i: jade(900 + i), 4, lambda i: obsidian(1000 + i), 4),
}

# --------------------------------------------------------------------------------------------- backgrounds

T = 1024


def bg_walnut(seed=21):
    """Dark walnut table top: four planks of horizontal grain with fine seams (seamless)."""
    rng = np.random.default_rng(seed)
    planks = 4
    ph = T // planks
    y = np.arange(T)
    out = np.zeros((T, T, 3))
    for k in range(planks):
        rows = slice(k * ph, (k + 1) * ph)
        yy = (y[rows] - k * ph)[:, None] / ph
        warp = (fbm(ph, T, 2, 4, 3, rng) - 0.5) * 1.2
        t = yy * rng.uniform(5, 8) + warp + (fbm(ph, T, 1, 3, 2, rng) - 0.5) * 2
        late = wood_rings(t, latewood=(0.6, 0.85), seed=seed + k)
        col = mix(hexc("#4A3122"), hexc("#2A1A11"), late * 0.85)
        col *= (1 + 0.08 * (fbm(ph, T, 60, 4, 2, rng) - 0.5))[..., None]
        col *= rng.uniform(0.9, 1.08)
        out[rows] = col
    seam = np.exp(-(((y % ph) - 0.5) / 1.3) ** 2)[:, None, None] * 0.55
    out *= 1 - seam
    out *= (1 + 0.05 * (fbm(T, T, 3, 3, 3, rng) - 0.5))[..., None]
    return out


def weave(rng, thread=4.0, slub=0.22):
    """A plain weave: alternating warp and weft threads with uneven (slubby) thickness; seamless."""
    y = np.arange(T)[:, None]
    x = np.arange(T)[None, :]
    k = int(T / thread)
    warp_row = np.sin(x / thread * math.pi) ** 2
    weft_row = np.sin(y / thread * math.pi) ** 2
    over = ((np.floor(x / thread) + np.floor(y / thread)) % 2).astype(float)
    sw = vnoise(1, T, 1, k, rng) * 0  # placeholder for shape
    slub_x = fbm(T, T, 3, k, 2, rng)
    slub_y = fbm(T, T, k, 3, 2, rng)
    tex = over * warp_row * (1 + slub * (slub_x - 0.5)) + (1 - over) * weft_row * (1 + slub * (slub_y - 0.5))
    return tex


def bg_linen(seed=22):
    rng = np.random.default_rng(seed)
    tex = weave(rng, thread=3.2, slub=0.5)
    col = mix(hexc("#25262A"), hexc("#3A3B40"), tex * 0.8)
    col *= (1 + 0.06 * (fbm(T, T, 4, 4, 4, rng) - 0.5))[..., None]
    return col


def bg_slate(seed=23):
    rng = np.random.default_rng(seed)
    y = np.arange(T)[:, None] / T
    layers = fbm(T, T, 2, 4, 5, rng)
    strata = np.sin((y * 14 + layers * 3) * math.pi) * 0.5 + 0.5
    speck = fbm(T, T, 128, 128, 2, rng)
    col = mix(hexc("#22252A"), hexc("#30343A"), strata * 0.5 + layers * 0.4)
    col *= (1 + 0.12 * (speck - 0.5))[..., None]
    return col


def bg_sashiko(seed=24):
    """Indigo cotton with white seigaiha (wave) stitching."""
    rng = np.random.default_rng(seed)
    tex = weave(rng, thread=2.6, slub=0.4)
    col = mix(hexc("#18223A"), hexc("#25314F"), tex * 0.7)
    col *= (1 + 0.10 * (fbm(T, T, 4, 4, 4, rng) - 0.5))[..., None]
    # Seigaiha: rows of overlapping circles; the lowest (front-most) circle covering a pixel is the one seen.
    # 2R divides the tile width and R its height, so the pattern repeats seamlessly.
    R = 64.0
    y = np.arange(T)[:, None].astype(float) * np.ones((1, T))
    x = np.arange(T)[None, :].astype(float) * np.ones((T, 1))
    half = R / 2
    base_row = np.floor(y / half)
    dist = np.full((T, T), -1.0)
    theta = np.zeros((T, T))
    for k in (2, 1, 0):
        row = base_row + k
        cy = row * half
        off = (row % 2) * R
        cx = np.round((x - off) / (2 * R)) * 2 * R + off
        d = np.sqrt((x - cx) ** 2 + (y - cy) ** 2)
        hit = (d < R) & (dist < 0)
        dist = np.where(hit, d, dist)
        theta = np.where(hit, np.arctan2(y - cy, x - cx), theta)
    rings = dist / R * 4  # four rings per wave
    f = rings - np.floor(rings)
    line = np.exp(-((f - 0.5) / 0.07) ** 2) * (dist >= 0) * (dist > R * 0.12)
    # running stitch: dashes along each ring (about 9 px on, 5 px off)
    dash = (np.sin(theta * dist / 14 * 2 * math.pi) > -0.25).astype(float)
    line *= dash
    col = mix(col, hexc("#D9DCE4"), np.clip(line, 0, 1) * 0.55)
    return col


def bg_sudare(seed=25):
    """Bamboo blind: horizontal slats with rounded shading, nodes and vertical binding threads."""
    rng = np.random.default_rng(seed)
    slat = 16
    y = np.arange(T)[:, None]
    x = np.arange(T)[None, :]
    k = T // slat
    f = (y % slat) / slat
    round_ = (np.sin(f * math.pi) ** 0.7) * np.ones((1, T))
    tone = vnoise(T, 1, k, 1, rng)
    col = mix(hexc("#8E6C3E"), hexc("#D2AE72"), np.clip(round_ * (0.75 + 0.25 * tone), 0, 1))
    col *= (1 + 0.08 * (fbm(T, T, k, 256, 2, rng) - 0.5))[..., None]
    gap = (f < 0.06).astype(float)
    col *= (1 - gap * 0.6)[..., None]
    for t_x in (T * 0.25, T * 0.75):
        d = np.abs(x - t_x)
        thread = np.exp(-(d / 2.2) ** 2) * (np.sin(y / slat * math.pi * 2) * 0.5 + 0.5)
        col = mix(col, hexc("#3B2A18"), thread * 0.8)
    return col


BACKGROUNDS = {
    "walnut": bg_walnut,
    "linen": bg_linen,
    "slate": bg_slate,
    "sashiko": bg_sashiko,
    "sudare": bg_sudare,
}

# --------------------------------------------------------------------------------------------- main


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--sheet", help="also write a contact sheet PNG here")
    ap.add_argument("--only", choices=["boards", "stones", "backgrounds"])
    args = ap.parse_args()

    if args.only in (None, "boards"):
        for name, make in BOARDS.items():
            save_rgb(make(), os.path.join(OUT, "Boards", f"{name}.jpg"), quality=88)
            print("board", name)

    if args.only in (None, "stones"):
        for name, (white, nw, black, nb) in STONES.items():
            for i in range(nw):
                save_rgba(white(i), os.path.join(OUT, "Stones", name, f"w{i}.png"))
            for i in range(nb):
                save_rgba(black(i), os.path.join(OUT, "Stones", name, f"b{i}.png"))
            print("stones", name)

    if args.only in (None, "backgrounds"):
        for name, make in BACKGROUNDS.items():
            save_rgb(make(), os.path.join(OUT, "Backgrounds", f"{name}.jpg"), quality=86)
            print("background", name)

    if args.sheet:
        contact_sheet(args.sheet)


def contact_sheet(path):
    """Each board with a few stones of every set on it, plus the backgrounds, for a quick look."""
    tiles = []
    for b in BOARDS:
        board = Image.open(os.path.join(OUT, "Boards", f"{b}.jpg")).convert("RGBA").resize((360, 360), Image.LANCZOS)
        tiles.append((b, board))
    sets = list(STONES)
    sheet = Image.new("RGBA", (6 * 370, 2 * 370 + 380), (20, 22, 30, 255))
    for i, (b, board) in enumerate(tiles):
        img = board.copy()
        for j, s in enumerate(sets):
            for k, col in enumerate("wb"):
                folder = os.path.join(OUT, "Stones", s)
                variants = sorted(f for f in os.listdir(folder) if f.startswith(col))
                st = Image.open(os.path.join(folder, variants[(i + j) % len(variants)])).resize((52, 52), Image.LANCZOS)
                img.alpha_composite(st, (20 + j * 66, 40 + k * 60 + (i % 2) * 150))
        sheet.alpha_composite(img, ((i % 6) * 370, (i // 6) * 370))
    for i, bg in enumerate(BACKGROUNDS):
        t = Image.open(os.path.join(OUT, "Backgrounds", f"{bg}.jpg")).convert("RGBA").resize((360, 360), Image.LANCZOS)
        sheet.alpha_composite(t, ((i % 6) * 370, 370 + 10))
    big = []
    for j, s in enumerate(sets):
        for k, col in enumerate("wb"):
            st = Image.open(os.path.join(OUT, "Stones", s, f"{col}0.png"))
            sheet.alpha_composite(st.resize((150, 150), Image.LANCZOS), (j * 330 + k * 160, 2 * 370 + 20))
    sheet.save(path)
    print("sheet", path)


if __name__ == "__main__":
    main()
