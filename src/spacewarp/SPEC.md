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
| Explosions (hot) | White | `11` |
| Explosions (warm) | Red/orange | `01` |
| Explosions (cool) | Blue | `10` |
| Stars and planets | Mixed (random) | `01`, `10`, `11` |
| Text (status panel) | White | `11` |
| Triton missiles | Blue (moving ball) | `10` |

Stars use random artifact patterns (blue, red, white) for a colorful starfield.

### RG6 Hardware Setup

```
VRAM base:  $4000 (SAM F offset = 32)
VRAM size:  6144 bytes ($4000-$57FF)
SAM V bits: 110 (V=6)
PIA $FF22:  $F8 (A*/G=1, GM2=1, GM1=1, GM0=1, CSS=1)
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

### String Literals

The fc.py cross-compiler supports `S"` and `."` string literals, which compile
inline string data with a BRANCH-over-skip pattern. At runtime, `S"` pushes
(addr len) on the stack; `."` additionally calls TYPE. The `rg-type` library
word loops over a string calling `rg-emit` for each character.

String literals save significant app space compared to per-character `CHAR X
rg-emit` sequences (4 bytes/char vs ~1.2 bytes/char for strings of 10+
characters). The status panel labels, title screen, briefing text, and death
messages all use string literals.

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
| 7 | Self-Destruct | "123" + ENTER | Countdown 5→1, destroy ship + nearby Jovians |
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

## Explosion System

Explosions are animated expanding rings using the trig library. Each frame
generates dots at random angles along a ring at increasing radius. Dots
accumulate across frames (no per-frame erase), building an expanding cloud
that transitions white → red → blue before a final erase.

### Explosion Parameters

| Type | Frames | Start R | End R | Dots/Frame | Blast R | Damage |
|------|--------|---------|-------|------------|---------|--------|
| Jovian | 6 | 2 | 12 | 20 | 12px | 30 |
| Ship | 10 | 3 | 22 | 28 | — | — |
| Base | 12 | 4 | 26 | 32 | 20px | 50 |
| Self-destruct | 20 | 8 | 68 | 48 | 60px | 200 |

### Color Schedule

Computed from frame position relative to total frames:
- First 30%: white (color 3) — hot core
- Next 30%: red (color 2) — cooling
- Next 25%: blue (color 1) — fading
- Last 15%: erase only (no new dots drawn)

### Proximity Damage

After the explosion animation, Manhattan distance from explosion center is
checked against all living Jovians and the ship. Entities within the blast
radius take damage. Ship takes half the listed damage amount.

Proximity-killed Jovians chain-explode (erase sprite, play Jovian-sized
explosion) but secondary explosions do not trigger further proximity checks.
A bitmask tracks which Jovians were killed per proximity pass.

### Screen Rebuild

After any kill, `refresh-after-kill` clears the tactical area (rows 0–143,
4608 bytes VRAM) and fully redraws: border, stars, storm stars, event
horizon, base, all living Jovians, and ship. The status panel is preserved.

## Beam Line of Sight

Both player masers and Jovian beams stop at the first non-black pixel in
their path. After `beam-trace` computes the full Bresenham line,
`beam-find-obstacle` scans the path buffer for the first entry with a
non-zero saved pixel color and truncates the beam total there. This means
beams are blocked by stars, sprites, the border, and any other visible
object.

Beam erase plots black (color 0) at beam pixel positions instead of
restoring saved pixel values. This eliminates stale-pixel artifacts when
sprites move after the beam was traced. The sprite refresh cycle (forced
every frame while beams are active) redraws stars and sprites naturally.

## Jovian AI

### Overview

Up to 3 Jovians occupy each quadrant. Each has a position (x, y in pixel
coordinates), a health value (0–100, 0 = dead), a state byte, and a tick
counter. The AI runs every frame via `tick-jovians` and `tick-jbeam`.

### Movement

Jovians chase the player ship at a speed that scales with difficulty level.
Movement is gated by a frame counter per Jovian: when the tick counter reaches
`jov-speed`, the Jovian moves 1 pixel toward the player and the counter resets.

**Speed formula**: `jov-speed = 10 - level` (minimum 2). At level 1, Jovians
move every 9 frames (~6.7 px/sec). At level 9, they move every 2 frames
(~30 px/sec).

**Direction**: Each axis moves independently by `sign(player - jovian)`,
producing diagonal, horizontal, or vertical chase depending on relative
position.

**Bounds clamping**: Positions are clamped to 4–123 (x) and 4–139 (y) to keep
sprites within the tactical view border.

### Obstacle Avoidance

Before committing a move, `jov-blocked?` checks the candidate position against
all obstacles using Manhattan distance:

| Obstacle | Avoidance radius |
|----------|-----------------|
| Stars | 6 px |
| Black holes | 15 px |
| Bases | 5 px |

If the diagonal move is blocked, the AI tries x-only movement, then y-only
movement. If all three are blocked, the Jovian stays put.

### Gravity

Jovians are affected by gravity wells just like the player ship. After
`tick-jovians`, `jov-gravity` pulls living Jovians toward black holes and
stars. A Jovian pulled into a black hole or star is killed (triggers explosion
and `refresh-after-kill`).

### Firing

Jovian beams are managed by a cooldown timer (`jbeam-cool`). When the cooldown
expires, `pick-jovian` selects a random living Jovian, and `fire-jbeam` fires
a red beam toward the player.

**Cooldown formula**: `150 - (level × 14)` frames (minimum 24).

| Level | Cooldown | Fire rate |
|-------|----------|-----------|
| 1 | ~136 frames | every ~2.3 sec |
| 5 | ~80 frames | every ~1.3 sec |
| 9 | ~24 frames | every ~0.4 sec |

**Beam direction**: The endpoint is calculated as `jovian_pos + (player_pos -
jovian_pos) × 4`, giving a beam that extends well past the player. The origin
is offset 5 pixels along the firing direction to avoid self-intersection.

**Line of sight**: The beam path is traced via Bresenham and truncated at the
first non-black pixel (same as player masers). Stars, sprites, and the border
block Jovian fire.

**Hit detection**: `jbeam-ship-hit?` checks whether any pixel in the beam path
falls within the ship's bounding box (±4 px horizontal, ±3 px vertical from
ship center).

### Damage

When a Jovian beam hits the player:

- **Energy damage**: 5 points subtracted from `penergy` (clamped to 0)
- **System damage**: A random system (ion engines, hyperdrive, scanners,
  deflectors, or masers) takes 20 points of damage (out of 100)

Jovians do not fire while the player is docked.

### Data Structures

```
JOV-POS     6 bytes   (3 × 2: x,y per Jovian)
JOV-DMG     3 bytes   (health: 100=full, 0=dead)
JOV-STATE   3 bytes   (AI state per Jovian, currently unused — reserved)
JOV-TICK    3 bytes   (frame counter per Jovian)
JOV-OLDX    3 bytes   (previous x for sprite redraw)
JOV-OLDY    3 bytes   (previous y for sprite redraw)
```

### Not Yet Implemented

- FLEE state (retreat toward stars when health is low)
- IDLE state (fire opportunistically without chasing)
- Base targeting priority (Jovians should prefer attacking bases)
- Jovian-to-Jovian collision avoidance
- Inter-quadrant movement (Jovians moving between galaxy sectors)

## Hyperdrive Energy Cost

Warp cost = Manhattan distance × 2, capped at 10 energy maximum. Adjacent
quadrant costs 2 energy; cross-galaxy costs 10.

## Safe Spawn

When entering a quadrant (warp or game start), the ship spawns at the center
(64, 72). `safe-spawn` then checks Manhattan distance to all stars and the
black hole. If any gravity source is within 35 pixels, the ship is relocated
to a random position and rechecked, up to 16 attempts. This prevents spawning
inside an inescapable gravity well.

## Self-Destruct Sequence

Self-destruct is state-driven and runs inside the game loop (non-blocking).

- `sd-active` holds the current countdown (5→1, or 0 when inactive)
- `sd-timer` counts down 30 frames (~0.5 sec) per step at 60Hz NTSC
- `tick-destruct` is called each game loop frame to advance the countdown
- During countdown, the game loop continues: ship can move, beams fire, etc.
- Key input is routed to `sd-check-key` which matches the cancel sequence
  7-1-2-3 (wrong keys reset progress)
- On detonation: cancel all beams/missiles, erase ship, play self-destruct
  explosion (largest ring), apply proximity damage, set `death-cause` to 2
- `death-cause` 2 tells the death handler to skip the redundant ship explosion

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

## Memory Map

ROMs are paged out at boot (`STA $FFDF`), giving full 64K RAM.

```
$0050–$007F   Kernel scratch variables (direct page)
$0400–$05FF   VDG text VRAM (32x16 alpha mode, used by title/briefing)
$1000–$1FFF   Kernel code + primitives + CFA table
$2000–$3FFF   Application code part 1 (8K, before VRAM hole)
$4000–$57FF   RG6 VRAM (6144 bytes, hole in app binary)
$5800–$73FF   Application code part 2 (continues after VRAM)
$7400–$75D8   Font data (font-art.fs, artifact-safe 8-byte glyphs)
$7600–$7774   Game data (galaxy, positions, sprites, AI state, bg saves)
$7C00–$7CEF   Lookup tables (sine, pixel color/shift/mask, CG expand)
$7E00         Data stack base (U register, grows downward)
$8000         Return stack init (S register, grows downward)
$FF00–$FFFF   I/O (PIA, SAM, VDG registers)
```
