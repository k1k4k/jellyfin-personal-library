#!/usr/bin/env python3
"""Dessine icon.png (1280x720, vignette du catalogue Jellyfin), dans le style des autres plugins k1k4k.

    python scripts/make_icon.py [--out icon.png]

La grille de la bibliothèque : trois cases gardées en bleu, les autres éteintes, et un médaillon utilisateur
(sa vue à lui) ; « Jellyfin » en blanc, « Personal Library » en bleu, bandeau dégradé en bas. Police Segoe UI Bold (Windows), sinon DejaVu Sans Bold.
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 720
SCALE = 4  # dessin en grand puis réduction : bords lisses
BG = (11, 12, 14)
WHITE = (255, 255, 255)
BLUE = (13, 193, 245)
PURPLE = (155, 48, 255)
DIM = (58, 62, 70)


def font(size):
    for path in (r"C:\Windows\Fonts\segoeuib.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "icon.png"))
    out = ap.parse_args().out

    s = SCALE
    img = Image.new("RGB", (W * s, H * s), BG)
    d = ImageDraw.Draw(img)

    def box(v):
        return [c * s for c in v]

    # Grille de la bibliothèque : ce que l'utilisateur garde en bleu, le reste éteint.
    x0, y0, cell, gap = 140, 236, 60, 16
    kept = {(0, 0), (1, 1), (0, 2)}
    for row in range(3):
        for col in range(3):
            x, y = x0 + col * (cell + gap), y0 + row * (cell + gap)
            if (row, col) in kept:
                d.rounded_rectangle(box((x, y, x + cell, y + cell)), radius=10 * s, fill=BLUE)
            else:
                d.rounded_rectangle(box((x, y, x + cell, y + cell)), radius=10 * s, outline=DIM, width=8 * s)

    # Médaillon utilisateur sur le coin de la grille : tête ronde + buste, détouré du fond.
    cx, cy, k = 352, 418, 0.8
    d.ellipse(box((cx - 62 * k, cy - 70 * k, cx + 62 * k, cy + 62 * k)), fill=BG)
    d.ellipse(box((cx - 30 * k, cy - 52 * k, cx + 30 * k, cy + 8 * k)), fill=WHITE)
    d.pieslice(box((cx - 52 * k, cy + 4 * k, cx + 52 * k, cy + 108 * k)), 180, 360, fill=WHITE)

    # Texte : « Jellyfin » / « Personal Library », réduit si besoin pour tenir dans la largeur.
    left, right = 470 * s, (W - 60) * s
    size = 96
    while size > 40:
        title, name = font(130 * s * size // 96), font(size * s)
        if d.textlength("Personal Library", font=name) <= right - left:
            break
        size -= 2
    d.text((left, 360 * s), "Jellyfin", font=title, fill=WHITE, anchor="ls")
    d.text((left, 470 * s), "Personal Library", font=name, fill=BLUE, anchor="ls")

    # Bandeau dégradé en bas.
    band = 30 * s
    for x in range(W * s):
        t = x / (W * s - 1)
        color = tuple(round(BLUE[i] + (PURPLE[i] - BLUE[i]) * t) for i in range(3))
        d.line((x, H * s - band, x, H * s), fill=color)

    img.resize((W, H), Image.LANCZOS).save(out, optimize=True)
    print(out)


if __name__ == "__main__":
    main()
