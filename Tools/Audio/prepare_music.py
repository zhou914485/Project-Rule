#!/usr/bin/env python3
"""Preserve delivered OGGs and make lossless PCM playback assets without export silence.

Requires ffmpeg (set FFMPEG when it is not on PATH). Never trims the start or
normalizes gain. The last sample above -60 dBFS (either channel), followed by
20 ms safety padding, defines the endpoint.

Directory layout of the delivery::

    积木区/ 发条区/ 毛绒区/ 魔王城/ 新手村/     *.ogg
    bgm_theme_musicbox.ogg                     (optional, goes to Theme/)

Every *.ogg in an area folder becomes one playback WAV under
Project-Rule/Assets/_Project/Audio/Music/<Area>/. The theme file becomes
.../Music/Theme/bgm_theme_musicbox.wav.
"""
from pathlib import Path
import argparse
import array
import hashlib
import json
import os
import shutil
import subprocess
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
AREAS = {
    "积木区": "Bricks",
    "发条区": "Clockwork",
    "毛绒区": "Fluffy",
    "魔王城": "Dungeon",
    "新手村": "Village",
}
THEME_FILE = "bgm_theme_musicbox.ogg"
THEME_AREA = "Theme"
PLAYBACK = ROOT / "Project-Rule/Assets/_Project/Audio/Music"
ARCHIVE = ROOT / "Doc/AudioSources/MusicOriginals"
FFMPEG = os.environ.get("FFMPEG", "ffmpeg")


def role_of(stem: str) -> str:
    if stem.endswith("_intro"):
        return "intro"
    if stem.endswith("_loop"):
        return "loop"
    return "single"


def process(source: Path, area: str) -> dict:
    # 不用管道：本机的进程沙箱会拒绝 CreatePipe，ffmpeg 先写到临时文件再读。
    raw_path = source.with_suffix(source.suffix + ".pcm.tmp")
    try:
        subprocess.run([
            FFMPEG, "-v", "error", "-y", "-i", str(source),
            "-f", "s16le", "-acodec", "pcm_s16le", "-ac", "2", "-ar", "44100",
            str(raw_path),
        ], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        raw = raw_path.read_bytes()
    finally:
        if raw_path.exists():
            raw_path.unlink()

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

    target = PLAYBACK / area / (source.stem + ".wav")
    target.parent.mkdir(parents=True, exist_ok=True)
    original = ARCHIVE / area / source.name
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
        "role": role_of(source.stem),
        "original": original.relative_to(ROOT).as_posix(),
        "sha256": hashlib.sha256(original.read_bytes()).hexdigest(),
        "playback": target.relative_to(ROOT).as_posix(),
        "original_frames": frames,
        "playback_frames": end,
        "sample_rate": 44100,
        "trimmed_seconds": round((frames - end) / 44100, 6),
        "playback_seconds": round(end / 44100, 6),
    }
    print(f"{area}/{source.name}: {frames / 44100:.3f}s -> {end / 44100:.3f}s")
    return record


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", type=Path)
    args = parser.parse_args()

    records = []
    for folder, area in AREAS.items():
        directory = args.source / folder
        if not directory.is_dir():
            continue
        for source in sorted(directory.glob("*.ogg")):
            records.append(process(source, area))

    theme = args.source / THEME_FILE
    if theme.exists():
        records.append(process(theme, THEME_AREA))

    if not records:
        raise RuntimeError(f"No OGG found under {args.source}")

    records.sort(key=lambda item: (item["area"], item["original"]))
    (ARCHIVE.parent / "MusicManifest.json").write_text(json.dumps({
        "processing": "PCM16 stereo 44100 Hz; remove only trailing samples below -60 dBFS with 20 ms padding; no gain or start changes",
        "files": records,
    }, ensure_ascii=False, indent=2) + "\n")
    print(f"wrote {len(records)} records")


if __name__ == "__main__":
    main()
