# Sound tour (#634)

One phrase, the opening of the Ode to Joy, played on every sound option
the project can drive, so the voices can be compared side by side.

```
make run    # XRoar, with a Multi-Pak: GameMaster in slot 0, Orchestra-90 in slot 1
make dsk    # build/sound-tour.dsk for the CoCo Zero (SOUNDTUR.BIN, autoruns)
```

| Key | Device | Library | What plays |
|-----|--------|---------|------------|
| 1 | 1-bit output, `$FF22` bit 1 | `lib/sound.fs` `snd-tone1` | the melody, square |
| 2 | 6-bit DAC, `$FF20` | `lib/sound.fs` `snd-saw`, `snd-zap` ... | the melody as a 4-step sawtooth, then the effects |
| 3 | SN76489 (GameMaster) | `lib/sn76489.fs` | melody, bass, white-noise hats, software decay |
| 4 | CoCo PSG (YM2149) | `lib/ay8910.fs` | melody on the hardware envelope, bass, noise hats |
| 5 | Symphony-12 (four AY-3-8910s) | `lib/ay8910.fs` | stereo: left melody, bass, chord, beat hats; right melody an octave up, bass, chord an octave up, off-beat hats |
| 6 | Orchestra-90CC (two 8-bit DACs) | `lib/orch90.fs` | triangle-wave melody left, bass right |
| A | all six in turn | | |

BREAK stops a section, and quits at the menu.

`tour.py` holds the phrase (two four-bar phrases over a C3 / B2 bass and
the chords I and V) and turns it into each device's tables, which
`sound-tour.fs` splices in with `DATA[PY`. The 1-bit and DAC players are
timed by their own loops, so their pitches come from cycle counts
measured with `fc.py --cycles`: `snd-tone1` 42 + 14p cycles a period
(worst note 2.7 cents out), `snd-saw` 36 + 7p cycles a sample (8.3
cents), `orch-play` 79 cycles a sample (11328 Hz). The chips divide
their own clocks: 4 MHz for the SN76489, 2 MHz for the CoCo PSG and
894886 Hz for the Symphony-12.

Stock XRoar has no CoCo PSG or Symphony-12, so keys 4 and 5 say NO COCO
PSG FOUND and NO SYMPHONY-12 FOUND there; play them on a CoCo Zero.
They check first (`ay-found?` in `lib/ay8910.fs` writes a register and
reads it back) because without the chip the GameMaster in `make run`'s
Multi-Pak takes the CoCo PSG's odd addresses, and the AY data came out of
its SN76489 as noise. The only AY in stock XRoar is the Deluxe CoCo
machine's, which needs ROMs this setup does not have.

Not in the tour, as nothing can drive them yet: the OPL3 on the MEGA mini MPI (#580, and not in the CoCo
Zero), Tandy's Speech/Sound Cartridge (not emulated yet, PIZERO-249), and
the Deluxe CoCo's AY-3-8913 (off by default on the CoCo Zero).

fc.py strips anything in parentheses as a comment, even inside `."`
strings, so the menu text avoids them.
