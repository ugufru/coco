# FujiNet Time

Asks a FujiNet for the time and prints it as

```
YYYY.MM.DD HH:MM:SS
```

on the text screen, then halts.

```
make                  # build fujinet-time.bin
make -C ../.. fntime-dsk   # a one-program disk, build/fntime.dsk, for the FujiNet SD card
```

On the CoCo: `LOADM"FNTIME":EXEC` (it is also on `build/demos.dsk`).

**Needs a FujiNet.** The request goes out over DriveWire through the
cartridge's bit-banger serial driver (`lib/fujinet.fs`), so it needs the
FujiNet connected and an HDB-DOS cartridge, as the clock demo does
(`src/clock`). While it waits for the FujiNet to answer it prints one dot
per frame, so "no FujiNet" (dots forever) is easy to tell from a crash
(nothing at all). Stock XRoar has no FujiNet.

How it works:

- `fn-ping` until the FujiNet answers, then the time command (`$23`)
  with `dw-write`, and six bytes back with `dw-read`: year since 1900,
  month, day, hour, minute, second.
- The six-byte buffer is a `CODE` word that pushes the address of its
  own reserved bytes (`RMB 6`), so no variable space is needed.
- `print-year` turns years since 1900 into four digits (1900-2099).

See `src/clock` for a clock that keeps running from this time.
