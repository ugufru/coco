# DECB Reader Test

A test program, not a demo: it checks `lib/decb.fs`, the disk reader that
needs no Disk BASIC (#586), against a test disk with known contents.

```
make run    # builds the test disk and runs with a disk controller and it in drive 0
```

`mkfiles.py` writes the files onto `build/test.dsk` and records what
they should read back as (length and 16-bit byte sum) in
`build/expect.bin`, which the program embeds. Then it:

1. Shows `HELLO.TXT` through `dk-getc`, a byte at a time.
2. Streams `SONG.SNG` (the Badinerie, two granules) with `dk-getc` and
   checks its length and sum.
3. Loads `BIG.DAT` (7000 random bytes, four granules) with `dk-load` and
   checks the same.
4. Opens `NOPE.BIN`, which is not there: error 1 expected.
5. Opens `X:7`, a drive that does not exist: error 11 expected.

Every check prints `OK` or `FAIL`. Not on any demo disk.
