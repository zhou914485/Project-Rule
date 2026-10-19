"""Prepare the delivered UI by cropping fixed rectangles; source pixels stay unchanged."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument("source", type=Path)
args = parser.parse_args()
out = ROOT / "Project-Rule/Assets/_Project/UI/Art/Launch"
out.mkdir(parents=True, exist_ok=True)
sources = {}

def load(relative):
    path = args.source / relative
    sources[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
    return Image.open(path).convert("RGBA")

title = load("开始界面/开始界面.png")
title.save(out / "TitleScreen.png")
mask = load("开始界面/按钮按下遮罩.png")
# Coordinates use the artist's 4800 x 2700 canvas, measured from its upper left.
regions = {"Start": (3540, 920, 4430, 1172), "Load": (3540, 1284, 4430, 1536),
           "About": (3540, 1656, 4430, 1908), "Settings": (4500, 72, 4750, 310),
           "Quit": (4500, 2370, 4750, 2600)}
for name, rect in regions.items():
    mask.crop(rect).save(out / f"{name}Pressed.png")

for relative, suffix in [("血量条/血条.png", "Full"),
                         ("血量条/血条_20261018_034515.png", "Empty")]:
    image = load(relative)
    image.crop((3682, 56, 4731, 168)).save(out / f"Health{suffix}.png")
    image.crop((4194, 168, 4686, 216)).save(out / f"Energy{suffix}.png")

sheet = load("标准格式通用UI组件/ui表1.png")
# Reassemble the separated 64px nine-slice samples into a single scalable panel.
panel = Image.new("RGBA", (184, 184))
tiles = [[(70, 150), (216, 150), (361, 150)],
         [(70, 295), (216, 296), (362, 296)],
         [(70, 442), (216, 442), (362, 442)]]
for y, row in enumerate(tiles):
    for x, (left, top) in enumerate(row):
        tile = sheet.crop((left, top, left + 64, top + 64))
        # The delivered upper-right tile contains a close icon; use the mirrored
        # upper-left corner so the separate interactive close button owns the icon.
        if x == 2 and y == 0:
            tile = ImageOps.mirror(sheet.crop((70, 150, 134, 214)))
        # The sample tiles have antialiased cut edges. Trim only their internal
        # joins, keeping the outside border intact, so nine-slicing has no seams.
        tile = tile.crop((2 if x > 0 else 0, 2 if y > 0 else 0,
                          62 if x < 2 else 64, 62 if y < 2 else 64))
        panel.paste(tile, ((0, 62, 122)[x], (0, 62, 122)[y]))
panel.save(out / "CommonPanel.png")
button = Image.new("RGBA", (184, 46))
for x, left in enumerate((215, 361, 507)):
    tile = sheet.crop((left + (2 if x > 0 else 0), 5, left + (62 if x < 2 else 64), 51))
    button.paste(tile, ((0, 62, 122)[x], 0))
button.save(out / "CommonButton.png")
sheet.crop((398, 153, 417, 172)).save(out / "CommonClose.png")

manifest = {"source_folder": args.source.name, "sha256": sources,
            "title_canvas": [4800, 2700], "hotspots": regions,
            "notes": "Fixed crops, mirrored panel corner and nine-slice assembly; trim 2px at internal alpha joins. PSD and reference JPG are not runtime assets."}
reference = ROOT / "Doc/References/UI"
reference.mkdir(parents=True, exist_ok=True)
(reference / "LaunchUI_20261018.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
print(f"Prepared {len(list(out.glob('*.png')))} textures in {out}")
