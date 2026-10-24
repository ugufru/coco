# vgm-test: lib/vgm.fs against its model (#598)

```
make run      # builds and runs on XRoar with the GameMaster (-cart gmc)
make expect   # what the screen should say
```

Embeds the Badinerie's first 20 seconds as a VGM file
(`src/sound-sn76489/sng2vgm.py`, logged for a 3579545 Hz chip, with a
loop and a GD3 tag). It prints the tag, the length in frames and the
pitch scale, seeks 600 frames with `vgm-skip`, and prints the player's
state in hex: levels, the dividers as the file built them, the dividers
sent after scaling, the stream offset, the wait and the frame count.
`expect.py` runs a model of the player's step on the same file, and
every line must match. Then it plays the file, twice through for the
loop; it should sound like `BADINERI.SNG`.

The title and composer show lowercase as inverse characters on the
text screen; the jukebox's CG font prints them in capitals.

Checked 2026-10-23: every value matched.
