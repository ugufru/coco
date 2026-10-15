# sn76489 demos for Bare Naked Forth

The xroar-waveshare-rp2350-pizero has built in support for sn76489.

Xroar supports this via the GameMaster "cartridge" for testing.

But we need a good audio demo for it written in Forth.

## Badinerie (#579)

J. S. Bach's Badinerie (Orchestral Suite No. 2, BWV 1067) in three voices
plus noise percussion.

```
make run          # builds, then xroar ... -cart gmc -run badinerie.bin
```

| File | Role |
|------|------|
| `badinerie.py` | The hand transcription (flute, inner line, bass) and the drum pattern |
| `badinerie.perf` | Its performance: tempo, dynamics, voicing, repeats (#614) |
| `sn76489.py`   | Score text to SN76489 byte stream |
| `midi2sng.py`  | MIDI file to the same stream (`make songs` converts `midi/*.mid`) |
| `SNG-FORMAT.md` | The song stream (.SNG) format spec |
| `PERFORMANCE.md` | Performance files (`midi/NAME.perf`): tempo shaping, dynamics and voicing for the MIDI imports (#609) |
| `badinerie.fs` | The demo: banner, play-again prompt, and the song spliced in with `DATA[PY` |
| `../../lib/sn76489.fs` | Chip words (`sn!`, `sn-tone`, `sn-vol`, `cart-audio`, ...) and the player (`sn-play`, or `sn-start` + `sn-frame` per vsync) (#581) |

How it fits together:

- The chip is at `$FF41`. XRoar's `gmc` cart sends any write to an odd
  address in `$FF40-$FF5F` to the SN76489 (`xroar/src/gmc.c`), clocked at
  4 MHz. `$FF41` is also the Becker port, so no Becker cart in the same
  session.
- Timing is in whole 60 Hz frames; the performance file sets the tempo.
- The lowest tone at 4 MHz is about B2 (divider 1023). Lower bass notes
  are lifted an octave, with a warning at build time.
- Volume bytes in the stream set a note's starting level; the player
  decays it toward a per-channel floor (`sn-lvl`, `sn-dmask`, `sn-floor`
  in `lib/sn76489.fs`), so notes pluck rather than drone.

- It is performed like the MIDI imports: `badinerie.perf` gives the
  tempo, dynamics and voicing (#614). Both halves are repeated, and the
  repeats are written out (A A B B) so the second time through can be
  shaped differently; the stream is about 6.5 KB. (The `$E8`/`$E9`
  repeat bytes it used to use are still in the format.)
- When it ends, any key plays it again and BREAK quits to BASIC (#585).
  BREAK during playback also quits.

Source: hand-transcribed from the public-domain Bach-Gesellschaft score
(vol. 31, IMSLP #01467, pp. 15-16). Mutopia has no Badinerie, and no MIDI
is used or checked in. Editorial choices (trills, one grace note, one
C natural) are listed at the top of `badinerie.py`.
