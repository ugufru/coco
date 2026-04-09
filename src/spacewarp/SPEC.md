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

Five systems, each with health (100% = fully operational, 0% = destroyed). All 5 at 0% = ship destroyed. Energy can reach 0 without death — ship is just powerless.

| System | Key | Effect when damaged | At 0% |
|--------|-----|-------------------|-------|
| Ion engines | Arrow keys | Speed scales: >67%→3px, 35-67%→2px, 1-34%→1px | Ship frozen — zero velocity, cannot move (#307) |
| Hyperdrive | 2 | <50%: 50% chance of mis-jump to adjacent quadrant | <25%: warp disabled entirely |
| Scanners | 3 | <40%: long-range scan shows noise (@), worse health = more static (#310) | Scanners useless — all noise |
| Deflectors | 4 | Absorbs 15/hit when UP. <40%: bleedthrough leaks damage to systems (#306) | Forced DOWN, cannot raise. Key 4 diverts 25% from warp to rebuild (#339) |
| Masers | 5 | Damage = maser-base × health / 100. Range-dependent base: 50 close (<15px), 30 mid (15-60px), 10 long (>60px) (#312) | Zero damage output |

### Energy Model

- **Ship energy** (0-100) — depleted by firing masers (10 energy), warp (Manhattan distance × 2, max 10), and system repairs. Recharged passively and by docking.
- **Passive regen** — every 16 frames (~3.75/sec), if energy > 0: attempt repair of ONE system (+2% to highest-priority damaged system, costs 1 energy). If no system needs repair, energy accumulates (+1). If energy = 0: slow regen to 1, then spent on next repair tick.
- **Field repair** — +2% per tick to ONE system. Below 25%: repairs up to 25% cap. 25-74%: repairs up to 75% cap. 75%+: no field repair (#309). Starbase docking required for full recovery to 100%.
- **Repair priority** — shields UP: deflectors → ion → warp → scan → masers. Shields DOWN: ion → warp → scan → masers → deflectors (#340). Only the highest-priority damaged system heals each tick — creates repair queue pressure.
- **Triton missiles** — finite supply (10 at start), replenished at bases. Each base carries **25 missiles total** (#318). Docking refills up to 10 from the base's pool. After 2-3 docks, the base runs dry. One-hit kill.

### Shield Model

Trek-style deflector toggle — shields UP/DOWN, no energy cost, no number entry.

- **Toggle**: Key 4 toggles UP/DOWN. No energy cost. Shield strength = pdmg-defl (deflector system health, 0-100%).
- **Damage absorption**: Each hit reduces pdmg-defl by 15. Above 40%: full absorption, no system damage. Below 40%: bleedthrough fraction `(damage × (40 - health) / 80)` bypasses to random systems (#306).
- **Shield failure**: At 0%, shields forced DOWN automatically. Cannot raise until repaired.
- **Emergency power**: When deflectors at 0%, Key 4 diverts 25% from warp drive to rebuild deflectors to 25% (#339). Requires warp ≥ 25%.
- **Docking**: Shields must be DOWN to dock (#345).
- **Damage spread**: Unshielded hits split across 2 random systems. If first system depleted, remainder overflows to second (#315).

### Time

- 1 stardate = ~15 seconds real-time (112 frames at 60fps, called every 8 frames = 896 actual frames)
- Game clock advances continuously
- SOS escalation: SOS-BASE (stardate 1) → SOS-URGENT (stardate 2) → SOS-FINAL (stardate 3) → base destroyed with DESTROYED C,R notification (#319)

### Scoring

Score based on: fraction of Jovians destroyed, fraction of bases surviving,
stardates elapsed. Scale 1-250.

## Commands

| Key | Command | Input | Description |
|-----|---------|-------|------------|
| 1 | Damage Report | — | Show system damage %, Jovians/bases remaining |
| 2 | Hyperdrive | 2 digits (col, row) | Warp to quadrant (0-7, 0-7) |
| 3 | Long Range Scan | — | Show 8x8 galaxy map |
| 4 | Deflectors | — (toggle) | Toggle shields UP/DOWN. At 0%: diverts 25% warp power |
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

**Jovian beam hit detection order** (`fire-jbeam-resolve`):
1. Check ship hit on the full unscrubbed trace (`jbeam-ship-hit?`)
2. Truncate at first non-black pixel (`beam-find-obstacle`) — beam stops at ship sprite
3. Scrub sprite pixels from truncated path (`beam-scrub-sprites`) — for clean erase

This order ensures the beam visually stops at the ship while still detecting the hit. The ship sprite pixels act as the natural obstacle that terminates the beam.

Beam erase plots black (color 0) at beam pixel positions instead of
restoring saved pixel values. This eliminates stale-pixel artifacts when
sprites move after the beam was traced. The sprite refresh cycle (forced
every frame while beams are active) redraws stars and sprites naturally.

## Jovian AI

### Overview

Up to 3 Jovians occupy each quadrant. Each has a position (x, y in pixel
coordinates), a health value (0–100, 0 = dead), a state byte (0=IDLE, 1=ATTACK, 2=FLEE), a 4-byte genome, and a tick
counter. The AI runs every frame via `tick-jovians` and `tick-jbeam`.

### Targeting

Each Jovian selects a target every time it moves (`jov-target`):

- **Jovian 0** always targets the base (if one exists in the quadrant)
- **Wounded Jovians** (health < 50) flee toward the base for cover
- **All others** chase the player ship

Base-targeting Jovians stop at 30px Manhattan distance from the base and
hold position rather than closing further. This standoff distance lets them
"attack from range" while the base destruction timer runs.

### Movement

Jovians move toward their target at a speed that scales with difficulty level.
Movement is gated by a frame counter per Jovian: when the tick counter reaches
`jov-speed`, the Jovian moves 1 pixel toward its target and the counter resets.

**Speed formula**: `jov-speed = 10 - level` (minimum 2). At level 1, Jovians
move every 9 frames (~6.7 px/sec). At level 9, they move every 2 frames
(~30 px/sec).

Speed is now genome-driven: each Jovian's tick threshold = `10 - (speed_modifier + pilot_skill)`, clamped to 2-8. A slow dolt ticks every 8 frames; a fast ace ticks every 2 frames.

**Direction**: Each axis moves independently by `sign(target - jovian)`,
producing diagonal, horizontal, or vertical movement depending on relative
position.

**Bounds clamping**: Positions are clamped to 4–123 (x) and 4–136 (y) to allow margin for 7-pixel-tall sprites within the tactical view border.

### Obstacle Avoidance

Before committing a move, `jov-blocked?` (6809 CODE word, ~1,105cy) checks the
candidate position against all obstacles using Manhattan distance:

| Obstacle | Avoidance radius |
|----------|-----------------|
| Stars | 6-13 px (scales with pilot skill) |
| Black holes | 15 px |
| Bases | 5 px |
| Endever (ship) | 8 px |
| Other Jovians | 8 px |

If the diagonal move is blocked, the AI tries x-only movement, then y-only
movement. If all three are blocked, the Jovian stays put.

Star avoidance distance scales with pilot skill: `6 + pilot_skill` (0-7). Skilled pilots also resist star gravity when beyond their avoidance distance. Fleeing Jovians use the same obstacle avoidance as attacking Jovians (via apply-intent).

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

IDLE Jovians (state 0) can also fire opportunistically when the player is within their detection range, without transitioning to ATTACK state. Emotion scales the cooldown: rage (emotion 15) reduces cooldown to 65%, fear (emotion 0) increases it to 140%.

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

When a Jovian beam hits the player (JBEAM-DMG = 75):

- **Shields up**: Shields absorb the hit, losing 25 points per hit. No system damage while shields hold. On shield failure (shields reach 0), shields drop and must be re-raised.
- **Shields down**: Full 75 damage to a random system. If the target system has less than 75 health, it goes to 0 and the remainder overflows to a second random system. One unshielded hit can damage two systems.
- **Death condition**: All 5 systems at 0% = ship destroyed. Energy can reach 0 without death.
- **System selection**: `8 rnd DUP 4 > IF 5 - THEN` maps 0-7 into 0-4 (slightly biased toward ion/warp/scan).

Jovians do not fire while the player is docked.

### Ship Protection of Starbases

When the player ship intercepts a Jovian beam aimed at a starbase (ship is between the Jovian and the base), the beam stops at the ship sprite, the ship takes damage, and the base-attack timer resets. The player can protect bases by physically shielding them — a classic Trek tactic with real cost (shield/system damage).

### Data Structures

```
JOV-POS     6 bytes   (3 × 2: x,y per Jovian)
JOV-DMG     3 bytes   (health: 100=full, 0=dead)
JOV-STATE   3 bytes   (0=IDLE, 1=ATTACK, 2=FLEE)
JOV-TICK    3 bytes   (frame counter per Jovian)
JOV-OLDX    3 bytes   (previous x for sprite redraw)
JOV-OLDY    3 bytes   (previous y for sprite redraw)
JOV-GENOME  12 bytes  (3 × 4: behavior, appearance, emotion+origin)
JOV-INTENT  9 bytes   (3 × 3: proposed x, y, flags)
JOV-SPRWORK 12 bytes  (sprite generation scratch)
JOV-EMCOL   3 bytes   (cached emotion color band per Jovian)
MOOD-GRID   64 bytes  (8×8 quadrant mood persistence)
```

### Base Attack

When a living Jovian is within 30px Manhattan distance of the base
(`jov-near-base?`), a frame counter (`base-attack`) increments. Every 60
frames, the Jovian fires a beam at the base. If the player ship intercepts
the beam, the timer resets. When it reaches 180 frames (~3 sec uninterrupted),
the base is destroyed:

1. Active beams and missiles are cancelled
2. The base sprite is erased and the galaxy byte is updated (base bit cleared)
3. A medium explosion plays at the base position
4. Full sprite refresh via `refresh-after-kill`
5. The status panel redraws to show updated base count

If all bases are destroyed, the game is lost.

### Implemented Since Initial Spec

- FLEE state — wounded Jovians (health < 50%) retreat from player, sprite turns blue
- IDLE state — Jovians fire opportunistically when player is within detection range
- Jovian-to-Jovian collision avoidance (8px threshold)
- Ship-to-Jovian collision (everything bumps)
- Emotion-driven engagement distance (rage=20px to fear=65px)
- Pilot skill-based star avoidance (6-13px)
- Procedural sprite generation from genome seed
- Quadrant mood persistence (save/load/stardate decay)
- CLEAR key cancels command input
- Beam trace buffer capped at 200 pixels

### Implemented in V0.93 — Combat Rebalance

- Maser range-dependent damage — 50 close, 30 mid, 10 long (#312)
- Missile damage nerf — 60-80, no longer one-hit kill (#313)
- Jovian aim scatter by pilot skill — dolts miss, aces terrify (#314)
- Damage spread across 2 random systems per hit (#315)
- Shield bleedthrough below 40% (#306)
- Handedness-based obstacle routing (#213)
- Scanner degradation at low health (#310)

### Implemented in V0.92

- SOS timer system — bases survive 3 stardates of threat, no random destruction (#317)
- Finite base missile supply — 25 per base, docking draws from pool (#318)
- Non-linear repair — field cap at 75%, no repair below 25% (#309)
- Smooth acceleration/deceleration — velocity-based movement (#343)
- Velocity-based gravity — smooth drift instead of position jerks (#357)
- Sprite redesign — Endever with twin red engines, 7×7 starbase ring (#353, #354)

### Implemented in V0.94

- SOS escalation messages — SOS-BASE → SOS-URGENT → SOS-FINAL (#319)
- Base destruction notification with coordinates (#381)
- Stardate pace — 15 seconds per stardate (#384)
- Warp non-operative below 25%, field repair recovers to 25% (#378)
- Friendly fire on starbases (#323)

### Not Yet Implemented — Post-v1.0

- Direct hit bonus — center-distance damage scaling (#316)
- Spacebar quick-fire masers (#320)
- Status line micro-reports (#321)
- Crew count, casualties, and score (#322)
- Smart Jovian missile evasion (#183)
- Directional Endever sprites (#324)
- Permanent damage accumulation (#325)
- Sound effects (#188)
- Inter-quadrant Jovian movement (#160)
- Regional character from origin hash (#179)

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

Move the Endever directly above or below a starbase to dock. Shields must be DOWN to dock (#345).

- Rapidly recharges energy (+3/tick, capped at 100)
- Repairs all five ship systems simultaneously (+2%/tick each, no cap — heals to 100%)
- Replenishes triton missiles from base supply (up to 10; each base starts with 25 total, #318)
- Shields are NOT auto-raised — player must re-raise manually after docking
- Takes time (stardates pass during docking)

## Future Enhancements

- **Sound effects** — maser fire, triton launch, explosions, docking, SOS alarm,
  self-destruct countdown (requires sound/DAC library, issue #36/#188)
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
$0050–$0082   Kernel scratch variables (direct page, 51 bytes)
$0600–$1FFF   RG6 VRAM (6144 bytes, set by rg-init after boot)
$0E00         Bootstrap (copies staged kernel to $E000, enables all-RAM)
$1000         Staged kernel (DECB load addr; copied to $E000 at boot)
$2000–$7AAC   Application code (~23K compiled Forth, ~1,348 bytes headroom to $8000)
$8000–$8EF4   Game data (all-RAM region — galaxy, sprites, AI, mood, etc.)
$8774–$8C23   Beam path buffers (BEAM-PATH 600b + JBEAM-PATH 600b)
$9000–$91D8   Font glyphs (59 × 8 bytes, all-RAM region)
$DE00         Data stack base (U register, grows downward)
$E000–$EE30   Kernel code (80 primitives incl. graphics/beam/sprite, all-RAM mode)
$EDB0–$FEFF   Kernel growth headroom (~4.4K)
$FF00–$FFFF   I/O (PIA, SAM, VDG registers)
```
