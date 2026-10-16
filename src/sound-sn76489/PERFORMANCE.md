# Performance files (.perf)

The MIDI files we import (Mutopia) are flat: one tempo for the whole
piece and the same loudness on every note. A performance file adds the
interpretation: how fast, where it broadens, where it is loud or soft,
and which voice leads. `midi2sng.py` reads `NAME.perf` beside
`NAME.mid` and bakes the result into the `.SNG` stream, so the
player needs nothing new (#609).

In coco the Badinerie is the one song left (`badinerie.perf`). The 95-song
Bach library that this layer was built for, with a `.perf` for every
piece, lives in its own project, ~/github/coco-fm-bach (#627).

**Every song ships with one** (#614). A piece added to the library gets
the same respect as the rest: its tempo researched from respected
recordings, its structure read from the score, and its dynamics,
voicing and timing shaped from that, with the reasons in the file's
comments. `midi2sng.py` refuses a song without a performance file;
`--draft` builds one anyway, for trying an import out before the
research is done. The Badinerie, written by hand, uses the same layer
through `badinerie.perf`.

## Format

Plain text, one command per line, `#` starts a comment. Bars count from
1 as in the score; `12.5` is halfway through bar 12, and a range
`12-16` covers bars 12 to 16 whole.

| Command | Example | Meaning |
|---------|---------|---------|
| `tempo BPM [UNIT]` | `tempo 60 4.` | The basic tempo, replacing the MIDI's. UNIT is the note that gets the beat: `2.` `2` `4.` `4` (default) `8.` `8` `16.` `16`. |
| `rit BARS END` | `rit 2 0.7` | Final ritardando: over the last BARS bars the tempo eases down to END times the basic tempo. `rit 0` turns it off. |
| `broaden RANGE FACTOR` | `broaden 12 0.9` | Hold back (below 1) or press on (above 1) over those bars, e.g. into a cadence. |
| `dyn BAR LEVEL` | `dyn 9 p` | From that bar on, play at LEVEL: `pp` `p` `mp` `mf` `f` `ff`. A mark at bar 1 also covers a pickup before it. |
| `ramp RANGE LEVEL` | `ramp 20-24 f` | A hairpin: from the level at the start of the range, move evenly to LEVEL by its end. |
| `voice VOICE STEPS` | `voice mid 1` | A standing balance for the top, mid or bottom voice, in 2 dB steps (positive is softer). |
| `lead RANGE VOICE` | `lead 1-2 top` | Bring that voice forward one step over those bars, e.g. while it has the subject. |
| `accent STEPS` | `accent 1` | Every note on a downbeat this many steps louder. |
| `pickup UNIT` | `pickup 8` | The piece starts with an upbeat of this note value, so bar 1 is the first full bar, as in the score. |
| `play RANGE...` | `play 1-8 1-8 9-16 9-16` | Play these stretches of the score in this order: written-out repeats, first and second endings, or one movement or section on its own (`play 1-29`). A whole bar number includes that bar; a fraction stops at that point (`0.75-8.75` for a repeat that begins and ends on a quarter upbeat). Needs `tempo`. |
| `parts TRACK...` | `parts 3 4 1 2` | The MIDI file's tracks, most important first (track 0 is Mutopia's control track). When four notes sound at once, the least important part gives way instead of the middle pitch, so a melody that sits inside the texture survives. |

A `--draft` build without a file gets `mf`, the middle voice one step
back, and a gentle final ritardando (the last 2 bars to 0.75, or the
last 4 when a bar is shorter than 3 beats).

## How it maps onto the chip

- **Levels** are the chip's attenuation for a note's start: `ff` 0,
  `f` 1, `mf` 2, `mp` 3, `p` 4, `pp` 5, then voice, lead and accent
  adjust that. Every step is 2 dB. The player's envelope then decays the
  note toward its channel's floor, so a soft note has less room to fade.
- **Voices** are worked out per note from what else is sounding when it
  starts: the highest pitch is `top`, the lowest `bottom`, anything
  between `mid`. In a three-part piece that is soprano, alto and bass.
- **Bars after `play`**: every other command counts bars along the
  performance, starting from the first range's bar, not the score.
  With `play 1-8 1-8 9-16 9-16`, bar 9 is the repeat of bar 1 and bar
  17 is the score's bar 9, as the Badinerie's file does. An excerpt
  (`play 30-60`) keeps the score's numbers.
- **Tempo** shaping moves the waits between frames, so it is exact to a
  60 Hz frame (about 17 ms).

## Principles we follow

Baroque keyboard music on a harpsichord or organ has no crescendo within
a note, so Bach's own dynamics are *terraced*: whole passages louder or
softer, by changing manual or registration, usually at cadences and
section boundaries. The shaping here keeps to that:

- Set dynamics by section, changing at cadences, not note by note.
  Ramps are for the longer arcs (building into the final section), and
  are used sparingly.
- Echo a repeated phrase softer, as on a second manual.
- Bring the subject forward where it enters, so the counterpoint reads.
- Broaden only into real cadences and at the end; keep the pulse steady
  elsewhere (Bach's dance and motor rhythms depend on it).
- Choose the tempo from the piece's character and its time signature,
  guided by how respected recordings take it, not from the MIDI's
  default.
