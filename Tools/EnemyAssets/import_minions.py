"""Import delivered art only for gameplay already present in the current project."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
DEST = ROOT / 'Project-Rule/Assets/_Project/Art/Enemies/Minions'


def natural(path):
    return [int(s) if s.isdigit() else s for s in re.split(r'(\d+)', str(path))]


def prepare(source):
    frames, sequences = [], []
    brick = '积木王国_20261018_225547'
    groups = {
        'ExplosiveBlock': (210, (350, 416), True, {
            'Idle': f'{brick}/滑动爆炸小方/滑动爆炸小方_20261018_225655.png',
            'Armed': f'{brick}/滑动爆炸小方/滑动爆炸小方_20261018_225641.png',
            'Warning': f'{brick}/滑动爆炸小方/滑动爆炸小方.png',
            'Explode': f'{brick}/滑动爆炸小方/爆炸'}),
        'ClockMouse': (522 / 1.1, (729, 424), False, {
            'Idle': '发条王国/老鼠.png', 'Walk': '发条王国/跑动平移',
            'Pop': '发条王国/正如我轻轻的来', 'Hide': '发条王国/轻轻的我走了',
            'Dead': '发条王国/嘎'}),
        'PlushBird': (329 / 1.2, (694, 383), False, {
            'Idle': '毛绒王国/毛绒鸟/毛绒鸟.png', 'Fly': '毛绒王国/毛绒鸟/飞行',
            'Attack': '毛绒王国/毛绒鸟/张嘴吐光球', 'Dead': '毛绒王国/毛绒鸟/死亡爆炸'}),
        'BatMinion': (230, (587, 241), True, {
            'Idle': '魔王城/蝙蝠小怪/蝙蝠小怪.png', 'Dash': '魔王城/蝙蝠小怪/冲刺',
            'Dead': '魔王城/蝙蝠小怪/死亡爆炸'}),
        'HugBear': (490 / 1.8, (640, 650), False, {
            'Idle': '毛绒王国/毛绒熊/抱抱熊.png', 'Stand': '毛绒王国/毛绒熊/站起',
            'Walk': '毛绒王国/毛绒熊/走路', 'Dead': '毛绒王国/毛绒熊/拥抱＋死亡消散'}),
        'PlushSeal': (363 / .9, (681, 626), False, {
            'Idle': '毛绒王国/毛绒海豹/毛绒海豹.png', 'Walk': '毛绒王国/毛绒海豹/蛄蛹前进',
            'Howl': '毛绒王国/毛绒海豹/嚎叫', 'BellySlap': '毛绒王国/毛绒海豹/拍肚子',
            'Dead': '毛绒王国/毛绒海豹/消失跳冰洞', 'ProjectileIce': '毛绒王国/毛绒海豹/冰块'}),
        'Firework': (175, (611, 670), True, {'Launch': '魔王城/烟花'}),
    }
    for actor, (ppu, anchor, right, actions) in groups.items():
        paths = {}
        for action, location in actions.items():
            entry = source / location
            originals = [entry] if entry.is_file() else sorted(entry.glob('*.png'), key=natural)
            if not originals:
                raise ValueError(f'Missing frames: {entry}')
            paths[action] = []
            for i, original in enumerate(originals, 1):
                target = DEST / actor / f'{actor}_{action}_{i:02d}.png'
                target.parent.mkdir(parents=True, exist_ok=True)
                pivot, frame_ppu, crop = anchor, ppu, None
                with Image.open(original) as image:
                    if actor == 'ExplosiveBlock' and action != 'Explode':
                        crop = (220, 180, 480, 440)
                    if actor == 'ExplosiveBlock' and action == 'Explode':
                        pivot = (640, 465)
                    if actor == 'ClockMouse' and action in ('Pop', 'Hide'):
                        pivot = (729, 564)
                    if actor == 'PlushBird' and action == 'Attack' and i > 1:
                        pivot, frame_ppu = (497, 347), 98
                        # Independent projectile sprites need tight bounds, not the full character canvas.
                        crop = image.getbbox()
                    if actor == 'BatMinion' and action == 'Idle':
                        pivot = (666, 285)
                    if actor == 'BatMinion' and action == 'Dead':
                        pivot = (570, 380)
                    if actor == 'PlushSeal' and action == 'Idle':
                        pivot = (750, 544)
                    if action == 'ProjectileIce':
                        pivot, frame_ppu = (644, 319), 233
                    if crop:
                        image.crop(crop).save(target)
                        pivot = (pivot[0]-crop[0], pivot[1]-crop[1])
                        width, height = crop[2]-crop[0], crop[3]-crop[1]
                    else:
                        shutil.copyfile(original, target)
                        width, height = image.size
                asset = str(target.relative_to(ROOT / 'Project-Rule'))
                frames.append(dict(path=asset, source=str(original.relative_to(ROOT)),
                                   sourceSha256=hashlib.sha256(original.read_bytes()).hexdigest(),
                                   width=width, height=height, pivotX=pivot[0]/width,
                                   pivotY=1-pivot[1]/height, ppu=frame_ppu, crop=crop))
                paths[action].append(asset)
        if actor == 'ExplosiveBlock':
            paths['Armed'] += paths['Warning']
        if actor == 'PlushBird':
            # The delivery folder mixes the bird pose with two independent light-ball frames.
            paths['ProjectileLight'] = paths['Attack'][1:]
            paths['Attack'] = paths['Attack'][:1]
        if actor == 'BatMinion':
            # Use the flapping part, not the nearly identical folded-wing dash ending.
            paths['Walk'] = paths['Dash'][8:11]
            paths['Fly'] = paths['Walk']
        if actor == 'HugBear':
            paths['Sit'] = list(reversed(paths['Stand']))
            paths['Ready'] = paths['Stand'][-1:]
            # Only the first four frames are an embrace; the rest dissolve the bear.
            paths['Hug'] = paths['Dead'][:4]
            paths['Hurt'] = paths['Walk'][:1]
        if actor == 'PlushSeal':
            paths['Slide'] = paths['Walk']
            paths['Hurt'] = paths['Idle']
        for action, items in paths.items():
            sequences.append(dict(actor=actor, action=action, facesRight=right,
                                  nativeFrameRate=action in ('Idle','Walk','Fly','Armed','ProjectileIce','ProjectileLight'), frames=items))
    all_sources = list(source.rglob('*.png'))
    included = {f['source'] for f in frames}
    excluded = []
    for path in sorted(all_sources):
        relative = str(path.relative_to(ROOT))
        if relative not in included:
            excluded.append(dict(source=relative, sourceSha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                                 reason='Existing StoneGuard art retained' if '石头人' in relative else 'No existing gameplay; source retained only'))
    manifest = dict(frameRate=8, frames=frames, sequences=sequences, retainedSources=excluded)
    (DEST/'MinionArtManifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
    print(f'{len(frames)} runtime PNGs, {len(sequences)} sequences; {len(excluded)} source-only/previously integrated PNGs retained')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    prepare(parser.parse_args().source.resolve())
