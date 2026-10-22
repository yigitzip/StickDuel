"""Turns the painted arena references in Tools/reference into in-game backgrounds.

Run from repo root:  python3 Tools/prepare_backgrounds.py
Each 1600x893 painting gets padding so the camera never sees past it: the sky is
stretched upwards, the ground mirrored downwards and the sides mirrored outwards.
Arenas.cs places them at 92 px/unit with the playable ground row at world y=0, and
knows the padding below (keep PAD_* in sync with it).
"""
import os

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(__file__)
REF = os.path.join(HERE, "reference")
OUT = os.path.join(HERE, "..", "Assets", "Resources", "Arena")
W, H = 1600, 893
PAD_TOP, PAD_BOTTOM, PAD_SIDE = 320, 160, 200


def close_ice_gap(img):
    """Paints over the spiked chasm in the ice cave with the left cliff's ground (feathered edges)."""
    patch = img.crop((60, 560, 560, H))
    mask = Image.new("L", patch.size, 0)
    ImageDraw.Draw(mask).rectangle((36, 22, patch.width - 36, patch.height), fill=255)
    img.paste(patch, (550, 560), mask.filter(ImageFilter.GaussianBlur(14)))
    return img


# Volcano: the painted ground becomes a lava sea with separate basalt pieces floating on it.
LAVA_TOP = 748      # painting row of the pit's wavy lava edge (where the lava texture is cut)
LAVA_ROW = 822      # where the lava sea sits in game, well under the pieces (Arenas.cs LavaRow)
PIECE_TOP = 704     # painting row where the basalt lip starts (ground surface is row 712)


def lava_sea(img):
    """Full-width, seamless lava strip cut from the painted pit, mirrored tile by tile."""
    tile = img.crop((612, LAVA_TOP, 986, 862))  # stop above the pit floor
    width = W + PAD_SIDE * 2
    strip = Image.new("RGB", (width, 420))
    x, flip = 0, False
    while x < width:
        t = tile.transpose(Image.FLIP_LEFT_RIGHT) if flip else tile
        strip.paste(t, (x, 0))
        x += tile.width
        flip = not flip
    below = strip.crop((0, tile.height - 40, width, tile.height)).resize((width, 420 - tile.height), Image.BILINEAR)
    strip.paste(below, (0, tile.height))
    # transparent above the wavy surface: keep only rows whose colour is lava-orange
    rgba = strip.convert("RGBA")
    px = rgba.load()
    for xx in range(width):
        for yy in range(12):
            r_, g_, b_, _ = px[xx, yy]
            if r_ < 170:
                px[xx, yy] = (0, 0, 0, 0)
    return rgba


def basalt_piece(img, x0):
    """One floating piece of the painted basalt ground (260 px wide, lip at the top)."""
    piece = img.crop((x0, PIECE_TOP, x0 + 260, H)).convert("RGBA")
    d = ImageDraw.Draw(piece)
    for i in range(6):  # darker cut sides so pieces read as separate blocks
        d.line([(i, 6), (i, piece.height)], fill=(20, 8, 8, 160 - i * 25))
        d.line([(piece.width - 1 - i, 6), (piece.width - 1 - i, piece.height)], fill=(20, 8, 8, 160 - i * 25))
    return piece


def volcano_without_ground(img):
    """Removes the painted floor and the two upper ledges: spire bases continue down, lava below."""
    lava = lava_sea(img)
    img = img.copy()
    for src_x, dst_x in ((310, 20), (1005, 1290)):  # cover each ledge with the scenery beside it
        patch = img.crop((src_x - 10, 498, src_x + 300, 612))
        mask = Image.new("L", patch.size, 0)
        ImageDraw.Draw(mask).rectangle((12, 14, patch.width - 12, patch.height - 14), fill=255)
        img.paste(patch, (dst_x - 10, 498), mask.filter(ImageFilter.GaussianBlur(5)))
    for y in range(700, LAVA_ROW, 60):
        img.paste(img.crop((0, 640, W, 700)), (0, y))
    img.paste(lava.crop((PAD_SIDE, 0, PAD_SIDE + W, H - LAVA_ROW)).convert("RGB"), (0, LAVA_ROW))
    return img


def icicle():
    """Falling stalactite for the ice cave, pointing down (64x256, placed by Arenas.cs)."""
    s = 4
    w, h = 64 * s, 256 * s
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(4 * s, 0), (60 * s, 0), (34 * s, h - 2 * s), (30 * s, h - 2 * s)], fill=(120, 200, 245, 235))
    d.polygon([(4 * s, 0), (24 * s, 0), (31 * s, h - 8 * s)], fill=(200, 240, 255, 240))
    d.line([(14 * s, 4 * s), (31 * s, h - 20 * s)], fill=(255, 255, 255, 255), width=3 * s)
    d.rectangle((0, 0, w, 10 * s), fill=(235, 248, 255, 255))  # snow cap where it broke off
    return img.resize((64, 256), Image.LANCZOS)


def pad(src):
    img = Image.open(src).convert("RGB").resize((W, H), Image.LANCZOS)
    if src.endswith("ice.png"):
        img = close_ice_gap(img)
    if src.endswith("volcano.png"):
        img = volcano_without_ground(img)
    out = Image.new("RGB", (W + PAD_SIDE * 2, H + PAD_TOP + PAD_BOTTOM))
    out.paste(img, (PAD_SIDE, PAD_TOP))
    # sides: mirror the outer strips
    out.paste(img.crop((0, 0, PAD_SIDE, H)).transpose(Image.FLIP_LEFT_RIGHT), (0, PAD_TOP))
    out.paste(img.crop((W - PAD_SIDE, 0, W, H)).transpose(Image.FLIP_LEFT_RIGHT), (W + PAD_SIDE, PAD_TOP))
    full_w = out.width
    # top: the first rows stretched up and blurred into plain sky
    top = out.crop((0, PAD_TOP, full_w, PAD_TOP + 24)).resize((full_w, PAD_TOP), Image.BILINEAR)
    out.paste(top.filter(ImageFilter.GaussianBlur(18)), (0, 0))
    # bottom: the ground mirrored
    bottom = out.crop((0, PAD_TOP + H - PAD_BOTTOM, full_w, PAD_TOP + H)).transpose(Image.FLIP_TOP_BOTTOM)
    out.paste(bottom, (0, PAD_TOP + H))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in ("shrine", "isles", "volcano", "ice"):
        pad(os.path.join(REF, name + ".png")).save(os.path.join(OUT, "Bg_" + name + ".png"))
    icicle().save(os.path.join(OUT, "Icicle.png"))
    volcano = Image.open(os.path.join(REF, "volcano.png")).convert("RGB")
    lava_sea(volcano).save(os.path.join(OUT, "LavaSea.png"))
    basalt_piece(volcano, 30).save(os.path.join(OUT, "BasaltPiece0.png"))
    basalt_piece(volcano, 1330).save(os.path.join(OUT, "BasaltPiece1.png"))


if __name__ == "__main__":
    main()
