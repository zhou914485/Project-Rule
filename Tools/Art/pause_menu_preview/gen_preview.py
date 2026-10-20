# -*- coding: utf-8 -*-
"""生成「ESC 暂停菜单」设计预览图（纯 PIL 渲染，不依赖浏览器）。

产物：
    Doc/UI预览/暂停菜单_预览.png       1920x1080 主预览
    Doc/UI预览/暂停菜单_状态对照.png    2560x1600 四种状态对照

底图用工程真实精灵（积木城渐变天空 / 主角动画帧 / 秋千存档点）拼一张
「冻结中的游戏画面」，UI 层按 1920x1080 画板绘制 —— 与 UXML/USS 的
1920x1080 artboard + 等比缩放方案对齐。
"""
import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
ART = os.path.join(ROOT, "Project-Rule", "Assets", "_Project", "Art", "Sprites")
FONT_PATH = os.path.join(ROOT, "Project-Rule", "Assets", "_Project", "UI", "Fonts", "NotoSansCJKsc-Regular.otf")
OUT_DIR = os.path.join(ROOT, "Doc", "UI预览")
BUILD = os.path.join(os.path.dirname(__file__), "_build")
os.makedirs(BUILD, exist_ok=True)
os.makedirs(OUT_DIR, exist_ok=True)

W, H = 1920, 1080
GROUND_Y = 880
SS = 2  # 超采样倍数：先 2 倍画，再缩回，边缘更顺

# ---------------------------------------------------------------- 调色板
PAPER = (248, 240, 218)
BUTTON = (253, 246, 230)
BUTTON_FOCUS = (255, 247, 223)
EDGE = (107, 74, 42)
BTN_EDGE = (179, 148, 99)
INK = (58, 42, 20)
INK_SOFT = (109, 92, 64)
MUTED = (151, 134, 106)
GOLD = (217, 166, 63)
RED = (180, 70, 47)
NAVY = (35, 48, 63)
RED_BTN = (168, 67, 44)
HUD_INK = (33, 33, 31)
HUD_MUTED = (111, 111, 102)

_font_cache = {}


def font(size):
    key = int(size * SS)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(FONT_PATH, key)
    return _font_cache[key]


def _stroke(size):
    if size * SS >= 46:
        return 2
    if size * SS >= 30:
        return 1
    if size * SS >= 22:
        return 1
    return 0


class Canvas:
    """2 倍超采样的画布，坐标一律写 1920x1080 逻辑像素。"""

    def __init__(self, w=W, h=H, bg=(0, 0, 0, 255)):
        self.w, self.h = w, h
        self.img = Image.new("RGBA", (w * SS, h * SS), bg)
        self.d = ImageDraw.Draw(self.img)

    def paste(self, im, xy):
        x, y = xy
        im = im.convert("RGBA").resize((int(im.width * SS), int(im.height * SS)), Image.LANCZOS)
        self.img.alpha_composite(im, (int(x * SS), int(y * SS)))

    def rect(self, box, fill=None, outline=None, width=1, radius=0):
        b = [int(v * SS) for v in box]
        if radius > 0:
            self.d.rounded_rectangle(b, radius=int(radius * SS), fill=fill,
                                     outline=outline, width=max(1, int(width * SS)))
        else:
            self.d.rectangle(b, fill=fill, outline=outline, width=max(1, int(width * SS)))

    def line(self, pts, fill, width=1):
        self.d.line([(int(x * SS), int(y * SS)) for x, y in pts],
                    fill=fill, width=max(1, int(width * SS)))

    def text(self, xy, s, size, color, bold=False, ls=0.0, anchor="la"):
        """anchor: 水平 l/r/m + 垂直 a(顶)/m(垂直居中)。y 一律是行盒顶边。"""
        f = font(size)
        ascent, descent = f.getmetrics()
        x, y = xy
        stroke = _stroke(size) if bold else 0
        halign, valign = anchor[0], anchor[1]
        if valign == "m":
            baseline = y * SS + 0.38 * size * SS
        else:
            baseline = y * SS + 0.88 * size * SS

        if ls == 0:
            self.d.text((x * SS, baseline), s, font=f, fill=color, anchor=halign + "s",
                        stroke_width=stroke, stroke_fill=color)
            return self.measure(s, size, ls)

        # 带字距：逐字推进（自己算右/居中对齐的起点）
        total = self.measure(s, size, ls)
        if halign == "r":
            cx = x * SS - total * SS
        elif halign == "m":
            cx = x * SS - total * SS / 2
        else:
            cx = x * SS
        for ch in s:
            self.d.text((cx, baseline), ch, font=f, fill=color, anchor="ls",
                        stroke_width=stroke, stroke_fill=color)
            cx += f.getlength(ch) + ls * SS
        return total

    def measure(self, s, size, ls=0.0):
        f = font(size)
        if ls == 0:
            return f.getlength(s) / SS
        return (sum(f.getlength(c) for c in s) + ls * SS * max(0, len(s) - 1)) / SS

    def wrap(self, s, size, max_w):
        f = font(size)
        lines, cur = [], ""
        for ch in s:
            if f.getlength(cur + ch) / SS > max_w:
                lines.append(cur)
                cur = ch
            else:
                cur += ch
        if cur:
            lines.append(cur)
        return lines

    def save(self, path):
        out = self.img.convert("RGB").resize((self.w, self.h), Image.LANCZOS)
        out.save(path)
        return path


def build_backdrop(path):
    sky = Image.open(os.path.join(ART, "Backdrops", "Gradient", "Brick_Backdrop.png")).convert("RGB")
    sky = sky.resize((W, H), Image.LANCZOS)
    sky = Image.blend(sky, Image.new("RGB", (W, H), (255, 250, 238)), 0.18)
    img = sky.convert("RGBA")
    d = ImageDraw.Draw(img, "RGBA")

    far = [(60, 150, 96), (170, 120, 210), (300, 90, 140), (430, 140, 300), (590, 110, 180),
           (720, 130, 250), (870, 100, 160), (990, 150, 230), (1160, 120, 190), (1300, 140, 300),
           (1470, 100, 150), (1590, 130, 240), (1740, 110, 180), (1850, 70, 120)]
    for x, w, h in far:
        d.rectangle([x, GROUND_Y - h, x + w, GROUND_Y], fill=(44, 62, 78, 26))
        d.rectangle([x, GROUND_Y - h, x + w, GROUND_Y - h + 6], fill=(44, 62, 78, 40))

    near = [(-30, 140, 120), (140, 180, 210), (380, 150, 96), (620, 200, 270), (900, 150, 150),
            (1120, 170, 230), (1340, 190, 120), (1600, 160, 250), (1820, 140, 180)]
    for x, w, h in near:
        d.rectangle([x, GROUND_Y - h, x + w, GROUND_Y], fill=(36, 52, 66, 42))
        d.line([x, GROUND_Y - h, x + w, GROUND_Y - h], fill=(30, 44, 56, 92), width=3)

    ground = Image.new("RGBA", (W, H - GROUND_Y))
    gd = ImageDraw.Draw(ground)
    for y in range(ground.height):
        t = y / max(1, ground.height - 1)
        gd.line([(0, y), (W, y)], fill=(int(206 - 40 * t), int(190 - 42 * t), int(165 - 42 * t), 255))
    img.alpha_composite(ground, (0, GROUND_Y))
    d = ImageDraw.Draw(img, "RGBA")
    # 错缝砖墙：把地面读成「积木城」的石台，而不是一条空灰带
    row_h, brick_w = 62, 134
    row = 0
    y = GROUND_Y + 4
    while y < H:
        d.line([0, y, W, y], fill=(74, 58, 42, 34), width=2)
        off = -brick_w // 2 if row % 2 else 0
        for x in range(off, W + brick_w, brick_w):
            d.line([x, y, x, min(y + row_h, H)], fill=(74, 58, 42, 30), width=2)
        row += 1
        y += row_h
    d.line([0, GROUND_Y, W, GROUND_Y], fill=(58, 44, 30, 235), width=5)

    hero_sheet = Image.open(os.path.join(ART, "Hero", "animation", "air.png")).convert("RGBA")
    fw = hero_sheet.width // 6
    hero = hero_sheet.crop((fw * 2, 0, fw * 3, hero_sheet.height))
    hero = hero.crop(hero.split()[3].getbbox())
    scale = 330 / hero.height
    hero = hero.resize((max(1, int(hero.width * scale)), 330), Image.LANCZOS)

    shadow = Image.new("RGBA", (hero.width, 40), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).ellipse([0, 0, hero.width - 1, 39], fill=(24, 34, 44, 55))
    shadow = shadow.filter(ImageFilter.GaussianBlur(10))
    hx, hy = 300, GROUND_Y - 330 - 96
    img.alpha_composite(shadow, (hx, GROUND_Y - 26))
    img.alpha_composite(hero, (hx, hy))


    img.convert("RGB").save(path)
    return path


def composite_hud(c, alpha=0.62):
    """HUD 画在遮罩之上、整层半透明：世界被压暗，状态信息仍然读得出来。"""
    lay = Canvas(bg=(0, 0, 0, 0))
    draw_hud(lay)
    lay.img.putalpha(lay.img.split()[3].point(lambda v: int(v * alpha)))
    c.img.alpha_composite(lay.img, (0, 0))


def draw_hud(c):
    c.text((36, 28), "规则游戏", 22, HUD_INK, bold=True)
    for i, (name, ratio, num) in enumerate((("生命", 0.72, "72 / 100"), ("能量", 0.45, "45 / 100"))):
        y = 66 + i * 25
        c.text((36, y), name, 13, HUD_INK)
        c.rect((82, y + 6, 258, y + 14), fill=(207, 207, 196))
        c.rect((82, y + 6, 82 + 176 * ratio, y + 14), fill=HUD_INK)
        c.text((266, y), num, 13, HUD_INK)
    x2 = 1920 - 36
    w = c.text((x2, 32), "生效规则", 13, HUD_MUTED, anchor="ra")
    for i, (name, t) in enumerate((("重力 × 反转", "3.2 s"), ("摩擦 × 削弱", "6.8 s"))):
        y = 56 + i * 30
        c.text((x2, y), t, 13, HUD_MUTED, anchor="ra")
        c.text((x2 - 34, y), name, 16, HUD_INK, bold=True, anchor="ra")
    c.text((36, 1032), "齿轮塔 · 找到第二把钥匙", 19, HUD_INK, bold=True)


def draw_shade(c):
    w, h = W // 4, H // 4
    yy, xx = np.mgrid[0:h, 0:w]
    dx = (xx - w / 2) / (w / 2)
    dy = (yy - h / 2.2) / (h / 2)
    r = np.sqrt(dx ** 2 + dy ** 2)
    alpha = np.clip(0.52 + 0.26 * np.clip((r - 0.55) / 0.9, 0, 1), 0, 1) * 255
    rgba = np.zeros((h, w, 4), dtype=np.uint8)
    rgba[..., 0], rgba[..., 1], rgba[..., 2] = 11, 26, 40
    rgba[..., 3] = alpha.astype(np.uint8)
    veil = Image.fromarray(rgba, "RGBA").resize((W, H), Image.LANCZOS)
    c.paste(veil, (0, 0))


def panel_shadow(c, box):
    x0, y0, x1, y1 = box
    lay = Image.new("RGBA", (c.w * SS, c.h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(lay)
    d.rounded_rectangle([int(x0 * SS), int((y0 + 18) * SS), int(x1 * SS), int((y1 + 22) * SS)],
                        radius=int(10 * SS), fill=(8, 18, 26, 130))
    lay = lay.filter(ImageFilter.GaussianBlur(int(16 * SS / 2)))
    c.img.alpha_composite(lay)


def panel_frame(c, box):
    panel_shadow(c, box)
    c.rect(box, fill=PAPER, outline=EDGE, width=3, radius=8)
    c.rect((box[0] + 3, box[1] + 3, box[2] - 3, box[3] - 3), outline=(255, 250, 235, 120), width=1, radius=6)


def button(c, box, label, desc=None, key=None, state="idle", danger=False, primary=False, ghost=False):
    """state: idle / hover / focus / disabled"""
    x0, y0, x1, y1 = box
    if state == "focus":
        x0 += 0
    if state == "disabled":
        fill, edge, lc, dc = (241, 233, 215), (214, 200, 171), (168, 154, 128), (179, 164, 136)
    elif primary:
        fill = NAVY if not danger else RED_BTN
        edge = fill
        lc, dc = PAPER, (235, 225, 205)
    elif ghost:
        fill, edge, lc, dc = None, (196, 173, 137), INK_SOFT, MUTED
    elif state in ("hover", "focus"):
        fill, edge, lc, dc = BUTTON_FOCUS, GOLD if state == "focus" else (138, 106, 58), INK, INK_SOFT
    else:
        fill, edge, lc, dc = BUTTON, BTN_EDGE, (151, 56, 31) if danger else (67, 48, 26), INK_SOFT

    if state == "focus":
        c.rect((x0 - 14, y0 + 8, x0 - 8, y1 - 8), fill=GOLD, radius=3)

    c.rect(box, fill=fill, outline=edge, width=3 if state == "focus" else 2, radius=5)

    ty = (y0 + y1) / 2
    if desc:
        c.text((x0 + 24, ty - 20), label, 24, lc, bold=True, ls=2, anchor="lm")
        c.text((x0 + 24, ty + 18), desc, 13, dc, ls=0.4, anchor="lm")
    else:
        c.text((x0 + 24, ty), label, 24, lc, bold=True, ls=2, anchor="lm")

    if key:
        kw = c.measure(key, 12, 1) + 20
        cx0, cx1 = x1 - 22 - kw, x1 - 22
        c.rect((cx0, ty - 13, cx1, ty + 13), outline=(195, 172, 131), width=1, radius=3)
        c.text(((cx0 + cx1) / 2, ty), key, 12, MUTED, ls=1, anchor="mm")
    if state == "disabled":
        c.line([(x1 - 48, ty), (x1 - 22, ty)], (194, 178, 149), 2)


def hints(c, y, items):
    gap = 18
    widths = []
    for k, v in items:
        widths.append(c.measure(k, 13, 1) + 6 + c.measure(v, 13, 1))
    total = sum(widths) + gap * (len(items) - 1)
    x = (W - total) / 2
    for (k, v), wd in zip(items, widths):
        c.text((x, y), k, 13, (111, 92, 61), bold=True, ls=1)
        x += c.measure(k, 13, 1) + 6
        c.text((x, y), v, 13, MUTED, ls=1)
        x += c.measure(v, 13, 1) + gap


def screen_pause(state):
    c = Canvas()
    c.paste(Image.open(os.path.join(BUILD, "backdrop.png")), (0, 0))
    draw_shade(c)
    composite_hud(c)

    pw, pad = 660, 44
    x0 = (W - pw) / 2
    x1 = x0 + pw
    ix0, ix1 = x0 + pad, x1 - pad
    top = 279
    bottom = top + 522
    panel_frame(c, (x0, top, x1, bottom))

    y = top + 34
    c.text((ix0, y), "PAUSED", 12, (150, 126, 88), ls=3)
    c.text((ix1, y), "规则时钟已停", 12, (150, 126, 88), ls=6, anchor="ra")
    y += 24
    c.text((ix0, y - 2), "暂停", 54, INK, bold=True, ls=12)
    y += 62
    c.text((ix0, y), "世界已经静止。想清楚了再继续。", 17, (122, 106, 79), ls=1)
    y += 26 + 24
    c.line([(ix0, y), (ix1, y)], (217, 200, 164), 1)
    y += 22

    focus_continue = state in ("normal", "no-checkpoint")
    button(c, (ix0, y, ix1, y + 76), "继续", key="Esc", state="focus" if focus_continue else "idle")
    y += 90
    if state == "no-checkpoint":
        button(c, (ix0, y, ix1, y + 76), "回到上一个存档点",
               desc="还没有存档点 —— 打一下秋千存档点就会记录", state="disabled")
    else:
        button(c, (ix0, y, ix1, y + 76), "回到上一个存档点", desc="存档点 · 三号街区 · 第 2 关",
               state="focus" if state == "focus-checkpoint" else "idle")
    y += 90
    button(c, (ix0, y, ix1, y + 76), "回到主菜单", danger=True)
    y += 76 + 22
    hints(c, y, [("↑ ↓", "选择"), ("Enter", "确认"), ("Esc", "继续游戏")])
    return c


def screen_confirm(kind):
    """kind: checkpoint / menu"""
    c = Canvas()
    c.paste(Image.open(os.path.join(BUILD, "backdrop.png")), (0, 0))
    draw_hud(c)
    draw_shade(c)

    pw, pad = 660, 44
    x0 = (W - pw) / 2
    x1 = x0 + pw
    ix0, ix1 = x0 + pad, x1 - pad
    copy = ("会回到存档点当时的状态：血量、已拼出的规则、机关的开关都会还原。"
            if kind == "checkpoint" else
            "本局从存档点之后的进度会丢失。下次从主菜单「读取存档」，还会回到上一个存档点。")
    lines = c.wrap(copy, 16, ix1 - ix0)
    card_h = 96 if kind == "checkpoint" else 0
    h = 34 + 22 + 44 + 16 + len(lines) * 28 + (24 + card_h if kind == "checkpoint" else 0) + 24 + 68 + 18 + 20 + 30
    top = (H - h) / 2
    bottom = top + h
    panel_frame(c, (x0, top, x1, bottom))

    y = top + 34
    c.text((ix0, y), "CHECKPOINT" if kind == "checkpoint" else "MAIN MENU", 12, (160, 138, 99), ls=3)
    c.text((ix1, y), "不可撤销", 12, RED, ls=6, anchor="ra")
    y += 22
    c.text((ix0, y), "回到上一个存档点？" if kind == "checkpoint" else "回到主菜单？", 34, INK, bold=True, ls=2)
    y += 44 + 16
    for ln in lines:
        c.text((ix0, y), ln, 16, (109, 92, 64), ls=0.5)
        y += 28

    if kind == "checkpoint":
        y += 24
        c.rect((ix0, y, ix1, y + card_h), fill=(242, 230, 200), radius=3)
        c.rect((ix0, y, ix0 + 4, y + card_h), fill=GOLD)
        c.text((ix0 + 18, y + 14), "存档点", 12, MUTED, ls=2)
        c.text((ix0 + 18, y + 34), "三号街区 · 秋千", 19, (67, 48, 26), bold=True)
        c.text((ix0 + 18, y + 64), "第 2 关 · 4 分钟前", 13, (141, 122, 88))
        y += card_h

    y += 24
    bw = (ix1 - ix0 - 14) / 2
    button(c, (ix0, y, ix0 + bw, y + 68), "取消", state="focus", ghost=True)
    button(c, (ix0 + bw + 14, y, ix1, y + 68),
           "回到存档点" if kind == "checkpoint" else "回到主菜单",
           primary=True, danger=(kind == "menu"))
    y += 68 + 18
    hints(c, y, [("Esc", "取消"), ("← →", "切换")])
    return c


def sheet(paths, out):
    pad, gap_x, gap_y = 40, 40, 36
    cw, chh = 1200, 675
    head_h = 44
    sh = 44 + head_h + 26 + (chh + 10 + 26) * 2 + gap_y + 30
    c = Canvas(2560, sh, bg=(25, 29, 33, 255))
    c.text((pad, 44), "暂停菜单 · 状态对照", 34, (231, 226, 214), bold=True, ls=3)
    c.text((pad + 470, 52), "1920 × 1080 画板 · 等比缩放 · Esc 打开 / 关闭", 16, (141, 138, 128), ls=1)
    x0, y0 = pad, 44 + head_h + 26
    for i, (png, letter, name, note) in enumerate(paths):
        col, row = i % 2, i // 2
        x = x0 + col * (cw + gap_x)
        y = y0 + row * (chh + 10 + 26 + gap_y)
        im = Image.open(png).convert("RGB").resize((cw, chh), Image.LANCZOS)
        c.paste(im, (x, y))
        c.rect((x, y, x + cw, y + chh), outline=(58, 64, 72), width=1)
        title = f"{letter} · {name}"
        c.text((x, y + chh + 10), title, 19, (207, 201, 186), ls=1)
        c.text((x + c.measure(title, 19, 1) + 14, y + chh + 13), note, 15, (141, 138, 128), ls=0.5)
    return c.save(out)


def main():
    build_backdrop(os.path.join(BUILD, "backdrop.png"))
    tmp = os.path.join(BUILD, "cells")
    os.makedirs(tmp, exist_ok=True)
    states = [("normal", "A", "打开时", "焦点默认在「继续」，回车直接回到游戏"),
              ("no-checkpoint", "B", "还没有存档点", "该项置灰、不可获得焦点，并直接说明去哪存档"),
              ("checkpoint", "C", "确认：回到存档点", "默认焦点在「取消」—— 危险操作不抢回车"),
              ("menu", "D", "确认：回到主菜单", "同一套版式，主按钮换成红色")]
    cells = []
    for key, letter, name, note in states:
        if key in ("checkpoint", "menu"):
            c = screen_confirm(key)
        else:
            c = screen_pause(key)
        p = os.path.join(tmp, key + ".png")
        c.save(p)
        cells.append((p, letter, name, note))
    hero = Image.open(cells[0][0])
    hero.save(os.path.join(OUT_DIR, "暂停菜单_预览.png"))
    sheet(cells, os.path.join(OUT_DIR, "暂停菜单_状态对照.png"))
    for f in ("暂停菜单_预览.png", "暂停菜单_状态对照.png"):
        print(os.path.join(OUT_DIR, f), Image.open(os.path.join(OUT_DIR, f)).size)


if __name__ == "__main__":
    main()