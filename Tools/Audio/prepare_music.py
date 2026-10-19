#!/usr/bin/env python3
"""Preserve delivered OGGs and make lossless PCM playback assets without export silence.

Requires ffmpeg. Never trims the start or normalizes gain. The last sample above
-60 dBFS (either channel), followed by 20 ms safety padding, defines the endpoint.
"""
from pathlib import Path
import argparse
import array
import hashlib
import json
import shutil
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
AREAS = {"积木区": "Bricks", "发条区": "Clockwork", "毛绒区": "Fluffy"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()
    archive = ROOT / "Doc/AudioSources/MusicOriginals"
    playback = ROOT / "Project-Rule/Assets/_Project/Audio/Music"
    records = []
    for folder, area in AREAS.items():
        for source in sorted((args.source / folder).glob("*.ogg")):
            raw = subprocess.check_output([
                "ffmpeg", "-v", "error", "-i", str(source),
                "-f", "s16le", "-acodec", "pcm_s16le", "-ac", "2", "-ar", "44100", "-",
            ])
            samples = array.array("h")
            samples.frombytes(raw)
            if sys.byteorder != "little":
                samples.byteswap()
            frames = len(samples) // 2
            last = frames - 1
            threshold = 32768 * 10 ** (-60 / 20)
            while last > 0 and max(abs(samples[last * 2]), abs(samples[last * 2 + 1])) < threshold:
                last -= 1
            end = min(frames, last + 1 + round(0.02 * 44100))
            target = playback / area / (source.stem + ".wav")
            target.parent.mkdir(parents=True, exist_ok=True)
            original = archive / area / source.name
            original.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, original)
            with wave.open(str(target), "wb") as output:
                output.setnchannels(2)
                output.setsampwidth(2)
                output.setframerate(44100)
                output.writeframes(raw[:end * 4])
            # Preserve any metadata created during the initial OGG import.
            previous = target.with_suffix(".ogg")
            old_meta = Path(str(previous) + ".meta")
            new_meta = Path(str(target) + ".meta")
            if old_meta.exists() and not new_meta.exists():
                old_meta.rename(new_meta)
            if previous.exists():
                previous.unlink()
            record = {
                "area": area,
                "original": str(original.relative_to(ROOT)),
                "sha256": hashlib.sha256(original.read_bytes()).hexdigest(),
                "playback": str(target.relative_to(ROOT)),
                "original_frames": frames,
                "playback_frames": end,
                "sample_rate": 44100,
                "trimmed_seconds": round((frames - end) / 44100, 6),
                "playback_seconds": round(end / 44100, 6),
            }
            records.append(record)
            print(f"{source.name}: {frames / 44100:.3f}s -> {end / 44100:.3f}s")
    if len(records) != 5:
        raise RuntimeError(f"Expected five delivered music files, found {len(records)}")
    (archive.parent / "MusicManifest.json").write_text(json.dumps({
        "processing": "PCM16 stereo 44100 Hz; remove only trailing samples below -60 dBFS with 20 ms padding; no gain or start changes",
        "files": records,
    }, ensure_ascii=False, indent=2) + "\n")


if __name__ == "__main__":
    main()
