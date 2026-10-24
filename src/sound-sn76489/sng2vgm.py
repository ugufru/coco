#!/usr/bin/env python3
"""sng2vgm.py - render a .SNG song into a VGM file (#598).

Test material for lib/vgm.fs that is ours to check in. The song is run
through a model of lib/sn76489.fs's player, frame by frame (the stream,
its repeat marks and the decay envelope), and every change the player
makes to the chip is logged as a VGM command, the way an emulator logs a
game. Played back with lib/vgm.fs the VGM should sound the same as the
.SNG does with lib/sn76489.fs.

The VGM is logged for a chip clocked at 3579545 Hz, like a Master
System, not the GameMaster's 4 MHz: every tone divider is converted, so
the file only plays in tune if the player scales pitch by clock. It also
uses the commands real files do: lone data bytes after a latch, $62 and
$61 waits, a loop offset and a GD3 tag.

Usage:
    python3 sng2vgm.py in.sng out.vgm [--seconds N] [--clock HZ] [--no-loop]
"""

import argparse
import struct

SNG_CLOCK = 4000000
FRAME = 735                     # samples per 60 Hz frame at 44100 Hz

# lib/sn76489.fs defaults: decay mask and floor per channel 0-3.
DMASK = [1, 3, 3, 0]
FLOOR = [8, 10, 8, 15]


def sng_body(data):
    """Skip a v2 header; return (title, composer, stream offset)."""
    if data[0] != 0xEA:
        return '', '', 0
    p = 4
    title = data[p + 1:p + 1 + data[p]].decode('ascii')
    p += 1 + data[p]
    composer = data[p + 1:p + 1 + data[p]].decode('ascii')
    p += 1 + data[p]
    return title, composer, p


def run_sng(data, start):
    """Yield, per frame, the chip writes the player makes: a list of
    ('tone', ch, divider), ('noise', byte), ('vol', ch, att)."""
    lvl = [15] * 4
    sp, wait, tick, reps, mark = start, 1, 0, 0, start
    div = [0, 0, 0]
    while wait:
        tick += 1
        wait -= 1
        writes = []
        if wait == 0:
            while True:
                b = data[sp]
                sp += 1
                if b < 0x80:
                    wait = b
                    break
                if b & 0x90 == 0x90:
                    lvl[(b >> 5) & 3] = b & 15
                elif b == 0xE8:
                    mark, reps = sp, 1
                elif b == 0xE9:
                    if reps:
                        reps, sp = 0, mark
                elif b >= 0xE0:
                    writes.append(('noise', b))
                else:
                    ch = (b >> 5) & 3
                    d = data[sp]
                    sp += 1
                    div[ch] = (d & 0x3F) << 4 | (b & 0x0F)
                    writes.append(('tone', ch, div[ch]))
        for ch in range(4):
            if DMASK[ch] & tick == 0 and FLOOR[ch] > lvl[ch]:
                lvl[ch] += 1
            writes.append(('vol', ch, lvl[ch]))
        yield writes


class VgmLog:
    """The SN76489 as a VGM log sees it: writes only what changes."""

    def __init__(self, clock):
        self.clock = clock
        self.out = bytearray()
        self.div = [None] * 3
        self.vol = [None] * 4
        self.reg = None             # last latched register, 0-7
        self.pending = 0            # samples not yet written as a wait

    def sn(self, b):
        self.flush()
        self.out += bytes([0x50, b])

    def tone(self, ch, n4m):
        n = round(n4m * self.clock / SNG_CLOCK) if n4m else 0
        n = max(0, min(1023, n))
        old = self.div[ch]
        if old == n:
            return
        if old is not None and self.reg == ch * 2 and (old & 15) == (n & 15):
            self.sn(n >> 4)                         # a lone data byte
        else:
            self.sn(0x80 | ch << 5 | n & 15)
            self.reg = ch * 2
            if old is None or old >> 4 != n >> 4:
                self.sn(n >> 4)
        self.div[ch] = n

    def volume(self, ch, att):
        if self.vol[ch] != att:
            self.sn(0x90 | ch << 5 | att)
            self.reg = ch * 2 + 1
            self.vol[ch] = att

    def noise(self, b):
        self.sn(b)
        self.reg = 6

    def wait(self, samples):
        self.pending += samples

    def flush(self):
        n = self.pending
        while n:
            if n == FRAME:
                self.out.append(0x62)
                n = 0
            else:
                k = min(n, 0xFFFF)
                self.out += bytes([0x61]) + struct.pack('<H', k)
                n -= k
        self.pending = 0


def gd3(title, game, system, author):
    strings = [title, '', game, '', system, '', author, '', '2026',
               'sng2vgm.py', 'Rendered from a .SNG song (coco #598)']
    body = b''.join(s.encode('utf-16-le') + b'\0\0' for s in strings)
    return b'Gd3 ' + struct.pack('<II', 0x100, len(body)) + body


def main():
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('sng')
    ap.add_argument('vgm')
    ap.add_argument('--seconds', type=float, default=0,
                    help='stop after this many seconds (0 = whole song)')
    ap.add_argument('--clock', type=int, default=3579545)
    ap.add_argument('--no-loop', action='store_true')
    a = ap.parse_args()

    data = open(a.sng, 'rb').read()
    title, composer, start = sng_body(data)
    limit = int(a.seconds * 60) if a.seconds else None

    log = VgmLog(a.clock)
    frames = 0
    for writes in run_sng(data, start):
        for w in writes:
            if w[0] == 'tone':
                log.tone(w[1], w[2])
            elif w[0] == 'noise':
                log.noise(w[1])
            else:
                log.volume(w[1], w[2])
        log.wait(FRAME)
        frames += 1
        if limit and frames >= limit:
            break
    for ch in range(4):
        log.volume(ch, 15)
    log.flush()
    log.out.append(0x66)

    total = frames * FRAME
    hdr = bytearray(0x40)
    hdr[0:4] = b'Vgm '
    struct.pack_into('<I', hdr, 0x08, 0x150)
    struct.pack_into('<I', hdr, 0x0C, a.clock)
    struct.pack_into('<I', hdr, 0x18, total)
    if not a.no_loop:
        struct.pack_into('<I', hdr, 0x1C, 0x40 - 0x1C)      # loop: the start
        struct.pack_into('<I', hdr, 0x20, total)
    struct.pack_into('<I', hdr, 0x24, 60)
    struct.pack_into('<H', hdr, 0x28, 0x0009)               # SMS noise feedback
    hdr[0x2A] = 16                                          # shift register width
    struct.pack_into('<I', hdr, 0x34, 0x40 - 0x34)

    tag = gd3(title or 'Untitled', 'coco', 'GameMaster SN76489', composer)
    struct.pack_into('<I', hdr, 0x14, 0x40 + len(log.out) - 0x14)
    vgm = hdr + log.out + tag
    struct.pack_into('<I', vgm, 0x04, len(vgm) - 4)
    open(a.vgm, 'wb').write(vgm)
    print(f'{a.vgm}: {len(vgm)} bytes, {frames} frames ({frames / 60:.1f} s)')


if __name__ == '__main__':
    main()
