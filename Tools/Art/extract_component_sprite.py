"""把美术交付的「黑底整幅画布」组件图，切成透明背景的单体精灵。

用法：
    python -I extract_component_sprite.py <输入.jpg> <输出.png> [容差]

- 背景色取左上角像素（交付图是纯黑 #010101）
- 从四条边向内 flood fill，只把「与外界连通的背景」变透明，
  组件内部的深色描边 / 阴影不会被误抠
- 输出裁到内容包围盒（含 PAD 像素留白）
"""
import os
import sys
from collections import deque

from PIL import Image

PAD = 2
EDGE_FEATHER = 1


def flood_background(px, w, h, bg, tol):
    """从四边向内扩散，返回背景像素集合。"""
    def is_bg(x, y):
        p = px[x, y]
        return (abs(p[0] - bg[0]) <= tol
                and abs(p[1] - bg[1]) <= tol
                and abs(p[2] - bg[2]) <= tol)

    bg_mask = bytearray(w * h)
    queue = deque()
    for x in range(w):
        for y in (0, h - 1):
            if is_bg(x, y) and not bg_mask[y * w + x]:
                bg_mask[y * w + x] = 1
                queue.append((x, y))
    for y in range(h):
        for x in (0, w - 1):
            if is_bg(x, y) and not bg_mask[y * w + x]:
                bg_mask[y * w + x] = 1
                queue.append((x, y))

    while queue:
        x, y = queue.popleft()
        for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if 0 <= nx < w and 0 <= ny < h and not bg_mask[ny * w + nx] and is_bg(nx, ny):
                bg_mask[ny * w + nx] = 1
                queue.append((nx, ny))
    return bg_mask


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    src, dst = sys.argv[1], sys.argv[2]
    tol = int(sys.argv[3]) if len(sys.argv) > 3 else 22

    im = Image.open(src).convert("RGB")
    w, h = im.size
    px = im.load()
    bg = px[0, 0]
    bg_mask = flood_background(px, w, h, bg, tol)

    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        row = y * w
        for x in range(w):
            if not bg_mask[row + x]:
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y
    if maxx < 0:
        print(f"!! {src} 没有找到前景")
        return 2

    minx = max(0, minx - PAD)
    miny = max(0, miny - PAD)
    maxx = min(w - 1, maxx + PAD)
    maxy = min(h - 1, maxy + PAD)
    cw, ch = maxx - minx + 1, maxy - miny + 1

    out = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    op = out.load()
    for y in range(ch):
        sy = miny + y
        for x in range(cw):
            sx = minx + x
            if bg_mask[sy * w + sx]:
                continue
            r, g, b = px[sx, sy]
            alpha = 255
            # 前景里紧挨背景的一圈：按「离黑底多远」给半透明，消掉 jpg 的暗边
            for nx, ny in ((sx + 1, sy), (sx - 1, sy), (sx, sy + 1), (sx, sy - 1),
                           (sx + 1, sy + 1), (sx - 1, sy - 1), (sx + 1, sy - 1), (sx - 1, sy + 1)):
                if 0 <= nx < w and 0 <= ny < h and bg_mask[ny * w + nx]:
                    peak = max(r, g, b)
                    if peak < 128:
                        alpha = int(255 * peak / 128)
                    break
            op[x, y] = (r, g, b, alpha)

    os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
    out.save(dst)
    print(f"{os.path.basename(src)} -> {os.path.basename(dst)}  {cw}x{ch}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
