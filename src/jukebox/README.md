# Jukebox: COCO.FM (#588, #599-#602)

A laid-back radio player for the SN76489 (GameMaster cart, or the CoCo
Zero's built-in chip). It plays every `.SNG` song on the disks in
drives 0 to 3, one after another, and wraps around to the first. The screen is a
90s MP3 player in CG2 (128x64, colour set 0: green, yellow, blue, red).

```
make run    # builds jukebox.bin and the four song disks, then starts
            # XRoar with a Multi-Pak (disk controller in slot 0,
            # GameMaster in slot 1) and the disks in drives 0 to 3
```

The library is four disks (#612, #620), 95 songs: `songs.dsk` holds
the player, the Badinerie, the Air, the Prelude, the Inventions, the
Sinfonias and the Duetti; `fugues.dsk` holds the fugues (#611), the
Well-Tempered Clavier preludes and the organ trio sonata; `bach3.dsk`
holds the Toccata, Sheep May Safely Graze, the Anna Magdalena pieces,
more Inventions and suite dances; `bach4.dsk` holds cello, flute,
violin and lute movements, partita movements and chorales and songs. On a CoCo Zero, put them in drives 0 to
3 from the F12 menu. Fewer disks work too: the jukebox plays whatever
it finds.

It runs like a radio station (#623): no fixed order. Each round plays
every song once, shuffled, so the popular pieces come round no more
often than the rest (95 songs is about three and a half hours). The
last quarter of a round is kept out of the next round's opening
quarter, and two songs from one set (Inventions, Sinfonias, one cello
suite...) do not follow each other where that can be helped. The
shuffle is seeded from the disk timing and from when keys are pressed;
on an emulator or a CoCo Zero, an untouched power-on can open the same
way each time. TRACK shows how far through the round it is.

Keys follow Winamp:

| Key | Does |
|-----|------|
| Z | previous song in the round |
| X | play (from the start if already playing; resumes when paused) |
| C | pause / resume |
| V | stop, back to the start of the song |
| B | next song in the round |
| left / right | back / forward 5 seconds |
| up / down | volume (hold to repeat) |
| M | switch colour theme (see Colours) |
| BREAK | quit to BASIC |

The screen, top to bottom: the COCO.FM title bar; the play state, a big
clock, the song length and four level bars (three tones and noise); the
title, scrolling Winamp style when it is wider than 32 characters; the
composer; a progress bar; the track number and a volume bar; the
transport buttons with their keys. Rows 61-63 stay empty because XRoar's
window and a TV's overscan crop them.

## Colours

The screen draws with four roles: background, bright (clock, title, lit
button, bar fills), soft (labels, level bars, unlit buttons) and frame
(title bar, progress and volume tracks).

- **CoCo 3 or CoCo Zero:** both have palette registers at `$FFB0-$FFBF`.
  The player writes colour set 0 as a dark Winamp night (navy, warm
  yellow, LCD green, slate) and colour set 1 as a Poolside FM afternoon
  (aqua, hot pink, deep blue, pastel pink), and M flips between them.
  The background is pixel value 0, the border colour, so the screen
  runs edge to edge. The colours are GIME RGB values (the `themes` table
  in `jukebox.fs`); a CoCo 3 on a composite TV shows different ones.
  BREAK puts the palette back as it was, or, on a CoCo Zero nothing had
  written, its stock colours (approximately: a reset restores them
  exactly, including any `color_` settings).
- **Stock CoCo 1/2:** no palette, so the roles move onto the VDG's
  fixed colours instead (blue background in set 0, magenta in set 1),
  and M flips the VDG colour set.

`make run3` runs the player on an emulated CoCo 3 with an RGB monitor.

How it fits together:

- `lib/decb.fs` reads the disk directly through the WD17xx (no Disk BASIC
  calls). `dk-entry` walks the directory; `dk-open-fname` and
  `dk-load-open` pull a song into `$5800` (at most `$2500` bytes,
  #618). The song list lives at `$0E00`, above the CG2 screen.
- `lib/sn76489.fs` plays it. Songs are `.SNG` streams
  (`src/sound-sn76489/SNG-FORMAT.md`); a v2 song's header gives the
  title, composer and length. `sn-atten` is the volume and `sn-skip`
  does the seeking: forward runs on from where it is, back restarts the
  song and runs up to the new time, since a stream only goes forward.
- `lib/cg-tiny.fs` draws the text: a 3x5 font in a 4x6 cell, one CG byte
  per glyph row, and the same glyphs doubled for the clock and buttons.
- The song steps from the 60 Hz field-sync interrupt (`sn-irq-on`,
  #604), so its timing never depends on the screen. The main loop waits
  for each tick (`sn-vsync`), reads the keys, nudges the level bars and
  does at most one bigger redraw (clock, progress bar, buttons, or one
  step of the scrolling title). The interrupt is off while the disk is
  in use, and the player is held (`sn-hold`) around seeks and pauses.
- The disk controller and the GameMaster both decode `$FF40-$FF5F`, so
  the player flips the Multi-Pak I/O slot at `$FF7F` (`>disk`, `>sound`).
  The ROM slot stays on the disk cart. On a real Multi-Pak with other slot
  numbers, change the `disk-io` and `sound-io` constants. On a CoCo Zero
  `$FF7F` is open bus and both answer without it.
- At start it checks each drive for a turning disk (`dk-ready?` watches
  the index pulse, since the controller's READY line is tied on, or
  asks for a sector where the controller has no pulse, #619), reads
  the directories of those that have one, and remembers each song's
  drive. That takes a few seconds. `dk-select` changes drive without
  stopping the motor.
- Add songs by copying more `.SNG` files onto either disk (see the
  Makefile). Up to 99 are played; a disk holds about 30 small songs.
