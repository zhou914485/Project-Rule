#!/usr/bin/env python3
"""Prepare CC0 player-feedback candidates and level-matched audition files."""
import argparse
from array import array
import hashlib
import json
import math
import shutil
from pathlib import Path
import subprocess
import wave
import zipfile

ROOT = Path(__file__).resolve().parents[2]
RATE = 44100


def write_wav(path, samples):
    pcm = array('h', (round(max(-1, min(1, x)) * 32767) for x in samples))
    with wave.open(str(path), 'wb') as wav:
        wav.setnchannels(1)
        wav.setsampwidth(2)
        wav.setframerate(RATE)
        wav.writeframes(pcm.tobytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('archive', type=Path)
    parser.add_argument('--install', action='store_true', help='Copy selected prepared clips into existing Unity asset paths')
    args = parser.parse_args()
    originals = ROOT / 'Doc/AudioSources/FeedbackOriginals'
    output = ROOT / 'Doc/AudioPreviews/PlayerFeedback'
    originals.mkdir(parents=True, exist_ok=True)
    output.mkdir(parents=True, exist_ok=True)
    entries = []
    candidates = [
        ('Hurt_A', 'impactPunch_heavy_003.ogg', .20, 6, .60),
        ('Hurt_B', 'impactSoft_medium_003.ogg', .14, 6, .60),
        ('Land_A', 'footstep_concrete_002.ogg', .113, 5, .33),
        ('Land_B', 'footstep_carpet_000.ogg', .145, 5, .33),
    ]
    with zipfile.ZipFile(args.archive) as archive:
        for name, original_name, duration, high_gain, event_volume in candidates:
            raw = archive.read('Audio/' + original_name)
            original = originals / original_name
            original.write_bytes(raw)
            low_cut = 140 if name.startswith('Land') else 100
            processing = f'highpass=f={low_cut},equalizer=f=260:t=q:w=0.8:g=-3,highshelf=f=3500:g={high_gain},lowpass=f=14000,atrim=duration={duration},afade=t=in:d=0.0005,afade=t=out:st={max(0, duration - .018)}:d=0.018'
            decoded = subprocess.check_output(['ffmpeg', '-v', 'error', '-i', str(original),
                '-af', processing, '-ac', '1', '-ar', str(RATE), '-f', 'f32le', '-'])
            samples = array('f')
            samples.frombytes(decoded)
            rms = math.sqrt(sum(x*x for x in samples) / len(samples))
            peak = max(abs(x) for x in samples)
            gain = min(.16 / max(rms, 1e-8), .75 / max(peak, 1e-8))
            samples = [x * gain for x in samples]
            target = output / (name + '.wav')
            write_wav(target, samples)
            if args.install and name in ('Hurt_A', 'Land_A'):
                runtime = ROOT / 'Project-Rule/Assets/_Project/Audio/SFX/PlayerFeedback' / ('PlayerHurt.wav' if name == 'Hurt_A' else 'PlayerLand.wav')
                shutil.copyfile(target, runtime)
            audition = [0.] * round(.2 * RATE)
            for _ in range(3):
                audition.extend(x * event_volume * .85 for x in samples)
                audition.extend([0.] * round(.55 * RATE))
            preview = output / (name + '_Audition.wav')
            write_wav(preview, audition)
            entries.append({
                'candidate': name,
                **({'runtime_file': 'Project-Rule/Assets/_Project/Audio/SFX/PlayerFeedback/' + ('PlayerHurt.wav' if name == 'Hurt_A' else 'PlayerLand.wav')} if name in ('Hurt_A', 'Land_A') else {}),
                'original': str(original.relative_to(ROOT)),
                'original_sha256': hashlib.sha256(raw).hexdigest(), 'processing': processing,
                'normalizing_gain': gain, 'proposed_event_volume': event_volume,
                'preview_master': .85, 'file': str(target.relative_to(ROOT)),
                'sha256': hashlib.sha256(target.read_bytes()).hexdigest(),
                'audition': str(preview.relative_to(ROOT)),
                'audition_sha256': hashlib.sha256(preview.read_bytes()).hexdigest(),
                'duration': len(samples) / RATE,
                'rms': math.sqrt(sum(x*x for x in samples) / len(samples)),
                'peak': max(abs(x) for x in samples),
            })
    manifest = {'author': 'Kenney', 'license': 'CC0-1.0',
        'source_page': 'https://kenney.nl/assets/impact-sounds',
        'purpose': 'Prepared player-feedback candidates; the preparation script does not modify game configuration.', 'files': entries}
    (originals.parent / 'PlayerFeedbackManifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    (output / 'README.md').write_text('''# 玩家反馈试听候选

当前采用 Hurt_A/ Land_A 用于玩家受击与落地，B 保留对照；准备脚本本身不修改游戏配置。来源为 Kenney Impact Sounds，CC0；原件在 Doc/AudioSources/FeedbackOriginals，处理与哈希在 PlayerFeedbackManifest.json。

| 候选 | 方向 | 原素材 |
| --- | --- | --- |
| Hurt_A | 保留清脆高频的短击 | impactPunch_heavy_003 |
| Hurt_B | 带高频细节的软撞击 | impactSoft_medium_003 |
| Land_A | 清楚的鞋底接触瞬态 | footstep_concrete_002 |
| Land_B | 较柔和的脚步对照 | footstep_carpet_000 |

不带叫声、呻吟或木板敲击。撤销旧版2.2–2.6kHz低通，3.5kHz以上高架提升5–6dB，仅在14kHz处保留带宽保护；削减260Hz低中频，保留起音和短淡出。目标是用户要求的清亮高频；实际听感仍需在游戏中确认。

无后缀 WAV 为可接入素材；_Audition.wav 每段重复三次，已乘拟定事件音量（受击0.60/落地0.33）与主音量0.85。接入时使用无后缀版本，避免重复乘音量。两种受击、两种落地分别匹配近似RMS并保留峰值余量。

复现：python3 Tools/Audio/prepare_player_feedback.py /path/to/kenney_impact-sounds.zip，需要 ffmpeg；加 --install 可更新两个现有 Unity 播放文件，保留其 .meta。
''')
    print(json.dumps([{'candidate': e['candidate'], 'duration': e['duration'], 'rms': e['rms'], 'peak': e['peak']} for e in entries], ensure_ascii=False))


if __name__ == '__main__':
    main()
