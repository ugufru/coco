#!/usr/bin/env python3
"""What vgm-test.bin should print: a model of lib/vgm.fs's step (#598)."""

import sys


def le(d, o):
    return d[o] | d[o + 1] << 8


def main():
    v = open(sys.argv[1], 'rb').read()
    k = int(sys.argv[2])
    p = 0x40 if le(v, 8) < 0x150 else (0x34 + le(v, 0x34) if le(v, 0x34) else 0x40)
    loop = 0x1C + le(v, 0x1C) if le(v, 0x1C) else 0
    clk = le(v, 0x0D)
    q = 0x3D0900 // clk if clk else 256
    m = 0x3FFF if q & 0xC000 else q
    f = lambda o: (le(v, o) | le(v, o + 2) << 16) // 735
    frames = f(0x18) + (f(0x20) if loop else 0)
    div, out, lvl = [0] * 3, [0] * 3, [15] * 4
    reg, w, loops = 0, 0, 1
    for _ in range(k):
        if w >= 735:
            w -= 735
            continue
        d = 735 - w
        while True:
            c = v[p]
            p += 1
            if c == 0x50:
                b = v[p]
                p += 1
                if b & 0x80:
                    reg = b >> 4 & 7
                if reg & 1:
                    lvl[reg >> 1] = b & 15
                    continue
                if reg == 6:
                    continue
                ch = reg >> 1
                if b & 0x80:
                    div[ch] = div[ch] & 0x3F0 | b & 15
                else:
                    div[ch] = div[ch] & 0x0F | (b & 0x3F) << 4
                n = div[ch]
                s = ((n & 255) * (m & 255) + 0x80 >> 8) + (n & 255) * (m >> 8) \
                    + (n >> 8) * (m & 255) + ((n >> 8) * (m >> 8) << 8)
                out[ch] = min(s, 1023)
                continue
            n = {0x62: 735, 0x63: 882}.get(c)
            if c == 0x61:
                n = le(v, p)
                p += 2
            elif c & 0xF0 == 0x70:
                n = (c & 15) + 1
            elif c == 0x66:
                if loops and loop:
                    loops, p = loops - 1, loop
                    continue
                raise SystemExit('ended before frame %d' % k)
            elif n is None:
                raise SystemExit('command %02X not modelled' % c)
            if n < d:
                d -= n
            else:
                w = n - d
                break
    h = lambda xs: ' '.join('%04X' % x for x in xs)
    print('FRAMES %04X M %04X' % (frames, m))
    print('AT %d:' % k)
    print('LVL', h(lvl))
    print('DIV', h(div))
    print('OUT', h(out))
    print('P %04X W %04X' % (p, w))
    print('T %04X' % k)


if __name__ == '__main__':
    main()
