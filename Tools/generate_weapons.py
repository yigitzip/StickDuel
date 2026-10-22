"""Generates the primitive weapon sprites (saw, club, spear, stone axe).

Run from repo root:  python3 Tools/generate_weapons.py
Writes into Assets/Resources/Weapons (loaded at runtime by Weapons.cs; pivot and
pixels-per-unit are set there). Deterministic. Arena art: Tools/prepare_backgrounds.py.
"""
import os

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Resources")
WEAPONS = os.path.join(ROOT, "Weapons")
os.makedirs(WEAPONS, exist_ok=True)


def hexc(h, a=255):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def save(img, folder, name):
    img.save(os.path.join(folder, name + ".png"))


# ================================================================ WEAPONS
# Every weapon points right with its grip centre at pixel x=48 and is vertically centred.
def saw():
    s = 4
    w, h = 300 * s, 64 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = 32 * s
    # wooden pistol handle
    d.rounded_rectangle((6 * s, cy - 13 * s, 84 * s, cy + 13 * s), radius=10 * s, fill=hexc("#7a4a26"))
    d.rounded_rectangle((22 * s, cy - 6 * s, 66 * s, cy + 6 * s), radius=6 * s, fill=hexc("#3a220f"))
    for x in (14, 76):
        d.ellipse(((x - 3) * s, (32 - 3 - 6) * s, (x + 3) * s, (32 + 3 - 6) * s), fill=hexc("#c9c2b0"))
    # tapered steel blade with teeth on the lower edge
    top = [(84 * s, cy - 15 * s), (292 * s, cy - 8 * s)]
    d.polygon([top[0], top[1], (292 * s, cy + 9 * s), (84 * s, cy + 16 * s)], fill=hexc("#b9c0c8"))
    d.line(top, fill=hexc("#e9eef3"), width=2 * s)
    for i in range(30):
        x0 = (86 + i * 7) * s
        yb = cy + (16 - 7 * (i / 30)) * s
        d.polygon([(x0, yb - 1 * s), (x0 + 3 * s, yb + 7 * s), (x0 + 7 * s, yb - 1 * s)], fill=hexc("#8c949e"))
    # rust patches and a hanging hole
    rng = np.random.default_rng(2)
    for _ in range(9):
        x = rng.uniform(110, 280) * s
        y = cy + rng.uniform(-8, 8) * s
        r = rng.uniform(2, 6) * s
        d.ellipse((x - r, y - r, x + r, y + r), fill=hexc("#8a4b22", 160))
    d.ellipse((268 * s, cy - 4 * s, 278 * s, cy + 4 * s), fill=(0, 0, 0, 0))
    return img.resize((300, 64), Image.LANCZOS)


def club():
    s = 4
    w, h = 280 * s, 76 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = 38 * s
    # shaft widening into a knobbly head
    pts_top, pts_bot = [], []
    for i in range(41):
        t = i / 40
        x = (6 + t * 268) * s
        half = (6 + 22 * t ** 2.2) * s
        pts_top.append((x, cy - half))
        pts_bot.append((x, cy + half))
    d.polygon(pts_top + pts_bot[::-1], fill=hexc("#6b4425"))
    d.ellipse((236 * s, cy - 30 * s, 278 * s, cy + 30 * s), fill=hexc("#6b4425"))
    # grain
    for k in (-0.5, 0, 0.5):
        d.line([(x, y + (yb - y) * (0.5 + k * 0.6)) for (x, y), (_, yb) in zip(pts_top[4:], pts_bot[4:])], fill=hexc("#4e2f17"), width=2 * s)
    # leather grip wrap
    for i in range(6):
        x = (16 + i * 10) * s
        d.line([(x, cy - 8 * s), (x + 6 * s, cy + 8 * s)], fill=hexc("#2c1a10"), width=3 * s)
    # iron studs / spikes on the head
    rng = np.random.default_rng(8)
    for _ in range(9):
        x = rng.uniform(170, 268) * s
        t = (x / s - 6) / 268
        half = (6 + 22 * t ** 2.2) * s
        side = rng.choice([-1, 1])
        y = cy + side * half
        d.polygon([(x - 4 * s, y), (x, y + side * 10 * s), (x + 4 * s, y)], fill=hexc("#9aa1a8"))
    for _ in range(6):
        x = rng.uniform(190, 262) * s
        y = cy + rng.uniform(-12, 12) * s
        d.ellipse((x - 3 * s, y - 3 * s, x + 3 * s, y + 3 * s), fill=hexc("#c3c9cf"))
    return img.resize((280, 76), Image.LANCZOS)


def spear():
    s = 4
    w, h = 480 * s, 40 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = 20 * s
    d.rounded_rectangle((6 * s, cy - 4 * s, 404 * s, cy + 4 * s), radius=4 * s, fill=hexc("#8a5a2f"))
    d.line([(6 * s, cy - 2 * s), (404 * s, cy - 2 * s)], fill=hexc("#a8763f"), width=1 * s)
    for i in range(8):  # grip wrap
        x = (26 + i * 6) * s
        d.line([(x, cy - 5 * s), (x + 4 * s, cy + 5 * s)], fill=hexc("#3d2412"), width=2 * s)
    for i in range(5):  # binding under the head
        x = (388 + i * 4) * s
        d.line([(x, cy - 6 * s), (x + 3 * s, cy + 6 * s)], fill=hexc("#d6c08a"), width=2 * s)
    # knapped flint leaf head
    head = [(404 * s, cy - 3 * s), (430 * s, cy - 13 * s), (474 * s, cy), (430 * s, cy + 13 * s), (404 * s, cy + 3 * s)]
    d.polygon(head, fill=hexc("#5c6470"))
    rng = np.random.default_rng(6)
    for _ in range(14):
        x = rng.uniform(410, 465) * s
        y = cy + rng.uniform(-8, 8) * s
        d.ellipse((x - 3 * s, y - 2 * s, x + 3 * s, y + 2 * s), fill=hexc("#7c8592"))
    d.line([(408 * s, cy), (472 * s, cy)], fill=hexc("#a9b1bc"), width=1 * s)
    # feather tassel at the head
    d.polygon([(392 * s, cy + 4 * s), (380 * s, cy + 18 * s), (398 * s, cy + 6 * s)], fill=hexc("#b8322c"))
    return img.resize((480, 40), Image.LANCZOS)


def stone_axe():
    s = 4
    w, h = 280 * s, 120 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = 60 * s
    # bent wooden haft
    d.rounded_rectangle((6 * s, cy - 6 * s, 262 * s, cy + 6 * s), radius=6 * s, fill=hexc("#7a4f2a"))
    for i in range(6):
        x = (16 + i * 10) * s
        d.line([(x, cy - 7 * s), (x + 6 * s, cy + 7 * s)], fill=hexc("#3a2414"), width=3 * s)
    # stone head: wedge sticking up and out, sharpened edge lighter
    headp = [(212 * s, cy + 10 * s), (204 * s, cy - 26 * s), (230 * s, cy - 52 * s), (272 * s, cy - 44 * s),
             (276 * s, cy + 2 * s), (262 * s, cy + 14 * s)]
    d.polygon(headp, fill=hexc("#6f7378"))
    d.polygon([(258 * s, cy - 46 * s), (272 * s, cy - 44 * s), (276 * s, cy + 2 * s), (264 * s, cy + 4 * s)], fill=hexc("#a7adb3"))
    rng = np.random.default_rng(12)
    for _ in range(12):
        x = rng.uniform(214, 258) * s
        y = cy + rng.uniform(-40, 4) * s
        d.ellipse((x - 3 * s, y - 2 * s, x + 3 * s, y + 2 * s), fill=hexc("#5a5e63"))
    # rawhide lashing
    for i in range(5):
        y = cy - 8 * s + i * 5 * s
        d.line([(208 * s, y), (240 * s, y + 4 * s)], fill=hexc("#d2b27a"), width=3 * s)
    return img.resize((280, 120), Image.LANCZOS)


def main():
    save(saw(), WEAPONS, "Saw")
    save(club(), WEAPONS, "Club")
    save(spear(), WEAPONS, "Spear")
    save(stone_axe(), WEAPONS, "StoneAxe")


if __name__ == "__main__":
    main()
