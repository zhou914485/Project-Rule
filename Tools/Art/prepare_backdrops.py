#!/usr/bin/env python3
"""把本地交付的背景图整理成 Unity 用的贴图（重制版 12 张）。

源：本地文档/背景（重制完成/...
出：Project-Rule/Assets/_Project/Art/Sprites/Backdrops/Painted/

全部 16:9，输出 2048x1152（PPU 128 = 16x9 世界）。
两张本来就是 2048x1152 的（钟表 Boss、宫殿）原样复制，不放大。
"""
import json
import os
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SOURCE = os.path.join(ROOT, "\u672c\u5730\u6587\u6863", "\u80cc\u666f\uff08\u91cd\u5236\u5b8c\u6210")
ASSETS = os.path.join(ROOT, "Project-Rule", "Assets", "_Project", "Art", "Sprites", "Backdrops", "Painted")

SIZE = (2048, 1152)

JOBS = [
    ("\u51fa\u751f\u70b9/\u4e1b\u6797.png", "Start_Backdrop.png", "\u51fa\u751f\u70b9\u00b7\u5916\u666f\uff08\u9ed8\u8ba4\uff09"),
    ("\u51fa\u751f\u70b9/\u6811\u6797.png", "StartGrove_Backdrop.png", "\u51fa\u751f\u70b9\u00b7\u5916\u666f\u53d8\u4f53"),
    ("\u51fa\u751f\u70b9/\u5192\u9669\u5bb6\u534f\u4f1a.png", "GuildHall_Backdrop.png", "\u51fa\u751f\u70b9\u00b7\u534f\u4f1a\u5185\u5385"),
    ("\u8f66\u7ad9/\u8f66\u7ad9.png", "Station_Backdrop.png", "\u8f66\u7ad9\u00b7\u5916\u666f"),
    ("\u79ef\u6728\u738b\u56fd/\u79ef\u6728\u5916\u666f.png", "Brick_Backdrop.png", "\u79ef\u6728\u738b\u56fd\u00b7\u5916\u666f"),
    ("\u79ef\u6728\u738b\u56fd/\u79ef\u6728\u5ba4\u5185\u80cc\u666f.png", "BrickBoss_Backdrop.png", "\u79ef\u6728\u738b\u56fd\u00b7Boss"),
    ("\u53d1\u6761\u738b\u56fd/\u9f7f\u8f6e\u5916\u666f.png", "Clockwork_Backdrop.png", "\u53d1\u6761\u738b\u56fd\u00b7\u5916\u666f"),
    ("\u53d1\u6761\u738b\u56fd/\u949f\u8868boss_20261021_035223.png", "ClockworkBoss_Backdrop.png", "\u53d1\u6761\u738b\u56fd\u00b7Boss"),
    ("\u6bdb\u7ed2\u738b\u56fd/\u73a9\u5177\u5806.png", "Plush_Backdrop.png", "\u6bdb\u7ed2\u738b\u56fd\u00b7\u5916\u666f"),
    ("\u6bdb\u7ed2\u738b\u56fd/\u821e\u53f0.png", "PlushBoss_Backdrop.png", "\u6bdb\u7ed2\u738b\u56fd\u00b7Boss"),
    ("\u9b54\u738b\u57ce/\u5916\u666f.png", "Demon_Backdrop.png", "\u9b54\u738b\u57ce\u00b7\u5916\u666f"),
    ("\u9b54\u738b\u57ce/\u5bab\u6bbf.png", "DemonHall_Backdrop.png", "\u9b54\u738b\u57ce\u00b7\u738b\u5ea7\u5385"),
]

MANIFEST = os.path.join(ROOT, "Tools", "Art", "BackdropManifest.json")


def main():
    os.makedirs(ASSETS, exist_ok=True)
    entries = []

    for relative, output, note in JOBS:
        source = os.path.join(SOURCE, relative)
        if not os.path.isfile(source):
            raise SystemExit("missing source: " + source)

        target = os.path.join(ASSETS, output)
        with Image.open(source) as raw:
            image = raw.convert("RGB")
            width, height = image.size
            if abs(width / height - 16 / 9) > 0.01:
                raise SystemExit("not 16:9: " + source)
            if (width, height) != SIZE:
                image = image.resize(SIZE, Image.LANCZOS)
            image.save(target, optimize=True)

        entries.append({
            "source": os.path.relpath(source, ROOT).replace("\\", "/"),
            "output": os.path.relpath(target, ROOT).replace("\\", "/"),
            "sourceSize": [width, height],
            "outputSize": list(SIZE),
            "note": note,
        })
        print("%-28s %s -> %s" % (note, (width, height), SIZE[0]))

    with open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump({"generatedBy": "Tools/Art/prepare_backdrops.py", "items": entries},
                  handle, ensure_ascii=False, indent=2)
    print("manifest ->", os.path.relpath(MANIFEST, ROOT))


if __name__ == "__main__":
    main()
