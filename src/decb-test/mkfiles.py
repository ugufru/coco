"""mkfiles.py - test files for decb-test (#586).

Writes into build/:
    hello.txt   a short multi-line text, shown on screen
    song.sng    a copy of the Badinerie stream (spans two granules)
    big.dat     7000 pseudo-random bytes (spans four granules)
    expect.bin  the expected length and 16-bit byte sum of song.sng and
                big.dat, four big-endian words, spliced into decb-test.fs
"""

import os
import random

os.makedirs("build", exist_ok=True)

hello = (b"HELLO FROM A DECB FILE.\r"
         b"READ WITH LIB/DECB.FS, NO\r"
         b"DISK BASIC INVOLVED.\r")
open("build/hello.txt", "wb").write(hello)

song = open("../sound-sn76489/build/badinerie.snd", "rb").read()
open("build/song.sng", "wb").write(song)

rnd = random.Random(586)
big = bytes(rnd.randrange(256) for _ in range(7000))
open("build/big.dat", "wb").write(big)

out = b""
for data in (song, big):
    out += len(data).to_bytes(2, "big") + (sum(data) & 0xFFFF).to_bytes(2, "big")
open("build/expect.bin", "wb").write(out)
print(f"song {len(song)} B, big {len(big)} B")
