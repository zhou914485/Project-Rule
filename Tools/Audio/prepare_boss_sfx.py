"""Rebuild the 19 Boss clips exclusively from downloaded online authors' sources.

Requires numpy, soundfile, and tar with 7z support (Windows bsdtar works).
Sources, download URLs, licenses and hashes are cached in Doc/AudioSources/BossOnline.
Does not read Src or existing game audio. Existing output .meta/GUIDs are preserved.
"""
from pathlib import Path
import hashlib
import io
import json
import subprocess
import zipfile
import numpy as np
import soundfile as sf

ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / 'Doc/AudioSources/BossOnline'
OUT = ROOT / 'Project-Rule/Assets/_Project/Audio/SFX/Boss'
PREVIEW = ROOT / 'Doc/AudioPreviews/BossOnline'
RATE = 44100
PACKS = {
    'clock': ('Brian MacIntosh (BMacZero)', 'CC0-1.0', 'https://opengameart.org/content/clock-wind-sounds'),
    'mechanical': ('Brian MacIntosh (BMacZero)', 'CC0-1.0', 'https://opengameart.org/content/mechanical-sounds'),
    'bat': ('AntumDeluge / polymorpheva', 'CC0-1.0', 'https://opengameart.org/content/bat-screeches'),
    'swish': ('qubodup', 'CC0-1.0', 'https://opengameart.org/content/swish-bamboo-stick-weapon-swhoshes'),
    'impact': ('Kenney', 'CC0-1.0', 'https://kenney.nl/assets/impact-sounds'),
    'rpg': ('Kenney', 'CC0-1.0', 'https://kenney.nl/assets/rpg-audio'),
    'earth': ('qubodup', 'CC0-1.0', 'https://opengameart.org/content/earth-element-magic-spell'),
    'spells': ('Ogrebane', 'CC0-1.0', 'https://opengameart.org/content/teleport-spell'),
    'virix': ('ViRiX Dreamcore (David Mckee)', 'CC-BY-3.0', 'https://opengameart.org/content/magic-sfx-sample'),
}

def layer(pack, source, end=None, gain=1., at=0., reverse=False, start=0.):
    return dict(pack=pack, source=source, start=start, end=end, gain=gain, at=at, reverse=reverse)

# Short physical cues retain their own transients and tails. Only spell tails are
# excerpted; there are no synthesized replacement tones, baked pitch changes or EQ.
RECIPES = {
    'BossSummon': (.12, [layer('spells', 'teleport.wav', 1.1)]),
    'BossPhaseChange': (.13, [layer('virix', 'Healing Full.wav', 1.4)]),
    'BrickBossWindup': (.12, [layer('swish', 'swosh-03.flac'), layer('rpg', 'Audio/beltHandle1.ogg', gain=.25)]),
    'BrickBossSlam': (.17, [layer('impact', 'Audio/impactWood_heavy_000.ogg'), layer('impact', 'Audio/impactPunch_heavy_001.ogg', gain=.6)]),
    'BrickBossThrow': (.12, [layer('swish', 'swosh-08.flac'), layer('impact', 'Audio/impactWood_heavy_002.ogg', gain=.25)]),
    'BrickBossSplit': (.14, [layer('earth', 'Earth%20Element%20Magic%20Spell_0.flac', .6),
                           layer('impact', 'Audio/impactWood_heavy_003.ogg', at=.06, gain=.8),
                           layer('impact', 'Audio/impactWood_heavy_004.ogg', at=.17, gain=.5)]),
    'ClockBossCharge': (.13, [layer('clock', 'multiwind2.wav', .65), layer('mechanical', 'mechanical2.wav', gain=.45)]),
    'ClockBossBarrageWarning': (.12, [layer('clock', 'singlewind1.wav'), layer('clock', 'singlewind2.wav', at=.16),
                                    layer('clock', 'singlewind3.wav', at=.29), layer('rpg', 'Audio/metalLatch.ogg', at=.41, gain=.5)]),
    'ClockBossBeam': (.13, [layer('virix', 'Wind effects 5.wav', .65), layer('clock', 'singlewind3.wav', gain=.35)]),
    'ClockBossRewind': (.12, [layer('clock', 'multiwind1.wav', .8, reverse=True), layer('mechanical', 'lightclunk2.wav', at=.65, gain=.5)]),
    'ClockBossStun': (.13, [layer('mechanical', 'clank1.wav', .55), layer('clock', 'singlewind2.wav', gain=.3)]),
    'RabbitBossBurrow': (.11, [layer('rpg', 'Audio/cloth3.ogg'), layer('swish', 'swosh-01.flac', gain=.35)]),
    'RabbitBossShot': (.11, [layer('virix', 'Ice attack 2.wav', .34), layer('swish', 'swosh-08.flac', gain=.3)]),
    'BatBossWindup': (.10, [layer('rpg', 'Audio/cloth1.ogg', .45), layer('bat', 'flac/bat_03.flac', .3, gain=.3)]),
    'BatBossDash': (.12, [layer('swish', 'swosh-03.flac'), layer('rpg', 'Audio/cloth4.ogg', gain=.35)]),
    'BatBossFlap': (.12, [layer('rpg', 'Audio/cloth2.ogg'), layer('rpg', 'Audio/cloth4.ogg', at=.16, gain=.7)]),
    'BatBossRoar': (.11, [layer('bat', 'flac/bat_01.flac', 1.0)]),
}

ALIASES = {'BossRuleCast': 'BossPhaseChange', 'RabbitBossEmerge': 'RabbitBossBurrow'}
CUES = ['BossRuleCast', 'BossSummon', 'BossPhaseChange', 'BrickBossWindup', 'BrickBossSlam', 'BrickBossThrow', 'BrickBossSplit', 'ClockBossCharge', 'ClockBossBarrageWarning', 'ClockBossBeam', 'ClockBossRewind', 'ClockBossStun', 'RabbitBossBurrow', 'RabbitBossEmerge', 'RabbitBossShot', 'BatBossWindup', 'BatBossDash', 'BatBossFlap', 'BatBossRoar']

DOWNLOADS = json.loads((CACHE / 'Downloads.json').read_text(encoding='utf-8'))

def raw_source(pack, source):
    record = next(record for record in DOWNLOADS if record['pack'] == pack and
                  (record['file'].endswith(source) or record['file'].endswith(('.zip', '.7z'))))
    archive = ROOT / record['file']
    data = archive.read_bytes()
    if hashlib.sha256(data).hexdigest() != record['sha256']:
        raise ValueError('Download hash changed: ' + str(archive))
    if archive.suffix == '.zip':
        with zipfile.ZipFile(io.BytesIO(data)) as z:
            data = z.read(source)
    elif archive.suffix == '.7z':
        data = subprocess.run(['tar', '-xOf', str(archive), source], capture_output=True, check=True).stdout
    author, license_id, page = PACKS[pack]
    return data, dict(author=author, license=license_id, url=page, download_url=record['url'],
                      archive_sha256=record['sha256'], source_sha256=hashlib.sha256(data).hexdigest())

def decode(spec):
    data, credit = raw_source(spec['pack'], spec['source'])
    pcm, rate = sf.read(io.BytesIO(data), dtype='float32', always_2d=True)
    pcm = pcm.mean(axis=1)
    # Bound trim to actual recorded signal; discard leading file padding rather
    # than masking it with gain. Individual swishes use the full recorded event.
    active = np.flatnonzero(np.abs(pcm) > max(.0001, float(np.max(np.abs(pcm))) * .025))
    if not len(active):
        raise ValueError('Silent source ' + spec['source'])
    onset = max(0, int(active[0]) - int(rate*.003))
    stop = min(len(pcm), int(active[-1]) + int(rate*.025))
    pcm = pcm[onset:stop]
    begin = int(spec['start'] * rate)
    end = min(len(pcm), int(spec['end'] * rate)) if spec['end'] is not None else len(pcm)
    pcm = pcm[begin:end]
    if spec['reverse']:
        pcm = pcm[::-1]
    pcm = np.interp(np.arange(round(len(pcm)*RATE/rate))*rate/RATE, np.arange(len(pcm)), pcm)
    pcm -= np.mean(pcm)
    n_in, n_out = min(len(pcm), 44), min(len(pcm), 1102)
    pcm[:n_in] *= np.linspace(0, 1, n_in)
    pcm[-n_out:] *= np.linspace(1, 0, n_out)
    # Layer gains are relative loudness, not dependent on each download's gain.
    pcm *= spec['gain'] / max(float(np.sqrt(np.mean(pcm*pcm))), 1e-8)
    return pcm, dict(**spec, **credit)

def main():
    OUT.mkdir(parents=True, exist_ok=True); PREVIEW.mkdir(parents=True, exist_ok=True)
    records = []; sounds = {}
    for cue, (target_rms, layers) in RECIPES.items():
        decoded = [decode(spec) for spec in layers]
        length = max(round(spec['at']*RATE)+len(pcm) for (pcm, _), spec in zip(decoded, layers))
        mixed = np.zeros(length)
        for (pcm, _), spec in zip(decoded, layers):
            offset = round(spec['at']*RATE); mixed[offset:offset+len(pcm)] += pcm
        mixed *= min(target_rms / max(float(np.sqrt(np.mean(mixed*mixed))), 1e-8),
                     .85 / max(float(np.max(np.abs(mixed))), 1e-8))
        path = OUT / (cue+'.wav'); sf.write(path, mixed, RATE, subtype='PCM_16')
        sounds[cue] = mixed
        records.append(dict(cue=cue, output=path.relative_to(ROOT).as_posix(),
                            output_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                            seconds=round(len(mixed)/RATE,4), peak=round(float(np.max(np.abs(mixed))),5),
                            rms=round(float(np.sqrt(np.mean(mixed*mixed))),5), layers=[credit for _,credit in decoded]))
    by_cue = {record['cue']: record for record in records}
    for cue, target in ALIASES.items():
        sounds[cue] = sounds[target]
        by_cue[cue] = dict(by_cue[target], cue=cue)
    records = [by_cue[cue] for cue in CUES]
    (ROOT/'Doc/AudioSources/BossSfxManifest.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    credits = 'Boss audio: online reselection v2\nChanges: excerpts, mono, resampling, gain, fades and layers. No EQ.\n\n'
    for author, license_id, url in PACKS.values():
        credits += author+'\n'+url+'\n'+license_id+'\n\n'
    credits += 'Some of the sounds in this project were created by ViRiX Dreamcore (David Mckee) www.soundcloud.com/virix\n'
    credits += 'CC BY 3.0: https://creativecommons.org/licenses/by/3.0/\nCC0: https://creativecommons.org/publicdomain/zero/1.0/\n'
    (OUT/'Credits.txt').write_text(credits,encoding='utf-8')
    groups = {
        'Brick': [cue for cue in sounds if cue.startswith('Brick')],
        'Clock': [cue for cue in sounds if cue.startswith('Clock')],
        'Rabbit': [cue for cue in sounds if cue.startswith('Rabbit')],
        'Bat': [cue for cue in sounds if cue.startswith('Bat')],
        'Shared': [cue for cue in sounds if cue.startswith('Boss')],
    }
    # Match the current event/main gain in these convenient review reels.
    import re
    bank=(ROOT/'Project-Rule/Assets/_Project/Data/Audio/PlayerWheel_SfxBank.asset').read_text(encoding='utf-8-sig')
    gains={int(c):float(v) for c,v in re.findall(r'  - Cue: (\d+)\n.*?    Volume: ([\d.]+)', bank, re.S)}
    all_clips=[]; timeline=[]; position=0.
    for group, names in groups.items():
        clips=[]
        for cue in names:
            clip=sounds[cue]*gains[CUES.index(cue)+34]*.85
            clips.extend([clip,np.zeros(round(.4*RATE))])
            timeline.append(dict(cue=cue,start=round(position,3),seconds=round(len(clip)/RATE,3)))
            position+=len(clip)/RATE+.4
        sf.write(PREVIEW/(group+'.wav'),np.concatenate(clips),RATE,subtype='PCM_16')
        all_clips.extend(clips)
    sf.write(PREVIEW/'Overview.wav',np.concatenate(all_clips),RATE,subtype='PCM_16')
    (PREVIEW/'Timeline.json').write_text(json.dumps(timeline,indent=2)+'\n')
    print('Prepared',len(sounds)-len(ALIASES),'unique clips for',len(records),'Boss events using online downloads only;',round(sum(r['seconds'] for r in records),2),'seconds')

if __name__ == '__main__':
    main()
