"""Pack a character-grid drawing into SG6 semigraphics bytes (#573).

SG6 byte layout is 1 C1 L5 L4 L3 L2 L1 L0. Each cell is 2 wide x 3 tall, so
the 512-byte text screen becomes 64x48 pixels. There is only one colour bit
per cell (C1), and the probe on 2026-09-17 found CSS makes no difference:
C1=0 draws red, C1=1 draws orange. Art therefore has to change colour on
2x3 cell boundaries, not per pixel.

Art characters: '.' off, 'R' red, 'O' orange.
"""

# L5 top-left, L4 top-right, L3 mid-left, L2 mid-right, L1 bottom-left, L0 bottom-right
BITS = {(0, 0): 32, (1, 0): 16, (0, 1): 8, (1, 2): 1, (1, 1): 4, (0, 2): 2}
C1 = dict(R=0, O=1)

ART = """
........OOOOOOOO........
......OOOOOOOOOOOO......
....OOOOOOOOOOOOOOOO....
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOORRRRRROOOOOOOOOO..
..OOOORRRRRROOOOOOOOOO..
..OOOORRRRRROOOOOOOOOO..
..RRRRRRRRRRRRRRRRRRRR..
..RRRRRRRRRRRRRRRRRRRR..
..RRRRRRRRRRRRRRRRRRRR..
..RRRROOOOOOOOOOOORRRR..
..RRRROOOOOOOOOOOORRRR..
..RRRROOOOOOOOOOOORRRR..
..OOOORRRRRRRRRRRROOOO..
..OOOORRRRRRRRRRRROOOO..
..OOOORRRRRRRRRRRROOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OORROOOORROOOORROOOO..
..OORROOOORROOOORROOOO..
..OORROOOORROOOORROOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..OOOOOOOOOOOOOOOOOOOO..
..RRRRRRRRRRRRRRRRRRRR..
..RRRRRRRRRRRRRRRRRRRR..
..RRRRRRRRRRRRRRRRRRRR..
..OOOO............OOOO..
..OOOO............OOOO..
..OOOO............OOOO..
OOOOOOOO........OOOOOOOO
OOOOOOOO........OOOOOOOO
OOOOOOOO........OOOOOOOO
""".strip().split("\n")

ORG_C, ORG_R = 10, 2                       # cell origin on the 32x16 screen

cells_w = len(ART[0]) // 2
cells_h = len(ART) // 3

scr = [0x60] * 512                         # $60 is space in T1 codes
for cr in range(cells_h):
    for cc in range(cells_w):
        pat, col = 0, 1
        for dy in range(3):
            for dx in range(2):
                ch = ART[cr * 3 + dy][cc * 2 + dx]
                if ch != '.':
                    pat |= BITS[(dx, dy)]
                    col = C1[ch]
        if pat:
            scr[(ORG_R + cr) * 32 + ORG_C + cc] = 0x80 | (col << 6) | pat

hexs = "".join("%02X" % b for b in scr)
for i in range(0, 512, 128):
    print(0x400 + i, hexs[i * 2:(i + 128) * 2])
