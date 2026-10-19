#!/usr/bin/env python3
"""Prepare short CC0 air swishes; preserve originals and leave 6 dB headroom."""
from pathlib import Path
import hashlib
import json
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = 'https://opengameart.org/content/swishes-sound-pack'


def main():
    import argparse
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    args = parser.parse_args()
    originals = ROOT / 'Doc/AudioSources/SwishesOriginals'
    playback = ROOT / 'Project-Rule/Assets/_Project/Audio/SFX/Swishes'
    originals.mkdir(parents=True, exist_ok=True)
    playback.mkdir(parents=True, exist_ok=True)
    files = []
    with zipfile.ZipFile(args.archive) as archive:
        for name, source in [('SwordLight_A', 'swish-1.wav'), ('SwordLight_B', 'swish-3.wav'),
                             ('SwordHeavy_A', 'swish-8.wav'), ('SwordHeavy_B', 'swish-9.wav')]:
            data = archive.read('swishes/' + source)
            original = originals / source
            original.write_bytes(data)
            target = playback / (name + '.wav')
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', str(original), '-af',
                            'highpass=f=120,lowpass=f=10000,volume=0.45,afade=t=in:d=0.003',
                            '-ac', '1', '-ar', '44100', '-c:a', 'pcm_s16le', str(target)], check=True)
            files.append({'file': str(target.relative_to(ROOT)), 'original': str(original.relative_to(ROOT)),
                          'original_sha256': hashlib.sha256(data).hexdigest(),
                          'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    (playback / 'Credits.txt').write_text('Swishes Sound Pack by artisticdude\nSource: ' + SOURCE
        + '\nLicense: CC0 1.0\nhttps://creativecommons.org/publicdomain/zero/1.0/\n'
        + 'Prepared variants: mono PCM16 44100Hz, highpass 120Hz, lowpass 10kHz, gain 0.45, 3ms fade-in.\n')
    (originals.parent / 'SwishesManifest.json').write_text(json.dumps({
        'author': 'artisticdude', 'source_page': SOURCE,
        'download': 'https://opengameart.org/sites/default/files/swishes.zip',
        'license': 'CC0-1.0', 'retrieved': '2026-10-18',
        'processing': 'mono PCM16 44100Hz; highpass 120Hz; lowpass 10kHz; gain 0.45; fade-in 3ms',
        'files': files}, indent=2) + '\n')


if __name__ == '__main__':
    main()
