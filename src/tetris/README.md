# Bare Naked Tetris

A full Tetris game for the TRS-80 Color Computer, built on the CoCo Forth kernel using SG4 semigraphics. Real-time game loop with zero-flicker rendering, collision detection, line clearing, scoring, high scores, and difficulty progression — all in ~555 lines of Forth.

## Screen Layout

The 32×16 VDG text display is split into two regions:

```
Col: 0         1         2         3
     01234567890123456789012345678901

     ┃          ┃
     ┃ playfield┃ BARE NAKED
     ┃ 10 wide  ┃ TETRIS
     ┃          ┃
     ┃          ┃ SCORE
     ┃          ┃ 1200
     ┃          ┃
     ┃          ┃ LINES
     ┃          ┃ 8
     ┃          ┃
     ┃          ┃ NEXT    HIGH
     ┃          ┃ ░░░░    SCORES
     ┃          ┃ ░░░░    1) 3200
     ┃          ┃         2) 1800
     ┃          ┃         3) 900
     ┃          ┃         4) 400
```

- **Columns 0 and 11**: vertical border (SG4 block `$CF`)
- **Columns 1–10**: 10-wide × 16-tall playfield (shorter than standard 20, fits the VDG)
- **Columns 13–31**: info panel — title, score, lines, next piece preview, high scores

All in-game text uses `vemit` (green on black, `$3F AND char`) instead of `EMIT` (inverse video) so the background stays black.

## Controls

| Key | Action |
|-----|--------|
| Left arrow | Move left |
| Right arrow | Move right |
| Down arrow | Soft drop (resets gravity timer) |
| Up arrow | Rotate clockwise |
| Space | Hard drop (instant lock) |

Delayed auto-shift provides arcade-style key repeat: 12-frame initial delay, then 4-frame repeat rate.

## Piece Colors

The 7 standard Tetris pieces each use a distinct SG4 color. Buff (color 4) is skipped because it looks white on the VDG and doesn't read well against the black background.

| Piece | Color | SG4 code |
|-------|-------|----------|
| I | Yellow | 1 |
| O | Blue | 2 |
| T | Red | 3 |
| S | Cyan | 5 |
| Z | Magenta | 6 |
| L | Orange | 7 |
| J | Red | 3 |

Each SG4 cell is rendered as `$80 | (color << 4) | $0F` — all four sub-pixels lit in the piece's color. Empty cells are `$80` (black, no sub-pixels).

## How It Works

### Piece Encoding

All 7 pieces × 4 rotations × 4 blocks = 112 bytes, stored at `$4100`. Each byte encodes one block's offset from the piece origin as `dx*4 + dy`. To decode: `4 /MOD` yields `( dy dx )` — dx on top of the stack, ready to add to the piece's column. Maximum offset is 3 in either axis, so one byte per block suffices.

### Zero-Flicker Rendering

The most interesting technique in the codebase. Moving a piece normally requires erasing the old position then drawing the new one, which causes visible flicker on the VDG. Instead, Tetris uses a **draw-first, clean-stale** pattern:

1. **`save-dirty`** — Before any move, record the 4 VRAM addresses of the current piece position (`d0`–`d3`)
2. Move the piece (update `px`/`py`/`pr`)
3. **`save-new`** — Record the 4 VRAM addresses of the new position (`n0`–`n3`) and precompute the SG4 byte
4. **`draw-new`** — Write the piece at its new position immediately
5. **`clean-dirty`** — Erase only the old cells that are NOT also occupied by the new position (checked by `is-new?`)
6. **`snap-new-to-dirty`** — Copy new addresses to dirty for the next frame

Because the new position is drawn before the old is erased, the piece is always visible — never absent for even a single frame.

### Collision Detection

`can-place?` decodes all 4 blocks of the current piece, checks each against the board bounds (0–9 columns, 0–15 rows) and the board array. Uses a flag variable (`ck-ok`) — any failing block sets it to 0. Movement words speculatively update position, call `can-place?`, and revert if it returns false.

### Line Clearing

Uses a cascade algorithm: scan all 16 rows for a full row, copy everything above it down by one row, clear the top row, and repeat the scan from the beginning. This naturally handles multi-line clears — each removal shifts the board and the scan restarts, catching any newly-adjacent full rows.

### Gravity & Difficulty

Gravity starts at 30 frames per drop and gets faster as lines accumulate: speed decreases by 1 frame for every 5 lines cleared, with a minimum of 3 frames per drop. The formula: `30 - (lines / 5)`, clamped to 3.

### Delayed Auto-Shift

Arcade-style key repeat: when a key is first pressed, it fires immediately, then waits 12 frames before repeating at 4-frame intervals. Releasing the key resets the timer.

### RNG Seeding

The title screen counts vsync frames in a tight loop until the player presses a key. The accumulated frame count becomes the LCG seed, giving a different piece sequence every game with zero extra hardware.

## Memory Map

| Address | Contents |
|---------|----------|
| `$4000`–`$40FF` | Board state array (16-byte stride × 16 rows, cols 10–15 unused padding) |
| `$4100`–`$416F` | Piece rotation table (7 pieces × 4 rotations × 4 bytes = 112 bytes) |
| `$4200`–`$4209` | High score table (5 entries × 2 bytes) |
| `$4210`–`$4216` | Piece color table (7 bytes, maps piece index to SG4 color) |

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run Tetris
cd src/tetris && make run
```

## Shared Libraries

This demo uses shared Forth libraries from `forth/lib/`:

- `rng.fs` — 16-bit LCG random number generator
- `screen.fs` — vsync synchronization, screen clearing
- `print.fs` — number printing utilities
