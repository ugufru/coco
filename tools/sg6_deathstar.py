"""Death Star in SG6 semigraphics, poke-ready for the CoCo terminal (#573).

CORRECTION (2026-10-12, #573): the red/orange palette below came from a
flawed probe that used solid blocks, which look the same in SG4 and SG6.
The CoCo terminal runs on the CoCo 2B's MC6847T1, which has no SG6: with
lowercase on it stays in SG4, so these bytes come out scrambled there.
This script is only right on a plain-6847 CoCo with $FF22 bit 4 set, and
there bit 6 gives blue/red (CSS=0) or magenta/orange (CSS=1), not
red/orange. See coco-guides/vdg-modes.md, Semigraphics-6.

SG6 byte is 1 C1 L5 L4 L3 L2 L1 L0: a 2 wide x 3 tall cell, one colour bit.
C1=1 orange, C1=0 red (measured 2026-09-17; CSS makes no difference).

Pixels come from the sphere, but colour is decided per CELL, because a cell
cannot hold two colours. The dish and the equatorial trench are red, the rest
of the sphere is orange.
"""

BITS = {(0, 0): 32, (1, 0): 16, (0, 1): 8, (1, 1): 4, (0, 2): 2, (1, 2): 1}

W, H = 36, 36                              # 18 cells wide, 12 cell rows
CX = CY = (W - 1) / 2.0
R = W / 2.0

DISH_CX, DISH_CY, DISH_R = 11.5, 10.5, 5.0
TRENCH_TOP, TRENCH_BOT = 21, 23            # whole cell row, so colour aligns

ORG_C, ORG_R = 7, 2                        # cell origin on the 32x16 screen


def on(x, y):
    """Is this pixel part of the sphere?"""
    dx, dy = x - CX, y - CY
    return dx * dx + dy * dy <= R * R


def red(x, y):
    """Is this pixel in a red region (dish or trench)?"""
    if TRENCH_TOP <= y <= TRENCH_BOT:
        return True
    dx, dy = x - DISH_CX, y - DISH_CY
    return dx * dx + dy * dy <= DISH_R * DISH_R


scr = [0x60] * 512                         # $60 is space in T1 codes
for cr in range(H // 3):
    for cc in range(W // 2):
        pat = 0
        reds = 0
        for dy in range(3):
            for dx in range(2):
                x, y = cc * 2 + dx, cr * 3 + dy
                if on(x, y):
                    pat |= BITS[(dx, dy)]
                    if red(x, y):
                        reds += 1
        if pat:
            c1 = 0 if reds >= 2 else 1     # a cell is red only if mostly red
            scr[(ORG_R + cr) * 32 + ORG_C + cc] = 0x80 | (c1 << 6) | pat

hexs = "".join("%02X" % b for b in scr)
for i in range(0, 512, 128):
    print(0x400 + i, hexs[i * 2:(i + 128) * 2])
