"""Pittsburgh's three rivers in SG6 semigraphics, for the CoCo terminal (#573).

CORRECTION (2026-10-12, #573): the red/orange palette below came from a
flawed probe that used solid blocks, which look the same in SG4 and SG6.
The CoCo terminal runs on the CoCo 2B's MC6847T1, which has no SG6: with
lowercase on it stays in SG4, so these bytes come out scrambled there.
This script is only right on a plain-6847 CoCo with $FF22 bit 4 set, and
there bit 6 gives blue/red (CSS=0) or magenta/orange (CSS=1), not
red/orange. See coco-guides/vdg-modes.md, Semigraphics-6.

The Point is where the Allegheny (from the north east) and the Monongahela
(from the south east) meet to form the Ohio (heading west). Rivers are red,
the Golden Triangle downtown is orange, everything else is black.

64x48 pixels in 512 bytes. Colour is per 2x3 cell. Run with --preview to dump
the art as text.
"""

import sys

BITS = {(0, 0): 32, (1, 0): 16, (0, 1): 8, (1, 1): 4, (0, 2): 2, (1, 2): 1}

W, H = 64, 48
POINT = (24.0, 24.0)
RIVERS = [                                  # (from, to)
    (POINT, (63.0, 5.0)),                   # Allegheny, up to the north east
    (POINT, (63.0, 39.0)),                  # Monongahela, down to the south east
    (POINT, (0.0, 12.0)),                   # Ohio, away to the west
]
BANK = 1.1                                  # half-width of a river, in pixels


def dist(px, py, a, b):
    """Distance from a pixel to a line segment."""
    (ax, ay), (bx, by) = a, b
    vx, vy = bx - ax, by - ay
    t = ((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy)
    t = max(0.0, min(1.0, t))
    dx, dy = px - (ax + t * vx), py - (ay + t * vy)
    return (dx * dx + dy * dy) ** 0.5


def river(x, y):
    return any(dist(x, y, a, b) <= BANK for a, b in RIVERS)


def downtown(x, y):
    """The wedge between the Allegheny and the Mon, out to the Hill District."""
    if not 25 <= x <= 44:
        return False
    hi = 24.0 - (19.0 / 39.0) * (x - 24) + BANK + 1
    lo = 24.0 + (15.0 / 39.0) * (x - 24) - BANK - 1
    return hi <= y <= lo


def on(x, y):
    return river(x, y) or downtown(x, y)


scr = [0x60] * 512                          # $60 is space in T1 codes
for cr in range(H // 3):
    for cc in range(W // 2):
        pat, warm, lit = 0, 0, 0
        for dy in range(3):
            for dx in range(2):
                x, y = cc * 2 + dx, cr * 3 + dy
                if on(x, y):
                    pat |= BITS[(dx, dy)]
                    lit += 1
                    if downtown(x, y) and not river(x, y):
                        warm += 1
        if pat:
            c1 = 1 if warm * 2 >= lit else 0
            scr[cr * 32 + cc] = 0x80 | (c1 << 6) | pat

if "--preview" in sys.argv:
    for y in range(H):
        row = ""
        for x in range(W):
            row += "R" if river(x, y) else ("O" if downtown(x, y) else ".")
        print(row)
else:
    hexs = "".join("%02X" % b for b in scr)
    for i in range(0, 512, 128):
        print(0x400 + i, hexs[i * 2:(i + 128) * 2])
