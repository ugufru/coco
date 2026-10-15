# The .SNG song format

A `.SNG` file is a song for the SN76489 sound chip (the GameMaster
cartridge, and the CoCo Zero's built-in chip), stored as a timed list of
chip commands. This is the format `lib/sn76489.fs` plays, `sn76489.py`
and `midi2sng.py` write, and `src/jukebox` loads from disk (#597).

Version: 2. A v2 file is a v1 stream with a short header in front
(title, composer, length); a v1 file has no header and no version mark.
Players read both (#600).

## At a glance

- A flat stream of bytes, with an optional header (v2) for the title,
  composer and length. No checksum.
- The player reads it one 60 Hz video frame at a time.
- Each byte is either a **wait**, a **chip command**, or one of two
  **repeat markers**, and `$00` ends the song.
- It contains no addresses, so a song can be loaded anywhere in memory.

## The v2 header

A v2 file starts with `$EA`, a byte reserved in v1 and so never the
first byte of a v1 file:

| Offset | Size | Meaning |
|--------|------|---------|
| 0 | 1 | `$EA`: this file has a header |
| 1 | 1 | header version, 2 |
| 2 | 2 | length in frames, big-endian, unsigned, repeats included (up to about 18 minutes) |
| 4 | 1+n | title: a length byte, then n printable ASCII characters (n up to 63) |
| | 1+m | composer: the same, may be empty |
| | | the v1 stream follows |

`sn-start` skips the header, so a v2 song plays anywhere a v1 song does.
`lib/sn76489.fs` reads it with `sng-v2?`, `sng-frames`, `sng-title`,
`sng-composer` and `sng-body`. The tools work the length out by walking
the stream (`sn76489.frames()`), so it always matches what plays.

Real start of `AIR.SNG`:

```
EA 02               v2 header
1E 9F               7839 frames: 130.7 s
29 "Air from Orchestral Suite No. 3, BWV 1068"
0A "J. S. Bach"
89 0A ...           the stream
```

## Byte codes

| Byte | Name | Meaning |
|------|------|---------|
| `$00` | end | The song is over. |
| `$01-$7F` | wait | Stop reading for this many frames, 1 to 127 (up to about 2.1 s). A longer pause is several waits in a row. |
| `$E8` | mark | Remember the position just after this byte as the start of a repeat. |
| `$E9` | repeat | If a repeat is owed, jump back to the mark once. Otherwise carry on. |
| any other `$80-$FF` | command | An SN76489 command byte; see below. Tone commands take one data byte after them. |

## Chip commands

Command bytes use the chip's own register encoding:

```
  bit   7   6 5   4   3 2 1 0
        1   c c   t   d d d d
            |     |   +------ 4 bits of data
            |     +---------- 0 = tone / noise setting, 1 = volume
            +---------------- channel: 0, 1, 2 = tone, 3 = noise
```

| Bytes | Command | Data | What the player does |
|-------|---------|------|----------------------|
| `$80-$8F` `$A0-$AF` `$C0-$CF` | tone, channel 0 / 1 / 2 | low 4 bits of the divider; **the next byte** (`$00-$3F`) holds the top 6 bits | sends both bytes to the chip |
| `$E0-$E7` | noise control | bit 2: 1 white, 0 periodic. Bits 1-0: rate 0 = N/512, 1 = N/1024, 2 = N/2048, 3 = follow tone channel 2 | sends it to the chip |
| `$90-$9F` `$B0-$BF` `$D0-$DF` `$F0-$FF` | volume, channel 0 / 1 / 2 / 3 | attenuation, 0 loudest to 15 off, 2 dB a step | **does not** send it; sets that channel's envelope level (see below) |
| `$EA-$EF` | (reserved) | | `$EA` as the first byte of a file marks the v2 header; anywhere in the stream these are sent to the chip as noise control, so don't use them |

### Pitch

The chip runs at 4 MHz, so a tone channel plays

```
frequency = 4000000 / (32 * N)        N = 125000 / frequency
```

with `N` a 10-bit divider (1 to 1023). It is split across the two bytes:

```
command = $80 | channel << 5 | (N & $0F)
data    = N >> 4                       ($00-$3F)
```

The lowest note, `N = 1023`, is about 122 Hz (B2). The tools lift
anything lower up an octave and say how many notes they lifted.

### Volume and the envelope

A volume byte sets the channel's **level**. Every frame the player then:

1. lets each channel's level fade one step quieter, if that channel's
   fade is due this frame and it hasn't reached its floor; then
2. writes all four levels to the chip.

So a note starts at the level its volume byte gave and fades toward the
floor, which makes notes pluck rather than drone. A volume of 15 is a
note off: it is already quieter than any floor, so it stays off.

The fade settings live in the player, not the song. They are byte
tables in `lib/sn76489.fs`, which a program can change before playing:

| Table | Meaning | Default (ch 0, 1, 2, noise) |
|-------|---------|------------------------------|
| `sn-dmask` | fade when `frame AND mask = 0`: 0 every frame, 1 every 2nd, 3 every 4th | 1, 3, 3, 0 |
| `sn-floor` | level the fade stops at | 8, 10, 8, 15 |
| `sn-lvl` | the current levels (the player resets them to 15) | |

The noise channel fades every frame all the way to silence, so a drum
hit dies away by itself. A song can also end a hit early with an
explicit `$FF`.

## Timing

- The player steps once per 60 Hz frame: `sn-frame` called on vsync
  (`sn-play` does this), or the field-sync interrupt handler that
  `sn-irq-on` installs, which keeps the steps even however busy the
  program is (#604).
- On a frame where the wait has run out, the player handles every
  command up to the next wait byte. All the commands between two waits
  take effect on the same frame.
- The first frame reads straight away. The stream starts with commands
  for frame 0, or with a wait if the song opens on a rest.
- Because everything is whole frames, tempo is quantised to 1/60 s.
  The Badinerie uses 4 frames per 32nd note, so a quarter note is
  112.5 bpm.

The tools write each frame's commands in a fixed order: note offs, then
note ons, then noise. Within a note on, the tone comes before the volume.
The format doesn't require this order, but it stops an old note from
cutting off a new one.

## Repeats

`$E8` and `$E9` let a section play twice without storing it twice:

```
$E8  [section]  $E9
```

- `$E8` records the position after itself and sets "one repeat owed".
- `$E9` with a repeat owed clears it and jumps back to the mark. The
  second time it is reached, nothing is owed, so playback carries on.
- One level only. Repeats can follow one another (A A B B) but not nest:
  a new `$E8` replaces the old mark.
- Both codes are noise-control bytes with bit 3 set, which the chip
  ignores, so they can never be mistaken for real noise settings.

## Ending

`$00` ends the song: `sn-frame` returns true and `sn-play` silences the
chip. The tools put a short wait (`$20`, about half a second) before the
end so the last notes can ring out.

## Size and placement

- **Relocatable:** the player keeps a pointer into the stream and takes
  the repeat mark while playing, so the song can sit at any address.
  `badinerie.fs` builds it into the program with `DATA[PY`, and the
  jukebox loads it from disk.
- **No size field:** the end is the `$00` byte. A loader takes the file
  length from the filesystem.
- **Jukebox limit:** the jukebox loads songs at `$5A00` and accepts up
  to `$1E00` (7680) bytes. `midi2sng.py` refuses to write anything
  larger. As a guide, the Badinerie is 3222 bytes for 86 seconds.
- **On disk:** any DECB file with the extension `SNG`, stored as a binary
  file (type 2). The 8-character name is what the jukebox menu shows.

## A worked example

The first bytes of the stream in `BADINERI.SNG`, after its header
(`EA 02 14 42`: 5186 frames, 86.4 s, then the title and composer):

```
8F 07               ch0 tone N = $07F (127)   984 Hz, B5 (flute)
91                  ch0 volume 1
A9 0A               ch1 tone N = $0A9 (169)   740 Hz, F#5 (violin)
B4                  ch1 volume 4
C4 3F               ch2 tone N = $3F4 (1012)  123 Hz, B2 (bass)
D3                  ch2 volume 3
0F                  wait 15 frames
9F BF DF            ch0, ch1, ch2 off (a one-frame gap so notes re-strike)
01                  wait 1 frame
8A 06               ch0 tone N = $06A (106)   1179 Hz, D6
91                  ch0 volume 1
...
```

The volumes come from its performance file (`badinerie.perf`): the
flute forward, violin II three steps back, the bass two. Its repeats are
written out rather than marked with `$E8`/`$E9`, so the second time
through each half can be played as an echo. The last bytes:

```
9F BF DF            all three tone channels off
01                  wait 1 frame
20                  wait 32 frames to let it ring
00                  end
```

## Making and playing songs

- `sn76489.py`: `parse()` reads a hand-written text score (one string per
  voice, e.g. `b5:16 f#5 d5:8.`), `stream()` builds the bytes and
  `header()` adds the v2 header. See `badinerie.py` for a full score.
- `midi2sng.py in.mid out.sng [--speed X] [--transpose N] [--title T]
  [--composer C]`: converts a Standard MIDI File, putting notes on the
  three tone channels, and writes the v2 header. Titles come from
  `midi/TITLES.txt` by file name. `make songs` converts everything in
  `midi/`.
- `lib/sn76489.fs`: `addr sn-play ( -- brk? )` plays a song to the end,
  or call `addr sn-start`, then `sn-frame` once per frame. `n sn-skip`
  jumps ahead n frames without playing them (for seeking), and
  `sn-atten` is a master volume.

## Known limits (ideas for a version 3)

- No envelope settings in the file: every song uses the player's
  tables, so held notes (the Air) pluck when they should sustain. A
  later header version could carry them.
- Pitch stops at B2, and tempo is quantised to 60 Hz frames.
