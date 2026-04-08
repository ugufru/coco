# BARE NAKED SPACE WARP

A real-time space combat game for the TRS-80 Color Computer, written from
scratch in Bare Naked Forth — a custom ITC Forth kernel running on bare metal
with no BASIC ROMs, no OS, and no safety net.

## The Original

In 1978, Joshua Lavinsky fit a real-time Star Trek game into 4,096 bytes of
Z-80 assembly. Multiply, divide, cosine, random numbers, line drawing, sound,
and dual-threaded game logic — all in 4K. Published by Personal Software (later
VisiCorp, of VisiCalc fame) as *Time Trek*, it was rebranded *Space Warp* for
Radio Shack's TRS-80 Model I. It predates Atari's *Star Raiders* by a year.

The Enterprise became the Endever. Klingons became Jovians. Phasers became
masers. But the gameplay survived intact: an 8x8 galaxy, a lone starship,
a fleet to destroy, and bases to defend — all in real time.

## This Version

This is not a port. The original Z-80 code doesn't transfer to the 6809. This
is a from-scratch reimplementation that preserves the original gameplay while
exploiting what makes the CoCo different: NTSC artifact coloring gives us four
colors from a monochrome framebuffer, and the 6809's clean architecture lets us
build a real Forth system to write the game in.

The entire game — galaxy generation, AI, sprite engine, weapon systems, text
rendering — is written in Forth source compiled by a cross-compiler (`fc.py`)
into ITC threaded code, with a handful of assembly CODE words for the inner
pixel loops. The Forth kernel runs in all-RAM mode with BASIC ROMs paged out,
giving the game the full 64K address space.

No line of this game touches a ROM routine. Every pixel is plotted by hand.
Every keystroke is read from the PIA. The CoCo is running nothing but the
game.

### Artifact Color

The CoCo's RG6 mode is officially 256x192 monochrome. But on an NTSC display,
adjacent pixel pairs create color through chroma interference:

| Bit pair | Color |
|----------|-------|
| `00` | Black |
| `11` | White |
| `10` | Blue (Endever, bases, masers) |
| `01` | Red/orange (Jovians, their beams) |

The Endever is a blue chevron with twin red engines. Jovians are red diamonds.
Maser beams streak blue; Jovian beams burn red. Stars scatter in all four
colors. The color coding is functional — you can read the tactical display
at a glance.

## What's In the Game

**Galaxy.** 64 quadrants in an 8x8 grid, randomly seeded with stars, Jovian
warships, UP bases, black holes, and magnetic storms. Difficulty level (1-9)
scales the fleet from 8 to 80+ ships.

**Tactical combat.** Arrow keys move the Endever with smooth acceleration and
inertial damping — tap for 1px precision, hold for full cruise speed. Jovians
chase you — or the base, if there's one to attack. Masers fire at any angle,
tracing a visible beam across the display. Triton missiles track toward the
nearest Jovian. Everything happens at 60fps.

**Jovian AI.** Each Jovian has a 4-byte genome controlling aggression, pilot skill, speed, and appearance. Emotion (0-15) drives engagement distance — aggressive Jovians close to 20px, fearful ones orbit at 65px. IDLE Jovians fire opportunistically before detection; ATTACK Jovians chase; wounded Jovians FLEE. Pilot skill determines star avoidance distance (6-13px). Everything bumps — ship, Jovians, and each other collide at 8px.

**Physics.** Black holes are invisible 30px gravity wells — tiered pull
strength, instant death on contact. Stars have their own gentle gravity.
Both affect Jovians too: a black hole will swallow a Jovian as happily as
it swallows you.

**Base defense.** Fly onto a base to dock: energy recharges, systems repair
to 100%, and missiles resupply from the base's limited pool of 25. But
Jovians target bases — when an SOS alert shows a base under threat, you have
3 stardates to warp there before it falls. Lose all bases and the galaxy falls.

**Seven commands:**

| Key | Command |
|-----|---------|
| 1 | Damage report — system health for all five subsystems |
| 2 | Hyperdrive — warp to any quadrant (energy cost scales with distance) |
| 3 | Long range scan — 8x8 galaxy map centered on your position |
| 4 | Deflectors — toggle shields UP/DOWN; at 0% diverts energy to rebuild |
| 5 | Maser — fire at an angle (0-360), beam traces across the display |
| 6 | Triton missile — homing projectile, one-hit kill, limited supply |
| 7 | Self-destruct — 10-second countdown, massive blast radius |

**Win** by destroying every Jovian with at least one base still standing.
**Lose** if all five systems reach 0% (ship destroyed), all bases fall,
you fly into a black hole, or you detonate your own ship.

## Technical Notes

The game is about 24K of compiled Forth (23,967 bytes) plus a 3.5K kernel (74 primitives). Performance-critical
routines are hand-written 6809 assembly, called as CODE words from Forth:

| Primitive | What it does | Speedup vs Forth |
|-----------|-------------|-----------------|
| `rg-pset` | Plot one artifact pixel | 11x |
| `rg-line` | Bresenham line (per pixel) | 10x |
| `spr-draw` | Draw 7x5 sprite with transparency | 12x |
| `spr-erase-box` | Erase sprite bounding box | new (no Forth equivalent) |
| `rg-char` | Render font glyph to VRAM | 3x |
| `beam-trace` | Trace beam path with hit detection | new |
| `plot-dots` | Bulk pixel plotter for explosions | new |
| `prox-dmg` | Proximity-based damage calculation | new |
| `bg-save` | Save sprite background pixels | new |
| `bg-restore` | Restore sprite background pixels | new |
| `xyn-pull` | Pull position toward target (variable step) | 3x |
| `collision-scan` | Proximity collision detection loop | 2.5x |
| `jov-blocked?` | Obstacle avoidance (stars, bhole, base, ship, Jovians) | 12.5x |
| `jov-think` | Genome-driven AI intent computation | new |
| `jov-flee` | Flee intent toward JOV-INTENT | new |
| `jov-emo@` | Read Jovian emotion from genome | new |
| `jov-emotion!` | Write Jovian emotion to genome | new |
| `jov-emotion-base` | Compute aggression-derived baseline | new |
| `gen-jov-sprite` | Procedural sprite from genome seed | new |
| `min` | Keep smaller of two values | kernel primitive |
| `max` | Keep larger of two values | kernel primitive |
| `mdist` | Manhattan distance between (x,y) pairs | kernel primitive |

Everything else — the game loop, AI, galaxy model, command input, explosion
animation, docking — is pure Forth.

## Building

Requires the Bare Naked Forth kernel (built separately) and Python 3 for the
cross-compiler:

```
cd forth/kernel && make        # build kernel
cd src/spacewarp && make       # build game
make run                       # launch in XRoar emulator
```

## Roadmap — v1.0 April 15, 2026

All features complete. 18 bytes headroom. In playtesting — one week to release.

**Done (V0.92):**
- Combat rebalance — maser range damage, missile nerf, aim scatter, damage spread, shield bleedthrough (#306, #312-315)
- Deflector toggle — UP/DOWN with key 4, divert energy to rebuild at 0% (#338-341)
- Non-linear repair — field cap at 75%, no repair below 25%; starbase heals to 100% (#309, #364)
- SOS timer system — bases survive 3 stardates of threat; no random destruction (#317)
- Finite base missile supply — 25 per base, docking draws from pool (#318)
- Friendly fire — masers and missiles can destroy your own starbases (#323)
- Jovian quadrant flee — wounded Jovians escape to adjacent quadrants (#185)
- Smooth acceleration/deceleration — velocity-based movement with inertial damping (#343)
- Velocity-based gravity — stars and black holes nudge velocity for smooth drift (#357)
- Sprite redesign — Endever chevron with twin red engines, 7×7 starbase with spokes (#353, #354)
- Ion engines at 0% disables movement (#307)
- Shields block docking (#345)
- Beam idle guards + save-ship-bg CODE — 1,444cy/frame saved (#349, #350)
- Backdrop preservation — stars and base redrawn every frame (#359, #362)
- 13 CODE words promoted to kernel — 74 primitives (#332-337)
- Gravity direction fix (#360), gravity step fix (#361)
- QCOUNTS overlap fix (#355), missile flicker fix (#358)
- Overlay dismiss on reinforcement spawn (#356)

**Remaining (playtesting + release prep):**
- Bug fixes as discovered during testing
- Final version bump to v1.0, git tag, itch.io upload

**Post-release** (v1.1+ — needs bytes freed via code factoring):
- Scanner degradation at low health (#149, #230, #310)
- Spacebar quick-fire masers (#320)
- Direct hit bonus (#316), SOS escalation messages (#319)
- Status line micro-reports (#321), crew count and score (#322)
- Directional Endever sprites (#324)
- Smart Jovian missile evasion (#183), permanent damage (#325)
- Reduce Jovian movement flicker (#363)
- Sound system (#188, blocked by all-RAM mode)

## Documents

- [USERGUIDE.md](USERGUIDE.md) — complete gameplay guide
- [SPEC.md](SPEC.md) — technical specification and architecture
- [FRAME_BUDGET.md](FRAME_BUDGET.md) — CPU cycle accounting and optimization history
- [AI_DIVERSITY_STRATEGY.md](AI_DIVERSITY_STRATEGY.md) — Jovian genome system design

## References

The `refs/` directory contains materials from the original game:

- `TimeTrekDesignDoc.pdf` — Lavinsky's original design document with
  flowcharts, memory maps, and subroutine listings
- `Josh Lavinsky Interview.pdf` — interview about the development of Time Trek
- `pic*.webp` — screenshots from the original TRS-80 Model I version
