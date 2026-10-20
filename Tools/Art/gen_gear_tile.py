"""生成危险齿轮瓦片贴图（64x64，PPU 128 -> 0.5m 一格，与 Grid cellSize 一致）。

美术替换时保持文件名与 .meta 不变，直接覆盖 PNG 即可 —— 瓦片资产、场景铺装全部自动生效。
"""

import math

from PIL import Image, ImageDraw

N = 64          # 最终边长（像素）
S = 8           # 超采样倍数
W = N * S
C = W / 2.0

R_TIP = 30.5    # 齿顶半径
R_ROOT = 22.0   # 齿根半径（被本体盖住）
R_BODY = 25.0   # 本体半径
R_RING = 15.5   # 内环
R_HUB = 7.0     # 轴心孔

TEETH = 12
HALF_ROOT = 9.0
HALF_TIP = 5.5

INK = (28, 26, 30, 255)
BODY = (96, 88, 84, 255)
BODY_DARK = (72, 66, 64, 255)
TOOTH = (150, 76, 58, 255)
HUB_RIM = (176, 96, 72, 255)
HUB = (54, 48, 46, 255)
GLINT = (198, 186, 178, 255)


def pol(r, deg):
    a = math.radians(deg)
    return (C + r * S * math.cos(a), C - r * S * math.sin(a))


def ring(r, fill=None, outline=None, width=1):
    r *= S
    box = [C - r, C - r, C + r, C + r]
    if fill is not None:
        d.ellipse(box, fill=fill, outline=outline, width=width)
    else:
        d.ellipse(box, outline=outline, width=width)


img = Image.new("RGBA", (W, W), (0, 0, 0, 0))
d = ImageDraw.Draw(img)

# 齿：先画，本体圆再把齿根盖住，连成一整块。
for i in range(TEETH):
    a = i * (360.0 / TEETH)
    poly = [
        pol(R_ROOT, a - HALF_ROOT),
        pol(R_TIP, a - HALF_TIP),
        pol(R_TIP, a + HALF_TIP),
        pol(R_ROOT, a + HALF_ROOT),
    ]
    d.polygon(poly, fill=TOOTH, outline=INK, width=int(0.9 * S))

# 本体 + 下半部暗面
ring(R_BODY, fill=BODY, outline=INK, width=int(1.4 * S))
d.chord([C - R_BODY * S, C - R_BODY * S, C + R_BODY * S, C + R_BODY * S],
        0, 180, fill=BODY_DARK)

# 内环与轴心
ring(R_RING, outline=HUB_RIM, width=int(1.3 * S))
ring(R_HUB, fill=HUB, outline=INK, width=int(1.2 * S))

# 一点高光，别做成方向性的（瓦片是静态的，亮暗只用来表现金属弧形）
d.arc([C - (R_BODY - 4) * S, C - (R_BODY - 4) * S,
       C + (R_BODY - 4) * S, C + (R_BODY - 4) * S],
      110, 170, fill=GLINT, width=int(1.2 * S))

img = img.resize((N, N), Image.LANCZOS)
out = r"D:\Users\lenovo\Documents\GitHub\Project-Rule\Project-Rule\Assets\_Project\Art\Sprites\Tiles\Hazards\GearTile_01.png"
img.save(out)
print("saved", out)
