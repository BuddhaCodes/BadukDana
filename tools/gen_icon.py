#!/usr/bin/env python3
"""Draws Hoshi's app icon: a small kaya Go board seen from above, with a few stones and a gold hoshi star point.
Hoshi's own artwork. Writes src/Hoshi.App/Assets/hoshi.ico (16–256 px) and hoshi.png (512 px), plus
docs/site/icon.png for the promotional page. Usage: python3 tools/gen_icon.py"""
import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(__file__), "..")
S = 1024  # drawn large, then downsampled


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def draw() -> Image.Image:
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # Soft shadow under the board.
    shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle((90, 120, S - 70, S - 40), 150, fill=(0, 0, 0, 150))
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(40)))

    # Board: warm kaya with a vertical grain gradient.
    board = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    bd = ImageDraw.Draw(board)
    x0, y0, x1, y1 = 72, 72, S - 72, S - 72
    for y in range(y0, y1):
        t = (y - y0) / (y1 - y0)
        c = lerp((240, 206, 140), (214, 165, 92), t)
        bd.line([(x0, y), (x1, y)], fill=c + (255,))
    grain = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(grain)
    for i in range(34):  # faint grain streaks, composited (not painted) so the board stays opaque
        x = x0 + (i * 53 + (i * i * 17) % 41) % (x1 - x0)
        gd.line([(x, y0), (x + 8, y1)], fill=(150, 100, 45, 22), width=4 + i % 4)
    board.alpha_composite(grain.filter(ImageFilter.GaussianBlur(3)))
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle((x0, y0, x1, y1), 130, fill=255)
    img.paste(board, (0, 0), mask)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((x0, y0, x1, y1), 130, outline=(150, 98, 40, 255), width=10)

    # A 5×5 corner of the grid (reads as "Go" even at 16 px).
    n = 5
    m0, m1 = 230, S - 230
    step = (m1 - m0) / (n - 1)
    for i in range(n):
        p = m0 + i * step
        d.line([(m0, p), (m1, p)], fill=(70, 42, 16, 255), width=14)
        d.line([(p, m0), (p, m1)], fill=(70, 42, 16, 255), width=14)

    def stone(cx, cy, black):
        r = step * 0.47
        sh = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        ImageDraw.Draw(sh).ellipse((cx - r + 10, cy - r + 22, cx + r + 10, cy + r + 22), fill=(0, 0, 0, 120))
        img.alpha_composite(sh.filter(ImageFilter.GaussianBlur(14)))
        st = Image.new("RGBA", (S, S), (0, 0, 0, 0))
        sd = ImageDraw.Draw(st)
        steps = 40
        for k in range(steps):
            t = k / steps
            rr = r * (1 - t * 0.85)
            ox, oy = -r * 0.32 * t, -r * 0.36 * t
            c = lerp((18, 18, 22), (92, 96, 108), t ** 1.6) if black else lerp((214, 210, 200), (255, 255, 252), t ** 0.7)
            sd.ellipse((cx + ox - rr, cy + oy - rr, cx + ox + rr, cy + oy + rr), fill=c + (255,))
        img.alpha_composite(st)

    gx = lambda i: m0 + i * step
    stone(gx(1), gx(1), True)
    stone(gx(2), gx(1), False)
    stone(gx(3), gx(3), True)
    stone(gx(1), gx(3), False)
    # The hoshi: a gold star point at the centre.
    c = gx(2)
    d.ellipse((c - 34, c - 34, c + 34, c + 34), fill=(201, 150, 40, 255), outline=(120, 80, 10, 255), width=6)
    return img


if __name__ == "__main__":
    big = draw()
    assets = os.path.join(ROOT, "src", "Hoshi.App", "Assets")
    big.resize((512, 512), Image.LANCZOS).save(os.path.join(assets, "hoshi.png"))
    big.save(os.path.join(assets, "hoshi.ico"), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    site = os.path.join(ROOT, "docs", "site")
    os.makedirs(site, exist_ok=True)
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(site, "icon.png"))
    preview = Image.new("RGBA", (16 + 32 + 64 + 128 + 256 + 60, 266), (20, 24, 44, 255))
    x = 10
    for s in (16, 32, 64, 128, 256):
        preview.alpha_composite(big.resize((s, s), Image.LANCZOS), (x, 266 - s - 5))
        x += s + 10
    preview.save("/tmp/claude-0/icon-preview.png")
    print("ok")
