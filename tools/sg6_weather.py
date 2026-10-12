"""Today's Pittsburgh weather as an SG6 icon for the CoCo terminal (#573).

CORRECTION (2026-10-12, #573): the red/orange palette below came from a
flawed probe that used solid blocks, which look the same in SG4 and SG6.
The CoCo terminal runs on the CoCo 2B's MC6847T1, which has no SG6: with
lowercase on it stays in SG4, so these bytes come out scrambled there.
This script is only right on a plain-6847 CoCo with $FF22 bit 4 set, and
there bit 6 gives blue/red (CSS=0) or magenta/orange (CSS=1), not
red/orange. See coco-guides/vdg-modes.md, Semigraphics-6.

Sun behind a storm cloud, rain and a lightning bolt, with a text caption on
the last row. SG6 and text coexist: bytes with bit 7 set are graphics cells,
bytes with bit 7 clear are T1 characters, so the caption is just poked in as
screen codes.

Edit the constants below to redraw it. Drawn 2026-09-17, 5:50pm: 85F, partly
cloudy, feels like 94F, storms over for the day. The 2026-09-16 version was
scattered storms: set STORMY = True, SUN (18, 13, 9), PUFFS (28, 21, 8), (39, 18, 10),
(49, 22, 7) and CLOUD_BASE (24, 54, 22, 28) to get it back.
"""

import sys

BITS = {(0, 0): 32, (1, 0): 16, (0, 1): 8, (1, 1): 4, (0, 2): 2, (1, 2): 1}

W, H = 64, 42                               # 14 cell rows; row 15 is the caption
CAPTION = "85F FEELS 94 PARTLY CLOUDY"
STORMY = False                              # rain and lightning under the cloud

SUN = (25, 17, 12)                          # x, y, radius
PUFFS = [(38, 28, 7), (47, 25, 9), (55, 29, 6)]
CLOUD_BASE = (33, 60, 29, 34)               # x from, x to, y from, y to
BOLT = [(37, 30), (33, 36), (36, 36), (32, 43)]


def in_circle(x, y, c):
    cx, cy, r = c
    return (x - cx) ** 2 + (y - cy) ** 2 <= r * r


def cloud(x, y):
    if any(in_circle(x, y, p) for p in PUFFS):
        return True
    x0, x1, y0, y1 = CLOUD_BASE
    return x0 <= x <= x1 and y0 <= y <= y1


def rain(x, y):
    """Slanted dashes falling out of the cloud base."""
    return 30 <= y <= 41 and 26 <= x <= 52 and (x + y) % 7 < 2


def bolt(x, y):
    """Thick polyline down the middle of the rain."""
    for (ax, ay), (bx, by) in zip(BOLT, BOLT[1:]):
        if ay <= y <= by:
            t = 0.0 if by == ay else (y - ay) / float(by - ay)
            if abs(x - (ax + t * (bx - ax))) <= 1.2:
                return True
    return False


def sun(x, y):
    return in_circle(x, y, SUN) and not cloud(x, y)


def on(x, y):
    storm = STORMY and (rain(x, y) or bolt(x, y))
    return sun(x, y) or cloud(x, y) or storm


def warm(x, y):
    return sun(x, y) or (STORMY and bolt(x, y))


scr = [0x60] * 512                          # $60 is space in T1 codes
for cr in range(H // 3):
    for cc in range(W // 2):
        pat, hot, lit = 0, 0, 0
        for dy in range(3):
            for dx in range(2):
                x, y = cc * 2 + dx, cr * 3 + dy
                if on(x, y):
                    pat |= BITS[(dx, dy)]
                    lit += 1
                    if warm(x, y):
                        hot += 1
        if pat:
            c1 = 1 if hot * 2 >= lit else 0
            scr[cr * 32 + cc] = 0x80 | (c1 << 6) | pat

start = 15 * 32 + (32 - len(CAPTION)) // 2  # T1 screen codes for the caption
for i, ch in enumerate(CAPTION):
    c = ord(ch)
    scr[start + i] = c if 0x40 <= c <= 0x5F else (c & 0x1F if c >= 0x60 else c | 0x40)

if "--preview" in sys.argv:
    for y in range(H):
        print("".join(("O" if warm(x, y) else "R") if on(x, y) else "." for x in range(W)))
    print(CAPTION.center(32))
else:
    hexs = "".join("%02X" % b for b in scr)
    for i in range(0, 512, 128):
        print(0x400 + i, hexs[i * 2:(i + 128) * 2])
