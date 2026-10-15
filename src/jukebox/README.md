# Jukebox (#588)

Lists the `.SNG` songs on the disk in drive 0, loads the one you pick and
plays it on the SN76489 (GameMaster cart).

```
make run    # builds jukebox.bin and build/songs.dsk, then starts XRoar
            # with a Multi-Pak: disk controller in slot 0, GameMaster in slot 1
```

- Letter: play that song. BREAK at the menu: quit to BASIC.
- While playing, BREAK goes back to the menu. When a song ends, any key
  plays it again.

How it fits together:

- `lib/decb.fs` reads the disk directly through the WD17xx (no Disk BASIC
  calls). `dk-entry` walks the directory for the menu; `dk-open-fname` and
  `dk-load-open` pull the chosen file into `$5A00` (at most `$1E00` bytes).
- `lib/sn76489.fs` plays it. A `.SNG` file is a raw song stream from
  `src/sound-sn76489/sn76489.py`, no DECB header, and is relocatable.
- The disk controller and the GameMaster both decode `$FF40-$FF5F`, so
  the player flips the Multi-Pak I/O slot at `$FF7F` (`>disk`, `>sound`).
  The ROM slot stays on the disk cart. On a real Multi-Pak with other slot
  numbers, change the `disk-io` and `sound-io` constants.
- Add songs by copying more `.SNG` files into `build/songs.dsk` (see the
  Makefile). Up to 12 are listed.
