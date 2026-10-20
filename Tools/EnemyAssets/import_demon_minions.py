"""Copy the four approved DiscoBall/SpinningTop frames, preserving the delivered PNGs."""
import hashlib
import json
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Src/小怪（结束/魔王城'
DEST = ROOT / 'Project-Rule/Assets/_Project/Art/Enemies/Minions'


def main():
    frames = []
    for actor, folder, name in [('DiscoBall', '迪斯科球怪', '迪斯科球'),
                                ('SpinningTop', '陀螺怪', '陀螺怪')]:
        for index in (1, 2):
            original = SOURCE / folder / f'{name}-{index}.png'
            target = DEST / actor / f'{actor}_{index:02d}.png'
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, target)
            frames.append({'source': str(original.relative_to(ROOT)),
                           'path': str(target.relative_to(ROOT / 'Project-Rule')),
                           'sha256': hashlib.sha256(original.read_bytes()).hexdigest()})
    (ROOT / 'Tools/EnemyAssets/DemonMinionArtManifest.json').write_text(
        json.dumps({'frames': frames}, ensure_ascii=False, indent=2) + '\n')
    print('Copied 4 original PNGs. Run the Unity DiscoBall/SpinningTop setup menu to import sprites.')


if __name__ == '__main__':
    main()
