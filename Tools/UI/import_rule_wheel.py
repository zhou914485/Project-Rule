"""Extract the delivered paper cards without redrawing their pixels."""
import hashlib
import json
import math
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Src/ui，10(1).18，12am/规则释放'
OUTPUT = ROOT / 'Project-Rule/Assets/_Project/UI/Art/RuleWheel'
# Interior bounds measured from the delivery; ten pixels retain each painted rim.
CARDS = {
    'Attribute': [(1326,292,1568,524),(1490,442,1712,668),(1580,634,1778,854),
                  (1578,872,1778,1092),(1486,1058,1712,1286),(1326,1200,1570,1434)],
    'Relation': [(516,364,758,602),(654,544,868,766),(714,754,898,978),
                 (662,974,876,1206),(542,1148,774,1378)]
}
OUTPUT.mkdir(parents=True, exist_ok=True)
manifest = {'sources': {}, 'cards': {}}
for filename, suffix in [('实心.png','Paper'), ('空心.png','Mask')]:
    path = SOURCE / filename
    manifest['sources'][filename] = hashlib.sha256(path.read_bytes()).hexdigest()
    sheet = Image.open(path).convert('RGBA')
    for group, bounds in CARDS.items():
        centers = [((b[0]+b[2])/2,(b[1]+b[3])/2) for b in bounds]
        for i, b in enumerate(bounds):
            rect = (b[0]-10,b[1]-10,b[2]+10,b[3]+10)
            card = sheet.crop(rect)
            pixels = card.load()
            # Adjacent painted rims can touch. Keep only the pixels nearest this
            # card's interior, so no neighbouring card appears in its crop.
            for y in range(card.height):
                for x in range(card.width):
                    if pixels[x,y][3] == 0: continue
                    wx, wy = x+rect[0], y+rect[1]
                    nearest = min(range(len(centers)), key=lambda j: (wx-centers[j][0])**2+(wy-centers[j][1])**2)
                    if nearest != i: pixels[x,y] = (0,0,0,0)
            name = f'{group}_{i+1:02d}_{suffix}.png'
            card.save(OUTPUT / name)
            manifest['cards'][name] = {'crop': rect, 'sha256': hashlib.sha256((OUTPUT/name).read_bytes()).hexdigest()}
reference = ROOT / 'Doc/References/UI'
reference.mkdir(parents=True, exist_ok=True)
(reference/'RuleWheelArt_20261018.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n')
print(f'Extracted {len(manifest["cards"])} textures into {OUTPUT}')
