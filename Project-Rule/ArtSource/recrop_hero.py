#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Hero 精灵批处理：等比缩一半 + 裁掉左右透明空白 + 重算轴心

用法:
    python recrop_hero.py <新素材根目录> [--scale 0.5] [--dry-run]

目录约定:
    <根目录>/<序列名>/*.png        一个子目录 = 一个动作序列

每张图会:
    1) 按序列求**并集**的左右边界（该序列所有帧 alpha 的 x 范围并起来）
       ⚠️ 绝不逐帧裁 —— 逐帧裁会把「挥剑前冲 / 翻跟斗」这些**故意的帧间位移**裁没
    2) 所有帧裁到**同一个**矩形（只裁左右，上下原样保留）
    3) 等比缩 <scale>（宽高同乘，默认 0.5）
    4) 轴心 = (alpha 质心 x, 底边)，写进同目录的 .png.meta

先跑 --dry-run 看一遍每序列的裁切量和缩放结果，确认没问题再去掉它真跑。
原图会被**就地覆盖**，所以: 先 git 提交 / 先备份。
"""

import argparse
import glob
import os
import re
import struct
import sys

from PIL import Image


def natural_key(path):
    """Frame_2 要排在 Frame_10 前面。"""
    return [int(t) if t.isdigit() else t.lower()
            for t in re.split(r"(\d+)", os.path.basename(path))]


def alpha_bbox_and_centroid(im):
    """返回 (左, 上, 右, 下) 与 alpha 质心的 x（原始像素坐标）。"""
    a = im.getchannel("A")
    bb = a.getbbox()
    if bb is None:
        return None, 0.0
    x0, y0, x1, y1 = bb
    # 质心：按 alpha 加权，逐行取一次以减少 Python 层循环
    sx = sw = 0.0
    px = a.load()
    for y in range(y0, y1):
        row = 0.0
        for x in range(x0, x1):
            v = px[x, y]
            if v:
                row += v
                sx += x * v
        sw += row
    cx = (sx / sw) if sw else (x0 + x1) / 2.0
    return (x0, y0, x1, y1), cx


def out_paths(out_root, seq, count):
    """目标路径：<out>/<序列>/Hero_<序列>_NN.png —— 和现有素材同目录同命名，
    这样能**原地覆盖**并保留 .meta（GUID 不变 → 现有 .anim 剪辑的引用不会断）。"""
    d = os.path.join(out_root, seq)
    os.makedirs(d, exist_ok=True)
    return [os.path.join(d, f"Hero_{seq}_{i+1:02d}.png") for i in range(count)]


def process_sequence(seq_dir, scale, dry_run, center, foot_y, align_foot, align_mode, out_root, airborne):
    files = sorted(glob.glob(os.path.join(seq_dir, "*.png")), key=natural_key)
    if not files:
        return None
    targets = out_paths(out_root, os.path.basename(seq_dir), len(files)) if out_root else files

    # ---- 1) 第一趟：求并集 bbox（x / y 都求）+ 各帧脚底 ----
    left, top, right, bottom = 10 ** 9, 10 ** 9, -1, -1
    src_w = src_h = 0
    bottoms = []
    for f in files:
        im = Image.open(f).convert("RGBA")
        bb, _ = alpha_bbox_and_centroid(im)
        if bb is None:
            continue
        left, top     = min(left, bb[0]), min(top, bb[1])
        right, bottom = max(right, bb[2]), max(bottom, bb[3])
        src_w, src_h = max(src_w, im.size[0]), max(src_h, im.size[1])
        bottoms.append(bb[3])
    if right < 0:
        print(f"  !! {os.path.basename(seq_dir)}: 全是空图，跳过")
        return None

    shift = 0
    if airborne:
        # ---- 滞空序列：裁到并集，四周补**等宽**透明边，人物居中 ----
        #   这类序列角色本来就在画布各处飞，按"脚底线"对齐没有意义。
        #   规则（按团队要求）：裁到并集 → 四周加同样宽的边 → 人物居中 → 轴心 = 图像中心。
        pad = int(round(max(right - left, bottom - top) * 0.08))
        cx0, cy0, cx1, cy1 = left - pad, top - pad, right + pad, bottom + pad
        foot = None                        # 用中心
    else:
        # ---- 接地序列：按本序列自己的**脚底线**整体平移，让所有序列的脚落在同一条线上 ----
        #   脚底线怎么取（--align-mode）:
        #     max    = 各帧 bbox 底边的最大值（角色最低那一帧 = 踩到地的那一帧）← 默认
        #     median = 中位数（只适合全程踩地的序列）
        #     first  = 第一帧
        foot = None
        if align_foot is not None:
            if align_mode == "max":     foot = max(bottoms)
            elif align_mode == "first": foot = bottoms[0]
            else:
                srt = sorted(bottoms)
                foot = srt[len(srt) // 2]
        elif foot_y is not None:
            foot = foot_y
        if align_foot is not None and foot is not None:
            shift = int(round(align_foot - foot))     # 正 = 往下移
        cx0, cx1 = left, right
        cy0, cy1 = 0, src_h
        if shift > 0:      cy1 = src_h + shift        # 往下移 → 底下要加空间
        elif shift < 0:    cy0 = shift                # 往上移 → 顶上要加空间

    canvas_w = cx1 - cx0
    win_h    = cy1 - cy0

    # ---- 2b) --center: 再补左右透明边，让**质心**落在图幅正中（可选，滞空序列已居中，跳过）----
    pad_left = 0
    if center and not airborne:
        cx_sum = w_sum = 0.0
        for f in files:
            im = Image.open(f).convert("RGBA").crop((cx0, cy0, cx1, cy1))
            px = im.getchannel("A").load()
            for y in range(im.size[1]):
                for x in range(im.size[0]):
                    v = px[x, y]
                    if v:
                        cx_sum += x * v
                        w_sum += v
        c0 = (cx_sum / w_sum) if w_sum else canvas_w / 2.0
        new_cw = int(round(2 * max(c0, canvas_w - c0)))
        pad_left = int(round(new_cw / 2.0 - c0))
        canvas_w = new_cw

    # ---- 3) 裁 + 补 + 等比缩放；质心在**同一趟里**从缩放后的图算出来 ----
    #         （不能事后再读一遍文件 —— dry-run 下文件没被改，会算到原图坐标上去）
    new_w = max(1, round(canvas_w * scale))
    out_h = max(1, round(win_h * scale))
    cx_sum = w_sum = 0.0
    for idx, f in enumerate(files):
        im = Image.open(f).convert("RGBA").crop((cx0, cy0, cx1, cy1))
        canvas = Image.new("RGBA", (canvas_w, win_h), (0, 0, 0, 0))
        canvas.alpha_composite(im, (pad_left, 0))
        im = canvas.resize((new_w, out_h), Image.LANCZOS)

        px = im.getchannel("A").load()
        for y in range(im.size[1]):
            for x in range(im.size[0]):
                v = px[x, y]
                if v:
                    cx_sum += x * v
                    w_sum += v

        if not dry_run:
            im.save(targets[idx], optimize=True)

    # ---- 4) 轴心（Unity 的 spritePivot 从**左下角**算起）----
    if airborne:
        nx = ny = 0.5                 # 人物已居中 → 直接用图像中心
    else:
        nx = (cx_sum / w_sum) / new_w if w_sum else 0.5
        ny = ((cy1 - foot) / win_h) if foot is not None else 0.0

    if not dry_run:
        write_meta_pivot(targets, nx, ny)

    return dict(seq=os.path.basename(seq_dir), frames=len(files),
                cropped=canvas_w, canvas=canvas_w, pad=pad_left,
                out=(new_w, out_h), nx=nx, ny=ny,
                foot=foot, shift=shift)


def write_meta_pivot(files, nx, ny):
    """.meta 已存在就改 spritePivot；不存在就跳过（PPU/压缩等交给 Unity 侧再刷）。"""
    pat = re.compile(r"spritePivot: \{x: [0-9.]+, y: [0-9.]+\}")
    for f in files:
        m = f + ".meta"
        if not os.path.exists(m):
            continue
        s = open(m, encoding="utf-8").read()
        s2 = pat.sub(f"spritePivot: {{x: {nx:.4f}, y: {ny:.4f}}}", s)
        if s2 != s:
            open(m, "w", encoding="utf-8", newline="").write(s2)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("root", help="新素材根目录（下面一个子目录一个序列）")
    ap.add_argument("--scale", type=float, default=0.5, help="等比缩放，默认 0.5")
    ap.add_argument("--center", action="store_true",
                    help="补透明边，让人物质心落在图幅正中（轴心可固定 0.5；画布会变宽）")
    ap.add_argument("--foot-y", type=float, default=None,
                    help="脚底线在**原图**里的 y（0=顶部）。给了就全序列共用这个 pivot.y；"
                         "不给 = 每个序列各自用自己 bbox 的底边")
    ap.add_argument("--align-foot", type=float, default=None,
                    help="**实测各序列脚底不一致时用这个**：把每个序列按自己的脚底线（各帧 bbox "
                         "底边的中位数）整体平移，让脚落在输出画布的 y=<值>。会按需上下补透明。")
    ap.add_argument("--align-mode", choices=("max", "median", "first"), default="max",
                    help="脚底线怎么取：max=最低那帧(默认，推荐) / median=中位数 / first=第一帧")
    ap.add_argument("--airborne", default="",
                    help="滞空序列名（逗号分隔）：这些改成「并集裁 + 四周等边距 + 人物居中」，轴心 = (0.5,0.5)")
    ap.add_argument("--out", default=None,
                    help="输出根目录；给了就写 <out>/<序列>/Hero_<序列>_NN.png（原地覆盖旧素材并保留 .meta），"
                         "不给就覆盖源目录里的原文件")
    ap.add_argument("--skip", default="", help="跳过的目录名，逗号分隔（如 Hero_Portrait,Hero_Attack）")
    ap.add_argument("--dry-run", action="store_true", help="只报告，不写文件")
    args = ap.parse_args()

    skip = {s.strip() for s in args.skip.split(",") if s.strip()}
    airborne = {s.strip() for s in args.airborne.split(",") if s.strip()}
    seqs = [d for d in sorted(os.listdir(args.root))
            if os.path.isdir(os.path.join(args.root, d)) and d not in skip]
    if not seqs:
        print("根目录下没有可分序列的子目录"); sys.exit(1)

    print(f"根目录: {args.root}   序列数: {len(seqs)}   缩放: {args.scale}x"
          f"   居中: {'是（补边）' if args.center else '否（轴心=质心）'}"
          f"   对齐脚底: {args.align_foot if args.align_foot is not None else '否'}"
          f"{'   [DRY RUN — 不写文件]' if args.dry_run else ''}")
    print(f"{'序列':20s} {'帧':>3s} {'裁后宽':>7s} {'脚底y':>7s} {'平移':>6s} "
          f"{'输出尺寸':>10s} {'pivot.x':>8s} {'pivot.y':>8s}")
    for s in seqs:
        r = process_sequence(os.path.join(args.root, s), args.scale, args.dry_run,
                             args.center, args.foot_y, args.align_foot, args.align_mode,
                             args.out, os.path.basename(s) in airborne)
        if not r:
            continue
        foot = f"{r['foot']:.0f}" if r["foot"] is not None else "-"
        print(f"{r['seq']:20s} {r['frames']:3d} {r['cropped']:7d} {foot:>7s} "
              f"{r['shift']:6d} {r['out'][0]:5d}x{r['out'][1]:<4d} {r['nx']:8.3f} {r['ny']:8.4f}")
    print("\n完成。" + ("（dry run，没动任何文件）" if args.dry_run else ""))


if __name__ == "__main__":
    main()
