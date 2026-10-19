#!/usr/bin/env python3
"""Prepare a brighter CC0 explosion while retaining its body and approximate RMS."""
from array import array
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import subprocess
import wave

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Project-Rule/Assets/_Project/Audio/SFX/Kenney/SciFi/explosionCrunch_000.ogg'
FILTER = 'highpass=f=50,equalizer=f=280:t=q:w=0.8:g=-2.5,highshelf=f=3500:g=5,lowpass=f=14000'


def write_wav(path, samples):
    pcm = array('h', (round(max(-1, min(1, x)) * 32767) for x in samples))
    with wave.open(str(path), 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(44100)
        wav.writeframes(pcm.tobytes())


def decode(filters=None):
    command = ['ffmpeg', '-v', 'error', '-i', str(SOURCE)]
    if filters:
        command += ['-af', filters]
    data = array('f')
    data.frombytes(subprocess.check_output(command + ['-ac', '1', '-ar', '44100', '-f', 'f32le', '-']))
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install', action='store_true')
    args = parser.parse_args()
    original, prepared = decode(), decode(FILTER)
    rms = lambda data: math.sqrt(sum(x*x for x in data)/len(data))
    gain = min(rms(original) / max(rms(prepared), 1e-8), .85 / max(abs(x) for x in prepared))
    prepared = [x * gain for x in prepared]
    folder = ROOT / 'Doc/AudioPreviews/Explosion'
    folder.mkdir(parents=True, exist_ok=True)
    target = folder / 'Explosion_Bright.wav'
    audition = folder / 'Explosion_Audition.wav'
    runtime = SOURCE.parent / 'Explosion_Bright.wav'
    write_wav(target, prepared)
    write_wav(audition, [x * .65 * .85 for x in prepared])
    if args.install:
        shutil.copyfile(target, runtime)
    manifest = {'author': 'Kenney', 'license': 'CC0-1.0', 'source_page': 'https://kenney.nl/assets/sci-fi-sounds',
        'original': str(SOURCE.relative_to(ROOT)), 'original_sha256': hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
        'processing': FILTER, 'normalizing_gain': gain, 'original_rms': rms(original), 'prepared_rms': rms(prepared),
        'peak': max(abs(x) for x in prepared), 'file': str(target.relative_to(ROOT)),
        'sha256': hashlib.sha256(target.read_bytes()).hexdigest(), 'runtime_file': str(runtime.relative_to(ROOT)),
        'audition': str(audition.relative_to(ROOT)), 'audition_sha256': hashlib.sha256(audition.read_bytes()).hexdigest()}
    (ROOT / 'Doc/AudioSources/ExplosionBrightManifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
    print(json.dumps({'original_rms': rms(original), 'prepared_rms': rms(prepared), 'peak': manifest['peak']}))


if __name__ == '__main__':
    main()
