"""Turns the painted arena references in Tools/reference into in-game backgrounds.

Run from repo root:  python3 Tools/prepare_backgrounds.py
Each 1600x893 painting (the 2752x1536 forest, space, neon and deserted ones are scaled down) gets padding so the camera never sees past it: the sky is
stretched upwards, the ground mirrored downwards and the sides mirrored outwards.
Arenas.cs places them at 92 px/unit with the playable ground row at world y=0, and
knows the padding below (keep PAD_* in sync with it).
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

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


# Forest: the painted ivy swings in game (Arenas.cs SwingingIvy), so it comes off the background
# and goes in on its own. forest_guide.png is the collision reference the Forest numbers in Arenas.cs
# were measured from (red = floors, green = climbable vines, blue = the ivy's anchor).
def ivy_alpha():
    return np.asarray(Image.open(os.path.join(REF, "forest_ivy.png")).convert("RGBA"))[..., 3] > 0


def lift_ivy(img):
    """Paints the ivy out of the forest: each of its pixels takes the scenery just beside it (the
    trees to its left cloned in, to its right where that is ivy too), blurred scenery where both are."""
    rgb = np.asarray(img, dtype=np.float32)
    hole = ndimage.binary_dilation(ivy_alpha(), iterations=8)
    valid = (~hole).astype(np.float32)
    weight = ndimage.gaussian_filter(valid, 30)
    out = ndimage.gaussian_filter(rgb * valid[..., None], (30, 30, 0)) / np.maximum(weight, 1e-3)[..., None]
    shift = 64  # wider than the ivy's stem, so both samples land on real trees
    for side in (-shift, shift):
        clone, ok = np.roll(rgb, side, axis=1), np.roll(valid, side, axis=1) > 0
        out[ok] = clone[ok]
    out[~hole] = rgb[~hole]
    # soften the seam
    seam = ndimage.binary_dilation(hole, iterations=3) & ~ndimage.binary_erosion(hole, iterations=3)
    out[seam] = ndimage.gaussian_filter(out, (2, 2, 0))[seam]
    return Image.fromarray(out.clip(0, 255).astype(np.uint8))


def ivy():
    """The ivy alone, cropped so its root is the top-right corner and its tip the bottom-left one
    (Arenas.cs hangs it by that corner and reads the swing length and rest angle off the size)."""
    img = Image.open(os.path.join(REF, "forest_ivy.png")).convert("RGBA")
    alpha = np.asarray(img)[..., 3] > 0
    ys, xs = np.nonzero(alpha)
    root_x = xs[ys.argmin()]  # the topmost pixel is where it hangs from; a twig further right is dropped
    return img.crop((xs.min(), ys.min(), root_x + 4, ys.max() + 1))


# Deserted: deserted_guide.png is the collision reference the numbers in Arenas.cs were measured from
# (red = floors, green = the ladder, pink = where the missiles come in). The missile flies in game
# (Arenas.cs MissileHazard), so it is cut out of deserted_missile.png at the painting's full resolution.
def missile():
    img = Image.open(os.path.join(REF, "deserted_missile.png")).convert("RGBA")
    ys, xs = np.nonzero(np.asarray(img)[..., 3] > 0)
    return img.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


# Train: the layers scroll past forever in Arenas.cs (Treadmill), so they keep their full 2752 px
# width, trimmed of the transparent or black edge columns/rows that would show as a seam between copies.
# The train and train_guide.png (red = what can be stood on; the numbers in Arenas.cs were measured
# off it, in its own pixels) share one 10100x8007 canvas: both are cut to the same box (TRAIN_CROP,
# Arenas.cs TrainCropLeft/Top) so they stay exactly on top of each other.
TRAIN_CROP = (770, 470, 6215, 1030)
TRAIN_SCALE = 100 / 140    # the train is stored at 100 px/unit, not the sheet's 140 (Arenas.cs TrainTextureScale): it fits 4096 px
TRAIN_WHEEL_ROW = 1450     # backdrop row the wheels run on (Arenas.cs BackdropWheelRow)
TRAIN_VIEW_FLOOR = 40      # the camera stays this far above the near layer's ragged bottom edge (Arenas.cs BackdropFloor)


def train():
    far = Image.open(os.path.join(REF, "train_far.png")).convert("RGBA").crop((0, 8, 2742, 1536))  # black top rows, clear right edge
    near = Image.open(os.path.join(REF, "train_near.png")).convert("RGBA").crop((4, 655, 2752, 1536))  # clear left edge, empty sky
    size = (round((TRAIN_CROP[2] - TRAIN_CROP[0]) * TRAIN_SCALE), round((TRAIN_CROP[3] - TRAIN_CROP[1]) * TRAIN_SCALE))
    body = Image.open(os.path.join(REF, "train.png")).convert("RGBA").crop(TRAIN_CROP).resize(size, Image.LANCZOS)
    guide = Image.open(os.path.join(REF, "train_guide.png")).convert("RGBA").crop(TRAIN_CROP).resize(size, Image.LANCZOS)
    far.save(os.path.join(OUT, "TrainFar.png"))
    near.save(os.path.join(OUT, "TrainNear.png"))
    body.save(os.path.join(OUT, "Train.png"))
    guide.save(os.path.join(OUT, "TrainGuide.png"))

    # Menu card: the scene as the camera first frames it, two backdrop copies wide.
    scene = Image.new("RGBA", (far.width * 2, far.height))
    for i in range(2):
        scene.alpha_composite(far, (i * far.width, 0))
        scene.alpha_composite(near, (i * far.width, far.height - near.height))
    small = body
    wheels = round((1024 - TRAIN_CROP[1]) * TRAIN_SCALE)  # the train's lowest wheel row
    scene.alpha_composite(small, (far.width - small.width // 2, TRAIN_WHEEL_ROW - 8 - wheels))
    view_h = 1100
    view_w = view_h * 16 // 9
    left = far.width - view_w // 2
    bottom = far.height - TRAIN_VIEW_FLOOR
    return scene.crop((left, bottom - view_h, left + view_w, bottom)).convert("RGB").resize((1024, 576), Image.LANCZOS)


def pad(src, keep_aspect=False):
    """keep_aspect: scale to height H at the painting's own aspect (the wide battlefield) instead of W x H."""
    img = Image.open(src).convert("RGB")
    if src.endswith("forest.png"):
        img = lift_ivy(img)
    width = round(H * img.width / img.height) if keep_aspect else W
    img = img.resize((width, H), Image.LANCZOS)
    if src.endswith("ice.png"):
        img = close_ice_gap(img)
    if src.endswith("volcano.png"):
        img = volcano_without_ground(img)
    W_ = img.width
    out = Image.new("RGB", (W_ + PAD_SIDE * 2, H + PAD_TOP + PAD_BOTTOM))
    out.paste(img, (PAD_SIDE, PAD_TOP))
    # sides: mirror the outer strips
    out.paste(img.crop((0, 0, PAD_SIDE, H)).transpose(Image.FLIP_LEFT_RIGHT), (0, PAD_TOP))
    out.paste(img.crop((W_ - PAD_SIDE, 0, W_, H)).transpose(Image.FLIP_LEFT_RIGHT), (W_ + PAD_SIDE, PAD_TOP))
    full_w = out.width
    # top: the first rows stretched up and blurred into plain sky
    top = out.crop((0, PAD_TOP, full_w, PAD_TOP + 24)).resize((full_w, PAD_TOP), Image.BILINEAR)
    out.paste(top.filter(ImageFilter.GaussianBlur(18)), (0, 0))
    # bottom: the ground mirrored
    bottom = out.crop((0, PAD_TOP + H - PAD_BOTTOM, full_w, PAD_TOP + H)).transpose(Image.FLIP_TOP_BOTTOM)
    out.paste(bottom, (0, PAD_TOP + H))
    return out


ROAD_CUT, ROAD_FEATHER = 670, 10  # battlefield: the dirt road's top edge, just under the lowest spear tip (Arenas.cs RoadCut)


def road_layer(padded):
    """Battlefield road and nearest fences on their own, transparent above ROAD_CUT, for the endless
    scroller: the road moves with the ground while the full painting behind it slides slowly."""
    img = padded.convert("RGBA")
    alpha = Image.new("L", img.size, 0)
    top = PAD_TOP + ROAD_CUT
    d = ImageDraw.Draw(alpha)
    for i in range(ROAD_FEATHER):
        d.line([(0, top + i), (img.width, top + i)], fill=int(255 * i / ROAD_FEATHER))
    d.rectangle((0, top + ROAD_FEATHER, img.width, img.height), fill=255)
    img.putalpha(alpha)
    return img


# RUN!: run_sky, run_hill, run_tower and run_hut share one 2752x1536 canvas, and run_guide.jpg (the same
# canvas at 1600x893) marks it: red = the ground (RUN_GROUND), pink = where towers stand, blue = where a
# hut stands, black = the hut's roof (Arenas/RunMode.cs read the rows below in canvas pixels; keep in sync).
# Everything is stored at RUN_SCALE: on a 1080p screen that is about one texel a pixel.
RUN_SCALE = 0.75
RUN_SEAM = 240                       # columns cross-faded so each layer repeats without a seam
RUN_GROUND = 1262                    # the red line (RunMode.cs GroundRow): the fighters' feet
RUN_FEATHER = 30
RUN_GROUND_ROW = RUN_GROUND - RUN_FEATHER  # the near grass strip fades in over RUN_FEATHER rows, solid from the red line down
RUN_HILL_FADE = (1070, 1150)         # the hill fades out over these rows, before its ragged bottom edge (1153-1190)
RUN_TOWER_CROP = (1527, 23, 2026, 1168)
RUN_HUT_CROP = (2033, 1013, 2740, 1456)
RUN_HUT_BOTTOM = 1560                # the hut's art reaches down to this row, past the bottom of the view
RUN_STONE_CROP = (1575, 1045, 1985, 1162)  # run_tower.png's stone base: the hut's parapet
RUN_PARAPET_COLUMNS = (2110, 2663)   # it spans the hut between the posts' outer edges
RUN_PARAPET_ROW = 1362               # its top: the sniper behind it shows from the chest up
RUN_WINDOW = (2168, 1272, 2606, 1415)  # the hut's dark window (cut out: the sniper stands in it, between front and back)


def seamless(img, n=RUN_SEAM):
    """Cross-fades the last n columns into the first n: the result tiles edge to edge."""
    a = np.asarray(img, dtype=np.float32)
    w = a.shape[1]
    out = a[:, : w - n].copy()
    t = np.linspace(0.0, 1.0, n, dtype=np.float32)[None, :, None]
    out[:, :n] = a[:, w - n:] * (1 - t) + a[:, :n] * t
    return Image.fromarray(out.clip(0, 255).astype(np.uint8), "RGBA")


def twice(img):
    out = Image.new("RGBA", (img.width * 2, img.height))
    out.paste(img, (0, 0))
    out.paste(img, (img.width, 0))
    return out


def small(img):
    return img.resize((round(img.width * RUN_SCALE), round(img.height * RUN_SCALE)), Image.LANCZOS)


def run():
    sky = Image.open(os.path.join(REF, "run_sky.png")).convert("RGBA").crop((0, 0, 2564, 1536))  # clear right edge
    sky = twice(seamless(sky))
    hill = Image.open(os.path.join(REF, "run_hill.png")).convert("RGBA")
    fade = np.asarray(hill).copy()
    a0, a1 = RUN_HILL_FADE
    fade[a0:a1, :, 3] = (fade[a0:a1, :, 3] * np.linspace(1.0, 0.0, a1 - a0)[:, None]).astype(np.uint8)
    fade[a1:, :, 3] = 0
    hill = twice(seamless(Image.fromarray(fade, "RGBA")))
    ground = sky.crop((0, RUN_GROUND_ROW, sky.width, sky.height))
    alpha = np.asarray(ground)[..., 3].astype(np.float32)
    alpha[:RUN_FEATHER] *= (np.linspace(0.0, 1.0, RUN_FEATHER) ** 1.5)[:, None]  # eased: the hill shows through longer
    ground.putalpha(Image.fromarray(alpha.astype(np.uint8)))
    small(sky).save(os.path.join(OUT, "RunSky.png"))
    small(hill).save(os.path.join(OUT, "RunHill.png"))
    small(ground).save(os.path.join(OUT, "RunGround.png"))
    small(Image.open(os.path.join(REF, "run_tower.png")).convert("RGBA").crop(RUN_TOWER_CROP)).save(os.path.join(OUT, "RunTower.png"))

    # The hut is a roof on two posts with an open front. A fighter is taller than that opening, so the
    # sniper stands low behind a stone parapet (the tower's own stone base) and only shows from the chest up.
    hut = Image.open(os.path.join(REF, "run_hut.png")).convert("RGBA").crop(RUN_HUT_CROP)
    tall = Image.new("RGBA", (hut.width, RUN_HUT_BOTTOM - RUN_HUT_CROP[1]))
    tall.paste(hut, (0, 0))
    x0, y0, x1, y1 = (v - o for v, o in zip(RUN_WINDOW, RUN_HUT_CROP[:2] * 2))
    back = Image.new("RGBA", tall.size)
    back.paste(tall.crop((x0, y0, x1, y1)), (x0, y0))
    back.paste(tall.crop((x0, y1 - 20, x1, y1 - 10)).resize((x1 - x0, tall.height - y1 + 10)), (x0, y1 - 10))  # the dark interior carries on down
    front = tall.copy()
    ImageDraw.Draw(front).rectangle((x0, y0, x1 - 1, y1 - 1), fill=(0, 0, 0, 0))
    stone = Image.open(os.path.join(REF, "run_tower.png")).convert("RGBA").crop(RUN_STONE_CROP)
    px0, px1 = (v - RUN_HUT_CROP[0] for v in RUN_PARAPET_COLUMNS)
    top = RUN_PARAPET_ROW - RUN_HUT_CROP[1]
    front.alpha_composite(stone.resize((px1 - px0, tall.height - top), Image.LANCZOS), (px0, top))
    small(front).save(os.path.join(OUT, "RunHut.png"))
    small(back).save(os.path.join(OUT, "RunHutWindow.png"))


def shield():
    """Weapons/Shield.png: shield.jpg off its pale backdrop, turned so its long axis stands upright
    (face to the right, strap to the left), 420 px tall (ShieldWeapon.cs reads it at that size)."""
    rgb = np.asarray(Image.open(os.path.join(REF, "shield.jpg")).convert("RGB")).astype(np.int16)
    pale = (rgb.min(axis=2) > 228) & (np.ptp(rgb, axis=2) < 14)
    backdrop, _ = ndimage.label(pale)
    edge = set(np.unique(np.concatenate([backdrop[0], backdrop[-1], backdrop[:, 0], backdrop[:, -1]]))) - {0}
    keep = ~np.isin(backdrop, list(edge))
    keep = ndimage.binary_opening(keep, iterations=2)
    alpha = ndimage.gaussian_filter(keep.astype(np.float32), 1.0)
    img = Image.fromarray(np.dstack([rgb.astype(np.uint8), (alpha * 255).astype(np.uint8)]), "RGBA")
    ys, xs = np.nonzero(keep)
    cov = np.cov(np.vstack([xs - xs.mean(), ys - ys.mean()]))
    vx, vy = np.linalg.eigh(cov)[1][:, -1]           # long axis
    tilt = np.degrees(np.arctan2(vx, vy))            # from straight down/up
    img = img.rotate(-tilt if abs(tilt) < 90 else 180 - tilt, resample=Image.BICUBIC, expand=True)
    box = img.getchannel("A").point(lambda a: 255 if a > 20 else 0).getbbox()
    img = img.crop(box)
    img.resize((round(img.width * 420 / img.height), 420), Image.LANCZOS).save(os.path.join(HERE, "..", "Assets", "Resources", "Weapons", "Shield.png"))


def main():
    os.makedirs(OUT, exist_ok=True)
    for name in ("shrine", "isles", "volcano", "ice", "forest", "space", "neon", "deserted"):
        pad(os.path.join(REF, name + ".png")).save(os.path.join(OUT, "Bg_" + name + ".png"))
    ivy().save(os.path.join(OUT, "Ivy.png"))
    missile().save(os.path.join(OUT, "Missile.png"))
    train().save(os.path.join(OUT, "TrainPreview.png"))
    battlefield = pad(os.path.join(REF, "battlefield.png"), keep_aspect=True)  # ultrawide: 2105 x 893 painting
    battlefield.save(os.path.join(OUT, "Bg_battlefield.png"))
    road_layer(battlefield).save(os.path.join(OUT, "Bg_battlefield_road.png"))
    icicle().save(os.path.join(OUT, "Icicle.png"))
    volcano = Image.open(os.path.join(REF, "volcano.png")).convert("RGB")
    lava_sea(volcano).save(os.path.join(OUT, "LavaSea.png"))
    basalt_piece(volcano, 30).save(os.path.join(OUT, "BasaltPiece0.png"))
    basalt_piece(volcano, 1330).save(os.path.join(OUT, "BasaltPiece1.png"))


if __name__ == "__main__":
    import sys
    if sys.argv[1:] == ["run"]:  # only the RUN! art and the shield
        run()
        shield()
    else:
        main()
        run()
        shield()
