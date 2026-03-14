# Bare Naked Space Warp — Technical Specification

## Overview

Bare Naked Space Warp is a reimplementation of Joshua Lavinsky's Space Warp
(1980, Personal Software / Radio Shack) for the TRS-80 Color Computer, written
in Bare Naked Forth. The original was a 4K Z-80 machine language game for the
TRS-80 Model I — one of the first real-time action games on a home computer,
predating Atari's Star Raiders by a year.

This version preserves the original gameplay while taking advantage of the
CoCo's color graphics hardware.

## Original Game

Space Warp is the Radio Shack-branded version of Time Trek (1978), written by
Joshua Lavinsky of Connan Enterprises and published by Personal Software (later
VisiCorp). The Star Trek names were changed for trademark reasons:

| Time Trek | Space Warp |
|-----------|-----------|
| Enterprise | Endever |
| Klingons | Jovians |
| Federation starbases | United Planet System bases |
| Phasers | Masers |
| Photon torpedoes | Triton missiles |
| Warp engines | Hyperdrive |
| Impulse engines | Ion engines |
| Shields | Deflectors |

The original fit multiply, divide, cosine, RNG, line drawing, sound, and
dual-threaded game logic into 4096 bytes of Z-80 assembly. Reference materials
including the original design document, screenshots, and the Josh Lavinsky
interview are in `src/spacewarp/refs/`.

## Target Platform

- TRS-80 Color Computer (any model), 64K RAM
- Bare Naked Forth kernel + fc.py cross-compiler
- RG6 display mode with NTSC artifact coloring
- No ROM dependencies (bare-metal keyboard, no BASIC)

## Display: RG6 Artifact Color

The game uses RG6 mode (PMODE 4 equivalent) — technically 256x192 monochrome,
1 bit per pixel, 6144 bytes VRAM — but exploits NTSC artifact coloring to
produce 4 apparent colors at an effective 128x192 resolution.

### How Artifact Coloring Works

On NTSC composite video, adjacent pixel pairs in RG6 create color through
chroma signal interference. Treating each horizontal pixel pair as one color
unit:

| Bit pair | Artifact color |
|----------|---------------|
| `00` | Black |
| `11` | White |
| `10` | Blue |
| `01` | Red/orange |

The CSS bit (PIA $FF22 bit 3) swaps the blue/red artifact assignment.

### Color Assignments

| Game element | Artifact color | Bit pair |
|-------------|---------------|----------|
| Space (background) | Black | `00` |
| Endever (player ship) | Blue | `10` |
| UP bases | Blue | `10` |
| Masers / phasers | Blue | `10` |
| Jovians (enemies) | Red/orange | `01` |
| Enemy fire | Red/orange | `01` |
| Explosions | Red/orange | `01` |
| Stars and planets | Mixed (random) | `01`, `10`, `11` |
| Text (status panel) | White | `11` |
| Triton missiles | Blue (moving ball) | `10` |

Stars use random artifact patterns (blue, red, white) for a colorful starfield.

### RG6 Hardware Setup

```
VRAM base:  $0E00 (SAM F offset = 7)
VRAM size:  6144 bytes ($0E00-$27FF)
SAM V bits: 110 (V=6)
PIA $FF22:  $F0 (A*/G=1, GM2=1, GM1=1, GM0=1, CSS=0)
Bytes/row:  32
```

### Pixel Addressing

RG6 packs 8 pixels per byte (1 bit each). For artifact color, treat pairs:

```
Byte: [P0 P1] [P2 P3] [P4 P5] [P6 P7]
       pair0    pair1    pair2    pair3
```

Each pair is one artifact-color pixel at effective 128x192 resolution.
Byte address = VRAM_base + (y * 32) + (x / 4), where x is in artifact pixels
(0-127). Bit pair position = 3 - (x % 4), counting from the right.

## Screen Layout

The 256x192 pixel display (effective 128x192 artifact pixels) is divided into
two regions:

```
+--------------------------------------------------+
|                                                  |  Rows 0-143
|           TACTICAL QUADRANT VIEW                 |  128x144 artifact pixels
|                                                  |  (18 text rows)
|  Ship, enemies, bases, stars, weapon fire,       |
|  black holes (invisible), border                 |
|                                                  |
+--------------------------------------------------+
|  STARDATE 2451  QUADRANT 3,5  DEFLECTORS 100%   |  Rows 144-191
|  ENERGY 87%   MISSILES 8     CONDITION GREEN     |  128x48 artifact pixels
|               SOS-BASE 5 1   COMMAND?            |  (6 text rows)
+--------------------------------------------------+
```

The tactical view has a visible border (single-pixel white line). The status
panel displays ship state, position, and the command prompt.

The long range scanner and damage report replace the tactical view temporarily,
using the same screen area.

## Text Rendering in RG6

Text in the status panel and scanner display uses the existing font5x7.fs
bitmap font rendered into RG6 VRAM. Each font pixel becomes a `11` (white)
pair, and each background pixel becomes `00` (black). The rg-text.fs library
already supports this via the `rg-char` word.

Since artifact-color pixels are 2 bits wide, each 5-pixel-wide glyph occupies
10 real pixels (just over 1 byte). Character cells will be 12x8 artifact pixels
(6 real pixels wide minimum, with spacing). At 128 artifact pixels wide, this
gives approximately 10 characters per row in the status panel — tight, but
workable with abbreviations. Alternatively, text can be rendered at full 256x192
resolution (single-pixel white) for readability, accepting that text characters
won't show artifact color (white only).

## Game Architecture

### Dual-Thread Design

Following Lavinsky's original architecture, the game alternates between two
logical threads each frame:

1. **Endever thread** — processes player input, executes commands, draws ship
2. **Jovian thread** — AI decision-making, enemy movement, enemy fire

The SWITCH routine yields control between them. On the 6809, this maps
naturally to Forth's main loop calling alternating update words.

### Galaxy Model

- 8x8 grid = 64 quadrants
- Each quadrant byte packed: storm(1) | black_hole(1) | stars(3) | base(1) | jovians(2)
  - Stars: 0-5 per quadrant
  - Jovians: 0-3 per quadrant
  - Base: 0 or 1
  - Black hole: 0 or 1
  - Magnetic storm: 0 or 1

### Quadrant State

When the player is in a quadrant, expanded state tracks positions of all
objects:

- Endever position (x, y) — pixel coordinates within tactical view
- Starbase position (x, y)
- Star positions — up to 5, (x, y) each
- Jovian positions — up to 3, (x, y) each, plus damage level
- Black hole position (x, y) — invisible

### Ship Systems

Five systems, each with a damage percentage (100% = fully operational, 0% = destroyed):

| System | Key | Effect when damaged |
|--------|-----|-------------------|
| Ion engines | Arrow keys | Slower movement, eventually inoperable |
| Hyperdrive | 2 | Cannot warp to other quadrants |
| Scanners | 3 | Long range scan shows partial/no data |
| Deflectors | 4 | Maximum shield energy reduced |
| Masers | 5 | Reduced damage output |

### Energy Model

- **Ship energy** — depleted by movement, firing masers, taking hits. Recharged by docking.
- **Deflector energy** — set as percentage (0-100%). Higher shields absorb more damage but reduce maser power. 50% shields = 1/3 maser loss. 100% shields = 2/3 maser loss.
- **Triton missiles** — finite supply (10 at start), replenished only by docking. One-hit kill at any range.

### Time

- 1 stardate = approximately 1 real-time minute (60 VSYNC frames/sec x 60 = 3600 frames)
- Game clock advances continuously
- Jovians attack bases on a timer — SOS alerts warn the player

### Scoring

Score based on: fraction of Jovians destroyed, fraction of bases surviving,
stardates elapsed. Scale 1-250.

## Commands

| Key | Command | Input | Description |
|-----|---------|-------|------------|
| 1 | Damage Report | — | Show system damage %, Jovians/bases remaining |
| 2 | Hyperdrive | 2 digits (col, row) | Warp to quadrant (0-7, 0-7) |
| 3 | Long Range Scan | — | Show 8x8 galaxy map |
| 4 | Deflectors | 3 digits (0-100) | Set shield energy percentage |
| 5 | Masers | 3 digits (0-360) | Fire at angle, variable damage by distance |
| 6 | Triton Missiles | 3 digits (0-360) | Fire at angle, one-hit kill, limited supply |
| 7 | Self-Destruct | "123" + ENTER | Destroy ship and all Jovians in quadrant |
| Arrows | Ion Engines | Hold | Move within quadrant (real-time, concurrent) |

Arrow keys work at all times, including while entering other commands. This is
the key real-time element — you can dodge enemy fire while typing a maser angle.

## Difficulty Levels

10 levels, selected at game start:

| Level | Jovians | Description |
|-------|---------|------------|
| 1 | ~8 | Training mission |
| 5 | ~40 | Standard difficulty |
| 10 | ~89 | Nearly impossible |

Higher levels also increase Jovian aggression (fire rate, movement frequency,
tendency to attack bases).

## Docking

Move the Endever directly above or below a starbase to dock. Docking:
- Fully repairs all five ship systems
- Replenishes triton missiles to 10
- Restores ship energy to 100%
- Takes time (stardates pass during docking)

## Future Enhancements

These features are not in the initial implementation but are tracked as future
issues:

- **Sound effects** — maser fire, triton launch, explosions, docking, SOS alarm,
  self-destruct countdown (requires sound/DAC library, issue #36)
- **Star twinkling** — randomly toggle star pixel patterns each frame for a
  living starfield effect
- **Title screen** — animated starfield or ship graphic before game start
- **High score table** — persist across game restarts (like Tetris)

## Dependencies

### Existing Libraries
- `forth/lib/vdg.fs` — SAM/PIA mode switching
- `forth/lib/rng.fs` — random number generation
- `forth/lib/screen.fs` — vsync, cls
- `forth/lib/keyboard.fs` — KEY-HELD? for real-time arrow key input
- `forth/lib/font5x7.fs` — bitmap font for text rendering
- `forth/lib/rg-text.fs` — RG6 text rendering
- `forth/lib/datawrite.fs` — data table construction

### New Libraries Required
- **`forth/lib/rg-pixel.fs`** — RG6 artifact-color pixel primitives
  - `rg-pset ( x y color -- )` — plot artifact pixel (color 0-3: black/blue/red/white)
  - `rg-pcls ( -- )` — clear screen to black
  - `rg-hline ( x1 x2 y color -- )` — fast horizontal line
  - `rg-line ( x1 y1 x2 y2 color -- )` — Bresenham line drawing
- **`forth/lib/sprite.fs`** — software sprite library
  - Sprite data format for artifact-color bitmaps
  - `spr-draw ( addr x y -- )` — draw sprite (color 0 = transparent)
  - `spr-erase ( addr x y bg-color -- )` — erase to background
  - Background save/restore for clean overdraw
- **Cosine/sine table** — for angle-based weapon fire (lookup table, not computed)

### Test Application
- **`src/rg-test/rg-test.fs`** — test app for prototyping RG6 artifact graphics
  before building the full game. Exercises pixel plot, line drawing, sprites,
  and text rendering. Visual verification via XRoar with NTSC artifact display.
