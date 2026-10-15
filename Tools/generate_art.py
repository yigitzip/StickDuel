"""Generates the procedural sprites and sounds under Assets/Art.

Run from repo root:  python3 Tools/generate_art.py
Everything is deterministic (fixed seeds) so re-running gives identical files.
"""
import math
import os
import wave

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Art")
SPR = os.path.join(ROOT, "Sprites")
SFX = os.path.join(ROOT, "Audio")
os.makedirs(SPR, exist_ok=True)
os.makedirs(SFX, exist_ok=True)
rng = np.random.default_rng(7)


def save(img, name):
    img.save(os.path.join(SPR, name + ".png"))


def lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(len(a)))


def hexc(h, a=255):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (a,)


def ridge(width, base, amp, octaves, seed):
    r = np.random.default_rng(seed)
    x = np.arange(width)
    y = np.full(width, float(base))
    for o in range(octaves):
        freq = (o + 1) ** 1.7 / width * 2.5
        phase = r.uniform(0, math.tau)
        y += amp / (o + 1) ** 1.3 * np.sin(x * freq * math.tau + phase)
        y += amp / (o + 1) ** 1.6 * np.abs(np.sin(x * freq * 1.9 * math.tau + phase * 2))
    return y


# ---------------------------------------------------------------- basics
def circle(size=128, soft=False):
    img = Image.new("RGBA", (size * 4, size * 4), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((4, 4, size * 4 - 5, size * 4 - 5), fill=(255, 255, 255, 255))
    img = img.resize((size, size), Image.LANCZOS)
    return img


def glow(size=256, power=2.2):
    y, x = np.mgrid[0:size, 0:size]
    c = (size - 1) / 2
    d = np.sqrt((x - c) ** 2 + (y - c) ** 2) / c
    a = np.clip(1 - d, 0, 1) ** power
    arr = np.zeros((size, size, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(arr)


def spark():
    w, h = 128, 32
    y, x = np.mgrid[0:h, 0:w]
    dx = np.abs(x - w / 2) / (w / 2)
    dy = np.abs(y - h / 2) / (h / 2)
    a = np.clip(1 - (dx ** 1.5 + dy ** 2) ** 0.5, 0, 1) ** 1.5
    arr = np.zeros((h, w, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(arr)


def petal():
    s = 4
    img = Image.new("RGBA", (64 * s, 64 * s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.ellipse((8 * s, 20 * s, 56 * s, 44 * s), fill=(255, 255, 255, 255))
    d.polygon([(50 * s, 26 * s), (62 * s, 32 * s), (50 * s, 38 * s)], fill=(0, 0, 0, 0))
    return img.rotate(20, resample=Image.BICUBIC).resize((64, 64), Image.LANCZOS)


def rounded_bar():
    s = 4
    img = Image.new("RGBA", (64 * s, 32 * s), (0, 0, 0, 0))
    ImageDraw.Draw(img).rounded_rectangle((0, 0, 64 * s - 1, 32 * s - 1), radius=12 * s, fill=(255, 255, 255, 255))
    return img.resize((64, 32), Image.LANCZOS)


# ---------------------------------------------------------------- environment
def sky():
    w, h = 64, 1024
    stops = [(0.0, hexc("#140b2e")), (0.38, hexc("#3b1d5c")), (0.62, hexc("#8a3a6b")),
             (0.80, hexc("#e2734f")), (0.92, hexc("#ffb36b")), (1.0, hexc("#ffd59a"))]
    arr = np.zeros((h, w, 4), np.uint8)
    for yy in range(h):
        t = yy / (h - 1)
        for i in range(len(stops) - 1):
            if stops[i][0] <= t <= stops[i + 1][0]:
                k = (t - stops[i][0]) / (stops[i + 1][0] - stops[i][0])
                arr[yy, :] = lerp(stops[i][1], stops[i + 1][1], k)
                break
    return Image.fromarray(arr)


def sun():
    size = 512
    g = np.array(glow(size, 1.6)).astype(float)
    core = np.array(circle(160).resize((160, 160))).astype(float)
    img = np.zeros((size, size, 4))
    img[..., 0], img[..., 1], img[..., 2] = 255, 214, 150
    img[..., 3] = g[..., 3] * 0.55
    o = (size - 160) // 2
    img[o:o + 160, o:o + 160, 3] = np.maximum(img[o:o + 160, o:o + 160, 3], core[..., 3])
    img[o:o + 160, o:o + 160, 1] = np.where(core[..., 3] > 0, 236, img[o:o + 160, o:o + 160, 1])
    return Image.fromarray(img.astype(np.uint8))


def mountains(seed, color, base, amp, octaves, fog_color=None, w=3072, h=768, snow=False):
    y = ridge(w, base, amp, octaves, seed)
    arr = np.zeros((h, w, 4), np.uint8)
    rows = np.arange(h)[:, None]
    mask = rows >= y[None, :]
    col = np.array(color, np.float32)
    shade = np.clip((rows - base + amp) / (h - base + amp), 0, 1) * np.ones((1, w))
    top = col.copy()
    bottom = np.array(fog_color if fog_color else color, np.float32)
    for c in range(4):
        arr[..., c] = np.where(mask, top[c] * (1 - shade) + bottom[c] * shade, 0)
    if snow:
        snowline = mask & (rows < y[None, :] + 14) & (y[None, :] < base - amp * 0.2)
        arr[snowline] = (226, 170, 190, 255)
    img = Image.fromarray(arr)
    return img


def ground_tile():
    w, h = 256, 256
    img = Image.new("RGBA", (w, h), hexc("#241828"))
    d = ImageDraw.Draw(img)
    r = np.random.default_rng(3)
    rows = 4
    bh = h // rows
    for row in range(rows):
        off = (row % 2) * 64
        for col in range(-1, 3):
            x0 = col * 128 + off
            shade = r.uniform(-10, 10)
            c = tuple(int(np.clip(v + shade, 0, 255)) for v in hexc("#3a2a3f")[:3]) + (255,)
            d.rectangle((x0 + 3, row * bh + 3, x0 + 125, row * bh + bh - 3), fill=c)
            d.line((x0 + 4, row * bh + 4, x0 + 124, row * bh + 4), fill=hexc("#4c3a52"), width=2)
    # cracks / speckles
    for _ in range(140):
        x, yy = r.uniform(0, w), r.uniform(0, h)
        d.point((x, yy), fill=hexc("#1b1220"))
    return img


def ground_top():
    w, h = 256, 64
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 22, w, 46), fill=hexc("#5a4560"))  # stone lip
    d.rectangle((0, 22, w, 26), fill=hexc("#8a6f8c"))  # rim light
    d.rectangle((0, 44, w, 48), fill=hexc("#1b1220"))
    r = np.random.default_rng(11)
    # moss tufts hanging over the edge, tileable
    for x in range(0, w, 3):
        hgt = int(4 + 6 * abs(math.sin(x * 0.07)) + r.uniform(0, 5))
        d.line((x, 22, x + r.uniform(-2, 2), 22 - hgt), fill=hexc("#3d5a3a"), width=2)
    for x in range(0, w, 5):
        d.line((x, 26, x, 26 + int(r.uniform(2, 8))), fill=hexc("#2e4a30"), width=2)
    return img


def cliff_side():
    w, h = 256, 768
    y_edge = ridge(h, 60, 22, 4, 5)
    arr = np.zeros((h, w, 4), np.uint8)
    cols = np.arange(w)[None, :]
    mask = cols <= (w - y_edge[:, None])
    shade = np.clip(cols / w, 0, 1)
    a, b = np.array(hexc("#241828"), float), np.array(hexc("#120a16"), float)
    for c in range(4):
        arr[..., c] = np.where(mask, a[c] * (1 - shade) + b[c] * shade, 0)
    return Image.fromarray(arr)


def torii():
    s = 2
    w, h = 900 * s, 700 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    col = hexc("#1a0f20")
    rim = hexc("#a4404a")
    # pillars
    for px in (210, 690):
        d.rectangle(((px - 26) * s, 150 * s, (px + 26) * s, 700 * s), fill=col)
        d.rectangle(((px - 26) * s, 150 * s, (px - 18) * s, 700 * s), fill=rim)
        d.rectangle(((px - 40) * s, 640 * s, (px + 40) * s, 700 * s), fill=col)
    # nuki (lower beam)
    d.rectangle((120 * s, 230 * s, 780 * s, 262 * s), fill=col)
    d.rectangle((120 * s, 230 * s, 780 * s, 236 * s), fill=rim)
    # kasagi (top beam), curved
    pts_top, pts_bot = [], []
    for i in range(41):
        t = i / 40
        x = 30 + t * 840
        curve = 38 * (2 * t - 1) ** 4
        pts_top.append((x * s, (70 - curve) * s))
        pts_bot.append((x * s, (128 - curve * 0.6) * s))
    d.polygon(pts_top + pts_bot[::-1], fill=col)
    d.line(pts_top, fill=rim, width=5 * s)
    # gakuzuka (center plaque)
    d.rectangle((420 * s, 128 * s, 480 * s, 230 * s), fill=col)
    d.rectangle((432 * s, 145 * s, 468 * s, 212 * s), fill=hexc("#3a2030"))
    return img.resize((900, 700), Image.LANCZOS)


def lantern():
    s = 3
    w, h = 140 * s, 300 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    col = hexc("#1e1424")
    d.rectangle((40 * s, 220 * s, 100 * s, 300 * s), fill=col)        # base
    d.rectangle((60 * s, 140 * s, 80 * s, 220 * s), fill=col)         # post
    d.rectangle((25 * s, 125 * s, 115 * s, 145 * s), fill=col)        # platform
    d.rectangle((35 * s, 70 * s, 105 * s, 125 * s), fill=col)         # fire box
    d.rectangle((50 * s, 82 * s, 90 * s, 115 * s), fill=hexc("#ffcf7a"))  # light window
    d.polygon([(5 * s, 72 * s), (70 * s, 25 * s), (135 * s, 72 * s)], fill=col)  # roof
    d.ellipse((60 * s, 5 * s, 80 * s, 28 * s), fill=col)
    return img.resize((140, 300), Image.LANCZOS)


def fog():
    w, h = 1024, 256
    y, x = np.mgrid[0:h, 0:w]
    a = np.exp(-((y - h / 2) / (h / 3.2)) ** 2)
    a *= 0.7 + 0.3 * np.sin(x / w * math.tau * 3) * np.sin(x / w * math.tau * 5 + 1)
    arr = np.zeros((h, w, 4), np.uint8)
    arr[..., :3] = 255
    arr[..., 3] = (np.clip(a, 0, 1) * 255).astype(np.uint8)
    return Image.fromarray(arr).filter(ImageFilter.GaussianBlur(6))


def vignette():
    w, h = 256, 144
    y, x = np.mgrid[0:h, 0:w]
    d = np.sqrt(((x - w / 2) / (w / 2)) ** 2 + ((y - h / 2) / (h / 2)) ** 2)
    a = np.clip((d - 0.65) / 0.75, 0, 1) ** 1.6 * 0.85
    arr = np.zeros((h, w, 4), np.uint8)
    arr[..., 3] = (a * 255).astype(np.uint8)
    return Image.fromarray(arr)


# ---------------------------------------------------------------- props
def sword():
    """Katana pointing right. Grip centre sits at pixel x=48 (pivot set in Unity)."""
    s = 4
    w, h = 320 * s, 40 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = 20 * s
    # handle (tsuka) with wrap diamonds
    d.rounded_rectangle((6 * s, cy - 6 * s, 76 * s, cy + 6 * s), radius=4 * s, fill=hexc("#1c1420"))
    for i in range(7):
        x = (12 + i * 9) * s
        d.polygon([(x, cy), (x + 4 * s, cy - 5 * s), (x + 8 * s, cy), (x + 4 * s, cy + 5 * s)], fill=hexc("#6b2030"))
    d.ellipse((2 * s, cy - 7 * s, 12 * s, cy + 7 * s), fill=hexc("#c9a24a"))  # kashira
    # guard (tsuba)
    d.ellipse((74 * s, cy - 14 * s, 86 * s, cy + 14 * s), fill=hexc("#c9a24a"))
    d.ellipse((77 * s, cy - 10 * s, 83 * s, cy + 10 * s), fill=hexc("#7a5a24"))
    # blade with slight curve
    top, bot = [], []
    for i in range(61):
        t = i / 60
        x = 86 + t * 228
        curve = -9 * t * t
        half = 4.6 * (1 - t ** 6 * 0.95)
        top.append((x * s, (cy / s - half + curve) * s))
        bot.append((x * s, (cy / s + half + curve) * s))
    d.polygon(top + bot[::-1] + [], fill=hexc("#cfd8e6"))
    d.line(top, fill=hexc("#ffffff"), width=2 * s)   # edge highlight
    hamon = [(p[0], p[1] + 3 * s) for p in top[:-4]]
    d.line(hamon, fill=hexc("#eef3fb"), width=1 * s)
    d.line(bot, fill=hexc("#8592a6"), width=2 * s)
    d.rectangle((86 * s, cy - 6 * s, 96 * s, cy + 6 * s), fill=hexc("#c9a24a"))  # habaki
    return img.resize((320, 40), Image.LANCZOS)


def write_all_sprites():
    save(circle(), "Circle")
    save(glow(), "Glow")
    save(spark(), "Spark")
    save(petal(), "Petal")
    save(rounded_bar(), "Bar")
    save(sky(), "Sky")
    save(sun(), "Sun")
    save(mountains(1, hexc("#6a3466"), 330, 120, 6, hexc("#b0566e"), snow=True), "MountainsFar")
    save(mountains(2, hexc("#3e1f48"), 420, 90, 5, hexc("#6e3060")), "MountainsMid")
    save(mountains(3, hexc("#22122a"), 470, 60, 6, hexc("#2c1634")), "Hills")
    save(ground_tile(), "GroundTile")
    save(ground_top(), "GroundTop")
    save(cliff_side(), "Cliff")
    save(torii(), "Torii")
    save(lantern(), "Lantern")
    save(fog(), "Fog")
    save(vignette(), "Vignette")
    save(sword(), "Katana")


# ---------------------------------------------------------------- audio
SR = 44100


def write_wav(name, samples):
    samples = samples / np.abs(samples).max()  # normalise; mix levels live on the AudioSources
    data = (samples * 32767 * 0.9).astype(np.int16)
    with wave.open(os.path.join(SFX, name + ".wav"), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(SR)
        f.writeframes(data.tobytes())


def t_axis(sec):
    return np.arange(int(SR * sec)) / SR


def lowpass(x, alpha):
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc += alpha * (v - acc)
        y[i] = acc
    return y


def bandnoise(sec, lo_alpha, hi_alpha, seed):
    n = np.random.default_rng(seed).standard_normal(int(SR * sec))
    return lowpass(n, lo_alpha) - lowpass(n, hi_alpha)


def swoosh():
    t = t_axis(0.32)
    n = bandnoise(0.32, 0.35, 0.02, 1)
    env = np.sin(np.pi * np.clip(t / 0.32, 0, 1)) ** 2
    return n * env * 2.2


def clang(seed=0):
    tt = np.arange(int(SR * 1.1), dtype=np.float64) / SR
    partials = [(1180, 1.0, 5.0), (1840, 0.7, 6.5), (2710, 0.5, 8.0), (3650, 0.35, 10.0), (5120, 0.2, 14.0)]
    r = np.random.default_rng(seed)
    out = np.zeros(len(tt))
    for freq, amp, decay in partials:
        freq *= r.uniform(0.97, 1.03)
        out += amp * np.sin(math.tau * freq * tt + r.uniform(0, 6)) * np.exp(-decay * tt)
    click = np.random.default_rng(seed + 9).standard_normal(len(tt)) * np.exp(-90.0 * tt)
    return out * 0.45 + click * 0.5


def hit():
    t = t_axis(0.35)
    thud = np.sin(math.tau * (110 - 60 * t) * t) * np.exp(-14 * t)
    slice_ = bandnoise(0.35, 0.6, 0.08, 4) * np.exp(-22 * t) * 1.6
    return thud * 0.9 + slice_


def ko():
    t = t_axis(2.2)
    boom = np.sin(math.tau * (70 - 25 * t) * t) * np.exp(-2.2 * t)
    noise = bandnoise(2.2, 0.2, 0.01, 6) * np.exp(-3 * t) * 1.5
    bell = sum(a * np.sin(math.tau * f * t) * np.exp(-1.4 * t) for f, a in [(220, .5), (331, .3), (440, .25), (587, .15)])
    return boom + noise * 0.6 + bell * 0.6


def beep(freq=660, sec=0.18):
    t = t_axis(sec)
    return (np.sin(math.tau * freq * t) + 0.3 * np.sin(math.tau * freq * 2 * t)) * np.exp(-9 * t) * np.minimum(t * 400, 1)


def gong():
    t = t_axis(2.5)
    parts = [(98, 1.0, 1.0), (196, 0.6, 1.3), (277, 0.45, 1.6), (392, 0.4, 2.0), (523, 0.25, 2.6), (741, 0.18, 3.2)]
    out = sum(a * np.sin(math.tau * f * (1 + 0.004 * np.sin(math.tau * 3 * t)) * t) * np.exp(-d * t) for f, a, d in parts)
    return out * np.minimum(t * 200, 1) * 0.6


def step():
    t = t_axis(0.12)
    return bandnoise(0.12, 0.25, 0.05, 12) * np.exp(-40 * t) * 0.8


def wind():
    sec = 8.0
    n = bandnoise(sec, 0.05, 0.004, 21)
    t = t_axis(sec)
    swell = 0.6 + 0.4 * np.sin(math.tau * t / sec * 2) * np.sin(math.tau * t / sec * 3 + 1)
    out = n * swell * 3
    fade = int(SR * 0.5)  # crossfade the loop seam
    out[:fade] = out[:fade] * np.linspace(0, 1, fade) + out[-fade:] * np.linspace(1, 0, fade)
    return out[:-fade]


def write_all_audio():
    write_wav("Swoosh", swoosh())
    write_wav("Clang1", clang(0))
    write_wav("Clang2", clang(5))
    write_wav("Hit", hit())
    write_wav("KO", ko())
    write_wav("Beep", beep())
    write_wav("Gong", gong())
    write_wav("Step", step())
    write_wav("Wind", wind() * 0.5)


if __name__ == "__main__":
    write_all_sprites()
    write_all_audio()
    print("art written to", os.path.abspath(ROOT))
