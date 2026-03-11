# CoCo Forth Tetris

A Tetris game for the TRS-80 Color Computer, built on the CoCo Forth kernel using semigraphics-4 mode. Each Tetris block is one SG4 character cell with all four sub-pixels lit in the piece's color.

## Screen Layout

The 32x16 VDG text display is split into two regions:

- **Columns 0-9**: 10-wide x 16-tall playfield (shorter than standard 20, fits the screen)
- **Column 10**: vertical border
- **Columns 12-31**: info panel showing score, lines, next piece preview

## Piece Colors

The 7 standard Tetris pieces each use a distinct SG4 color:

| Piece | Color |
|-------|-------|
| I | Yellow |
| O | Blue |
| T | Red |
| S | Buff |
| Z | Cyan |
| L | Magenta |
| J | Orange |

## Controls

| Key | Action |
|-----|--------|
| Left arrow | Move left |
| Right arrow | Move right |
| Down arrow | Soft drop (reset gravity timer) |
| Up arrow | Rotate clockwise |
| Space | Hard drop (instant lock) |

Delayed auto-shift: 12-frame initial delay, 4-frame repeat rate.

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile Tetris
cd src/tetris && make

# Run in XRoar
make run
```

## Implementation Notes

### Memory Map

- `$3000`-`$30FF`: Board state array (16-byte stride x 16 rows, cols 10-15 unused padding)
- `$3100`-`$316F`: Piece rotation table (7 pieces x 4 rotations x 4 bytes)

### Piece Encoding

Each rotation is 4 bytes, one per block. Each byte packs a column/row offset as `dx*4 + dy`. Decode with `4 /MOD SWAP` to get `( dy dx )`. Maximum offset is 3 in either axis, so one byte per block suffices.

### Shared Libraries

This demo uses shared Forth libraries from `forth/lib/`:

- `rng.fs` — 16-bit LCG random number generator
- `screen.fs` — vsync synchronization, screen clearing
- `print.fs` — number printing utilities
