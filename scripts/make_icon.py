#!/usr/bin/env python3
"""Dessine icon.png (1280x720, vignette du catalogue Jellyfin), dans le style des autres plugins k1k4k.

    python scripts/make_icon.py [--out icon.png]

Des cartes blanches tombent dans un entonnoir, une seule carte bleue en sort, « Jellyfin » en blanc,
« Personal Library » en bleu, bandeau dégradé en bas. Police Segoe UI Bold (Windows), sinon DejaVu Sans Bold.
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

    # Contenus du serveur : trois cartes blanches au-dessus de l'entonnoir.
    for x, y in ((150, 150), (232, 132), (314, 150)):
        d.rounded_rectangle((x * s, y * s, (x + 64) * s, (y + 84) * s), radius=10 * s, fill=BG, outline=WHITE, width=10 * s)

    # Entonnoir : trapèze blanc puis goulot.
    cx = 270
    d.polygon([((cx - 150) * s, 262 * s), ((cx + 150) * s, 262 * s), ((cx + 34) * s, 380 * s), ((cx - 34) * s, 380 * s)], fill=WHITE)
    d.rectangle(((cx - 34) * s, 378 * s, (cx + 34) * s, 420 * s), fill=WHITE)

    # Ce qui reste pour l'utilisateur : une carte bleue sous le goulot.
    d.rounded_rectangle(((cx - 32) * s, 440 * s, (cx + 32) * s, 524 * s), radius=10 * s, fill=BLUE)

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
