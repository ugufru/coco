"""Build an SG4 semigraphics R2-D2 and emit poke-ready hex for $0400.

Each SG4 byte is 1 C C C L3 L2 L1 L0: colour 0-7 plus four quadrant bits
(L3 upper-left, L2 upper-right, L1 lower-left, L0 lower-right). One colour
per cell, so the art below is laid out on 2x2 pixel boundaries.

SG4 is the right format for the CoCo terminal: the CoCo 2B's MC6847T1 stays
in SG4 with lowercase on ($FF22 bit 4), it has no SG6 (#573).
"""

ART = """
....WWWWWWWW....
..WWWWWWWWWWWW..
..WWRRWWWWBBWW..
..WWRRWWWWBBWW..
..BBBBBBBBBBBB..
..BBBBBBBBBBBB..
..WWWWWWWWWWWW..
..WWWWWWWWWWWW..
..BBWWWWWWWWBB..
..BBWWWWWWWWBB..
..WWBBBBBBBBWW..
..WWBBBBBBBBWW..
..WWWWWWWWWWWW..
..WWWWWWWWWWWW..
..WWBBWWWWBBWW..
..WWBBWWWWBBWW..
..WWWWWWWWWWWW..
..WWWWWWWWWWWW..
..BBBBBBBBBBBB..
..BBBBBBBBBBBB..
..WW........WW..
..WW........WW..
WWWWWW....WWWWWW
WWWWWW....WWWWWW
""".strip().split("\n")

COL = dict(W=4, B=2, R=3)                      # buff, blue, red
BITS = {(0, 0): 8, (1, 0): 4, (0, 1): 2, (1, 1): 1}

scr = [0x60] * 512                             # $60 is space in T1 codes
ORG_C, ORG_R = 12, 2

for cr in range(12):
    for cc in range(8):
        pat, col = 0, 4
        for dy in range(2):
            for dx in range(2):
                ch = ART[cr * 2 + dy][cc * 2 + dx]
                if ch != '.':
                    pat |= BITS[(dx, dy)]
                    col = COL[ch]
        scr[(ORG_R + cr) * 32 + ORG_C + cc] = 0x80 | (col << 4) | pat

for i, ch in enumerate("R2-D2"):               # $20-$3F -> OR $40, $40-$5F as-is
    c = ord(ch)
    scr[15 * 32 + 13 + i] = c if 0x40 <= c <= 0x5F else c | 0x40

hexs = "".join("%02X" % b for b in scr)
for i in range(0, 512, 128):
    print(0x400 + i, hexs[i * 2:(i + 128) * 2])
