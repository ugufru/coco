"""Beach at sunset in SG6 semigraphics, poke-ready for the CoCo terminal (#573).

The SG6 palette is exactly right for this: orange sun and sand, red clouds and
sea, black sky. Full screen, 64x48 pixels in 512 bytes.

Colour is per 2x3 cell, so the colour test runs on the cell, not the pixel.
Run with --preview to dump the art as text before poking it.
"""

import sys

BITS = {(0, 0): 32, (1, 0): 16, (0, 1): 8, (1, 1): 4, (0, 2): 2, (1, 2): 1}

W, H = 64, 48
HORIZON = 27                                # first sea row; whole cell rows are 3 tall
SAND = 42                                   # foam line; sand fills the rest
SUN_X, SUN_Y, SUN_R = 32, 27, 11
REFLECT = range(28, 36)                     # sun's glitter path on the water

CLOUDS = [                                  # (row, x start, x end) inclusive
    (7, 4, 20), (8, 2, 17),
    (12, 40, 58), (13, 44, 61),
    (17, 6, 24), (18, 10, 21),
]


def on(x, y):
    """Is this pixel lit?"""
    if y < HORIZON:                                     # sky
        dx, dy = x - SUN_X, y - SUN_Y
        if dx * dx + dy * dy <= SUN_R * SUN_R:
            return True
        return any(r == y and a <= x <= b for r, a, b in CLOUDS)
    if y < SAND:                                        # sea
        if x in REFLECT:                                # broken glitter path
            return (x + y) % 3 != 0
        return (y % 2 == 0) and ((x + y // 2) % 6) < 4   # dashed wave lines
    if y == SAND:                                       # foam edge of the beach
        return (x % 7) != 3
    return True                                         # sand


def orange(x, y):
    """Orange where the light falls; red for cloud, sea and shadow."""
    if y >= SAND:
        return True                                     # sand
    if y >= HORIZON:
        return x in REFLECT                             # only the glitter path
    dx, dy = x - SUN_X, y - SUN_Y
    return dx * dx + dy * dy <= SUN_R * SUN_R           # the sun itself


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
                    if orange(x, y):
                        warm += 1
        if pat:
            c1 = 1 if warm * 2 >= lit else 0
            scr[cr * 32 + cc] = 0x80 | (c1 << 6) | pat

if "--preview" in sys.argv:
    for y in range(H):
        print("".join(("O" if orange(x, y) else "R") if on(x, y) else "." for x in range(W)))
else:
    hexs = "".join("%02X" % b for b in scr)
    for i in range(0, 512, 128):
        print(0x400 + i, hexs[i * 2:(i + 128) * 2])
