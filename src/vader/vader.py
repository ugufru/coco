#!/usr/bin/env python3
# vader.py: SG4 Darth Vader helmet plus vaping PSA captions for the
# CoCo text screen ($0400, 32x16). Prints hex for live VRAM pokes.
# Image: rows 0-11 (64x24 SG4 pixels). Captions: rows 12-15.

# Left half of the helmet, mirrored. '.' red, '#' black, 'o' buff.
HALF = """
.........................#######
....................############
..................##############
.................###############
................################
...............#################
...............#################
..............##################
..............##################
..............####oooooooooooooo
..............####o#######o###oo
..............####o########o##oo
..............####oo########o#oo
.............######oooooooooo#oo
.............##############o##oo
............###############o####
............##############o##o##
...........##############o##o#o#
..........##############o##o#o#o
.........##############o##o#o#o#
........##############oooooooooo
.......#########################
.....###########################
....############################
""".split()

COLOR = {'.': 3, 'o': 4}  # SG4 color index: 3 red, 4 buff

CAPTIONS = [
    ["", "I FIND YOUR LACK OF", "LUNG CAPACITY DISTURBING.", ""],
    ["", "VAPING IS A PATH TO", "THE DARK SIDE.", ""],
    ["NICOTINE LEADS TO ADDICTION.", "ADDICTION LEADS TO CRAVING.",
     "CRAVING LEADS TO", "SUFFERING."],
    ["", "IT STRAINS YOUR HEART AND", "REWIRES A YOUNG BRAIN.", ""],
    ["I NEED A MACHINE TO BREATHE.", "YOU DO NOT.",
     "", "KEEP IT THAT WAY."],
    ["", "THE FORCE IS STRONG WITH", "THOSE WHO QUIT.", ""],
]


def image():
    rows = [h.ljust(32, '.')[:32] for h in HALF]
    rows = [r + r[::-1] for r in rows]
    out = bytearray()
    for cy in range(12):
        for cx in range(32):
            px = [rows[cy * 2][cx * 2], rows[cy * 2][cx * 2 + 1],
                  rows[cy * 2 + 1][cx * 2], rows[cy * 2 + 1][cx * 2 + 1]]
            colors = {COLOR[p] for p in px if p != '#'}
            if len(colors) > 1:  # red and buff in one cell: buff wins
                colors = {4}
            c = colors.pop() if colors else 0
            bits = sum(8 >> i for i, p in enumerate(px) if p != '#')
            out.append(0x80 | (c << 4) | bits)
    return bytes(out)


def text(s):
    # T1 screen codes, centered in 32 columns
    s = s.center(32)
    out = bytearray()
    for ch in s:
        a = ord(ch)
        if a == 0x20:
            out.append(0x60)
        elif 0x20 < a < 0x40:
            out.append(a | 0x40)
        else:
            out.append(a & 0x5F if a < 0x60 else a & 0x1F)
    return bytes(out)


if __name__ == '__main__':
    img = image()
    for i in range(0, len(img), 128):
        print(f'${0x400 + i:04X} {img[i:i + 128].hex()}')
    for cap in CAPTIONS:
        print(f'$0580 {b"".join(text(l) for l in cap).hex()}')
