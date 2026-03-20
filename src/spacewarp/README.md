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

The Endever is a blue chevron. Jovians are red diamonds. Maser beams streak
blue; Jovian beams burn red. Stars scatter in all four colors. The color
coding is functional — you can read the tactical display at a glance.

## What's In the Game

**Galaxy.** 64 quadrants in an 8x8 grid, randomly seeded with stars, Jovian
warships, UP bases, black holes, and magnetic storms. Difficulty level (1-9)
scales the fleet from 8 to 80+ ships.

**Tactical combat.** Arrow keys move the Endever. Jovians chase you — or the
base, if there's one to attack. Masers fire at any angle, tracing a visible
beam across the display. Triton missiles track toward the nearest Jovian.
Everything happens at 60fps with smooth sprite animation.

**Jovian AI.** Jovians path toward their target, avoid stars and black holes,
and fire red beams when they have line of sight. The first Jovian in a
quadrant prioritizes attacking the base; wounded Jovians flee to the base
instead of chasing you. If they hold position near a base long enough, it's
destroyed.

**Physics.** Black holes are invisible 30px gravity wells — tiered pull
strength, instant death on contact. Stars have their own gentle gravity.
Both affect Jovians too: a black hole will swallow a Jovian as happily as
it swallows you.

**Base defense.** Fly onto a base to dock: energy recharges, systems repair,
missiles resupply. But Jovians target bases. Leave one undefended too long
and it explodes. Lose all bases and the galaxy falls.

**Seven commands:**

| Key | Command |
|-----|---------|
| 1 | Damage report — system health for all five subsystems |
| 2 | Hyperdrive — warp to any quadrant (energy cost scales with distance) |
| 3 | Long range scan — 3x3 galaxy map centered on your position |
| 4 | Deflectors — adjust shield strength (trades maser power for defense) |
| 5 | Maser — fire at an angle (0-360), beam traces across the display |
| 6 | Triton missile — homing projectile, one-hit kill, limited supply |
| 7 | Self-destruct — 10-second countdown, massive blast radius |

**Win** by destroying every Jovian with at least one base still standing.
**Lose** if all bases are destroyed, you run out of energy, you fly into a
black hole, or you detonate your own ship.

## Technical Notes

The game is about 18.8K of compiled Forth plus a 1.9K kernel. Performance-critical
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

## Documents

- [USERGUIDE.md](USERGUIDE.md) — complete gameplay guide
- [SPEC.md](SPEC.md) — technical specification and architecture

## References

The `refs/` directory contains materials from the original game:

- `TimeTrekDesignDoc.pdf` — Lavinsky's original design document with
  flowcharts, memory maps, and subroutine listings
- `Josh Lavinsky Interview.pdf` — interview about the development of Time Trek
- `pic*.webp` — screenshots from the original TRS-80 Model I version
