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
| `midi2sng.py`  | MIDI file to the same stream, and the performance layer the Badinerie uses. The Bach library it converted lives in ~/github/coco-fm-bach (#627) |
| `sng2vgm.py`   | A .SNG song rendered to a VGM file, logged for a 3.58 MHz chip: test material for `lib/vgm.fs` (#598, checked by `src/vgm-test`) |
| `SNG-FORMAT.md` | The song stream (.SNG) format spec |
| `PERFORMANCE.md` | Performance files (`NAME.perf`): tempo shaping, dynamics and voicing (#609) |
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

## Retrospective: transcribing and performing the Badinerie

The Badinerie was the project's first song (#579, 2026-10-14), and the
only one written out by hand. Everything after it came from MIDI files,
so the lessons here shaped the whole song library that followed.

### Why by hand

The plan was to fetch it from Mutopia, but Mutopia's Bach collection
has no BWV 1067 (it skips from BWV 1056 to the Third Suite), and a
search for "Badinerie" finds nothing. The public-domain Bach-Gesellschaft
edition on IMSLP was the source instead: two printed pages, flute,
two violins, viola and continuo. It was reduced to the three lines that
carry the piece, one per tone channel: the flute, violin II as the
inner line, and the continuo.

### Reading the score

The music itself was the easy part: the rhythms, the harmony and the
figured bass. Reading pitches off a scan was the hard part. Telling
whether a notehead sits on a line or in the space beside it, and
counting ledger lines, went wrong by eye: the first pass had bar A8 as
E5 E5 (it is F sharp F sharp) and an A2 in the bass of A3 that is a
C sharp 3. So a small script found the noteheads and measured each
against its nearest staff lines. It only became reliable once we saw
that page 16 is scanned with a curve, which pushed notes on the left of
the page down a step. Ledger-line notes were then checked one at a time.

What we learned: a square wave makes a wrong note obvious on the first
listen, and the text score (`badinerie.py`, one string per voice, e.g.
`f#5:8 b5:16 f#5`) makes fixing one a single-token edit. That pairing,
an ear on the chip and an editable text score, is what made a hand
transcription practical at all. Paul's verdict: one of the better
transcribed tracks in the collection.

### Editorial choices

- Trills (A8, A10, B11, B14, B16) are played as plain notes. Realising
  them as short alternations is still open (#582).
- The grace note in B17 is a long appoggiatura, half the note it leans
  on.
- The flute's C in B8 has no natural sign in the print, but the bass
  beneath it is a marked C natural under a 4/2 figure (D7 over C), so a
  C sharp would clash. The flute plays C natural.
- 42 continuo notes fall below B2, the chip's lowest tone at 4 MHz, and
  are lifted an octave. A real bass down there is #617.

### How it needed to be performed

- **Not a race.** "Badinerie" means a jest. It is the suite's last
  movement, a quick dance in 2/4, and it has to sparkle over a steady
  motor pulse. Pinnock and the English Concert take about 1:32 (quarter
  about 105); many period bands go faster. We settled on quarter = 112:
  a little quicker than Pinnock, and slow enough that every sixteenth
  is 8 frames and the thirty-seconds of B17, B18 and B22 still get 4.
  At 60 Hz, a tempo is really a choice of how many frames each note
  gets, and below 4 a run starts to smear.
- **The flute leads.** The flute sits on top at full level, the
  continuo two steps (4 dB) back, and violin II, the inner filler,
  three steps back. With all three square waves at one level the inner
  line crowds the melody.
- **Repeats, with an echo.** Both halves are repeated, as Bach wrote.
  The first version used the stream's repeat bytes, which can only play
  a passage the same way twice. Writing the repeats out (A A B B, 6.5 KB
  instead of about 3) let the second time through be an echo, mp after
  f, the way a harpsichordist changes manual or a period band drops
  back. The full sound returns in the B repeat at the E pedal (B17) for
  the close.
- **Terraced, not swelled.** Dynamics change at the cadences between
  sections, never note by note, in keeping with the instruments Bach
  wrote for.
- **A modest close.** The last two bars broaden to 0.85 of the
  tempo into the final cadence; any more and a dance turns into a
  farewell. It plays for 1:26.
- **The drums.** Bach wrote no percussion; the light pulse on the noise
  channel (kick on the beat, hats on the other eighths) is an
  arrangement choice for a chip with a spare channel. The first version
  ran each hit through the player's decay, about 100 ms of hiss per hat
  and 200 ms per kick. Paul: "reminiscent of children marching through
  autumn leaves". Brighter noise was not the answer (the hats were
  already on the brightest setting); shorter was. Each hit is now cut
  off after 2 frames (hats) or 3 (the kick) (#590), and the drums
  follow the dynamics, so they echo with the rest.
- **Plucked, not held.** The player decays every note toward a
  per-channel floor, so the flute's runs articulate the way a flute's
  tongued notes do, instead of droning like an organ.

### What it led to

The performance layer (#609) and the rule that every song ships with a
researched performance (#614) grew out of this piece: `badinerie.perf`
was the first time the shaping was written down with its reasons, and
the `pickup` command exists because the Badinerie's halves both start on
a quarter-note upbeat. The `.SNG` format (`SNG-FORMAT.md`), the chip
library (`lib/sn76489.fs`), the replay prompt ("play it again, Sam",
#585) and the COCO.FM jukebox all started as this one song. The 95-song
Bach library that followed lives in ~/github/coco-fm-bach; the
Badinerie stays here as the demo.
