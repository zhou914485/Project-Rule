#!/usr/bin/env python3
"""Export text-free, sub-1 MB GIFs from RuleCastVfxPreview's actual URP frames.

Requires Pillow. Run the Unity recorder in a disposable project first, then pass
its Logs/RuleVfxPreview directory. Only resize, resample and quantize captured
pixels; never generate effect pixels, recolor them, or add text/overlays.
"""
import argparse
import bisect
import hashlib
import json
from pathlib import Path
from PIL import Image

NAMES = ("Player", "Bat", "Clock", "Rabbit", "Brick")
TIMES = tuple(i / 10 for i in range(38))
LIMIT = 1_000_000


def encode(frames, path):
    durations = [100] * len(frames)
    durations[0] += 300
    durations[-1] += 500
    for _ in range(4):
        for colors in (256, 128, 96, 64):
            w, h = frames[0].size
            samples = Image.new("RGB", (w, h * 10))
            for i in range(10):
                samples.paste(frames[round(i * (len(frames)-1) / 9)], (0, h*i))
            palette = samples.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)
            indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
            indexed[0].save(path, save_all=True, append_images=indexed[1:], duration=durations,
                            loop=0, optimize=False, disposal=2)
            if path.stat().st_size < LIMIT:
                with Image.open(path) as image:
                    result = {"size": list(image.size), "gif_frames": image.n_frames,
                              "duration_ms": sum(durations), "bytes": path.stat().st_size,
                              "colors": colors, "text_overlays": False,
                              "sha256": hashlib.sha256(path.read_bytes()).hexdigest()}
                print(f"{path}: {result['bytes']} bytes, {colors} colors, no text")
                return result
        frames = [f.resize((round(f.width*.9), round(f.height*.9)), Image.Resampling.LANCZOS) for f in frames]
    raise ValueError(f"Could not export {path} below {LIMIT} bytes")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("captures", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[2] / "Doc/特效预览/RuleSacredWave")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    clips, report = {}, {}
    for name in NAMES:
        data = json.loads((args.captures/name/"frames.json").read_text())
        records = data["frames"]
        if not any(f["phase"] == "Active" and f["visible"] == 0 for f in records):
            raise ValueError(f"{name}: missing fully offscreen footage")
        if not any(f["phase"] == "Return" and f["visible"] > 0 for f in records):
            raise ValueError(f"{name}: missing real return footage")
        times = [f["time"] for f in records]
        clips[name] = []
        for time in TIMES:
            record = records[max(0, bisect.bisect_right(times, time)-1)]
            with Image.open(args.captures/name/record["file"]) as image:
                clips[name].append(image.convert("RGB").resize((480,270), Image.Resampling.LANCZOS))
        report[name] = encode(clips[name], args.output/f"RuleSacredWave_{name}.gif")
        report[name]["captured_frames"] = len(records)
    overview = []
    for index in range(len(TIMES)):
        image = Image.new("RGB", (720,270), (7,9,17))
        for i, name in enumerate(NAMES):
            x, y = (i*240, 0) if i < 3 else (120+(i-3)*240, 135)
            image.paste(clips[name][index].resize((240,135), Image.Resampling.LANCZOS), (x,y))
        overview.append(image)
    path = args.output/"RuleSacredWave_Overview_Small.gif"
    report["Overview_Small"] = encode(overview, path)
    # Preserve the existing full-overview link while keeping both links text-free and below 1 MB.
    (args.output/"RuleSacredWave_Overview.gif").write_bytes(path.read_bytes())
    report["Overview"] = dict(report["Overview_Small"])
    (args.output/"PreviewManifest.json").write_text(json.dumps(report, ensure_ascii=False, indent=2)+"\n")


if __name__ == "__main__":
    main()
