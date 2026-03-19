# BARE NAKED SPACE WARP

A reimplementation of Joshua Lavinsky's Space Warp (1980, Personal Software /
Radio Shack) for the TRS-80 Color Computer, written in Bare Naked Forth.

The original was a 4K Z-80 machine language real-time space combat game for the
TRS-80 Model I — one of the first real-time action games on a home computer,
predating Atari's Star Raiders by a year. This version preserves the original
gameplay while taking advantage of the CoCo's color graphics via RG6 NTSC
artifact coloring.

## Documents

- [USERGUIDE.md](USERGUIDE.md) — complete gameplay guide
- [SPEC.md](SPEC.md) — technical specification and architecture

## Current State

Implemented:
- RG6 NTSC artifact-color display (128x192 effective, 4 colors)
- Tactical view with border, stars, base, Jovians, ship sprites
- Status panel with live readouts (stardate, energy, missiles, condition)
- Arrow key movement with energy cost
- Maser fire (command 5) with angle input, hit detection, beam erase
- Jovian beam weapons with line-of-sight, hit detection, system damage
- Triton missiles (command 6) with animated projectile, one-hit kill
- Hyperdrive (command 2, warp between quadrants with energy cost)
- Deflectors (command 4, shield energy vs maser tradeoff)
- Self-destruct (command 7, countdown with cancel sequence, proximity damage)
- Docking at bases (fly onto base to repair, resupply, restore energy)
- Black hole gravity wells (30px radius, tiered pull, invisible, instant death)
- Star gravity wells (10px radius, gentle pull) and star collision (lethal)
- Energy model (movement, masers, missiles all cost energy; 0% = destroyed)
- Galaxy generation (8x8 grid, random placement scaled by difficulty)
- Jovian AI (movement toward ship/base, firing, proximity-triggered attacks)
- Animated explosions (expanding rings, white/red/blue color schedule, chain explosions)
- SOS alerts (base under attack notification with coordinates)
- Title screen with level select (1-9), mission briefing
- Death/restart flow (black hole, destroyed, self-destruct, AGAIN? prompt)
- String literals (`S"`, `."`) in fc.py for compact text output via `rg-type`
- Object redraw after sprite erase (stars/base/jovians persist correctly)

Not yet implemented:
- Damage report (command 1, system status display)
- Long range scan (command 3, galaxy map)
- Ship damage system (5 degradable systems)
- Magnetic storms, scoring, win/lose conditions

## Performance

Graphics-intensive primitives are implemented as 6809 assembly kernel
primitives rather than ITC Forth, eliminating threading overhead in the
tightest loops.  These are called hundreds of times per frame:

| Primitive | Function | ITC Forth | Kernel asm | Speedup |
|-----------|----------|-----------|------------|---------|
| rg-pset | Plot one artifact pixel | ~500 cy | ~45 cy | 11× |
| rg-line | Bresenham line (per pixel) | ~1500 cy | ~150 cy | 10× |
| spr-draw | Draw sprite (per pixel) | ~700 cy | ~60 cy | 12× |
| spr-erase-box | Erase sprite bbox (per pixel) | — | ~35 cy | new |
| rg-char | Render font glyph | ~900 cy | ~320 cy | 3× |

BASIC ROMs are paged out at startup (`$FFDF`) to reclaim the upper 32K
as RAM.

## References

The `refs/` directory contains reference materials from the original game:

- `TimeTrekDesignDoc.pdf` — Lavinsky's original design document with flowcharts,
  memory maps, subroutine listings, and entry points
- `Josh Lavinsky Interview.pdf` — interview about the development of Time Trek
- `pic*.webp` — screenshots from the original game showing tactical view,
  scanner, damage report, docking, combat, mission briefing, and box art
