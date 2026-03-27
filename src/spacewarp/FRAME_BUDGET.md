# Space Warp Frame Budget

**Target: 16,667 cycles per frame (60fps NTSC VSYNC)**
**CPU: 6809 @ 0.895 MHz (CoCo 2 NTSC) = ~14,917 cycles/frame**

The 6809 runs at 0.895 MHz, giving ~14,917 CPU cycles between VSYNC events.
The game loop must complete all pre-VSYNC work within this budget, or frames drop.

## ITC Threading Overhead

All cycle counts computed by `fc.py --cycles` from the MC6809E instruction timing
tables.  The ITC (Indirect Threaded Code) model imposes significant per-word overhead
that dominates the cost of most Forth words:

| Mechanism | Cycles | What it does |
|-----------|--------|-------------|
| **NEXT** | 15 | `LDY ,X++` (9) + `JMP [,Y]` (6) — dispatches next word |
| **DOCOL** | 27 | `PSHS X` (7) + `LEAX 2,Y` (5) + NEXT (15) — enters colon def |
| **EXIT** | 22 | `PULS X` (7) + NEXT (15) — returns from colon def |
| **DOVAR** | 29 | `LEAY 2,Y` (5) + `STY ,--U` (9) + NEXT (15) — pushes var addr |
| **CONSTANT** | 80 | DOCOL (27) + LIT (31) + EXIT (22) — pushes constant value |

Every colon word call pays DOCOL + EXIT = **49cy** overhead before doing any work.
Every CONSTANT reference costs **80cy** — more than DROP (20cy) + DUP (28cy) combined.
Every VARIABLE reference costs **29cy** just to push its address.

These overheads explain why previous hand estimates were off by ~10x: they counted
the work instructions but not the threading dispatch.

## Kernel Primitive Costs

Computed from kernel.asm instruction-by-instruction (includes trailing NEXT):

| Primitive | Cycles | | Primitive | Cycles | | Primitive | Cycles |
|-----------|--------|-|-----------|--------|-|-----------|--------|
| DROP | 20 | | DUP | 28 | | SWAP | 39 |
| OVER | 29 | | ROT | 53 | | + | 36 |
| - | 37 | | * | 114 | | /MOD | 88 |
| = | 52 | | <> | 52 | | < | 53 |
| > | 53 | | 0= | 37 | | AND | 39 |
| OR | 39 | | XOR | 40 | | INVERT | 29 |
| @ | 31 | | ! | 37 | | C@ | 32 |
| C! | 35 | | +! | 43 | | LIT | 31 |
| 0BRANCH | 66 | | BRANCH | 33 | | DO | 49 |
| LOOP | 83 | | +LOOP | 141 | | I | 28 |
| J | 29 | | >R | 30 | | R> | 30 |
| R@ | 28 | | ?DUP | 31 | | 2DROP | 20 |
| 2DUP | 43 | | PICK | 38 | | UNLOOP | 20 |
| ABS | 36 | | NEGATE | 33 | | MIN | 47 |
| MAX | 47 | | MDIST | 99 | | FILL | 69 |
| CMOVE | 96 | | TYPE | 140 | | COUNT | 43 |
| EMIT | 68 | | CR | 44 | | LSHIFT | 71 |
| RSHIFT | 71 | | KEY | 318 | | KEY? | 141 |
| KBD-SCAN | 48 | | PROX-SCAN | 252 | | | |

## CODE Word Costs

Assembly CODE words bypass ITC overhead — their costs are exact:

| CODE Word | Cycles | Notes |
|-----------|--------|-------|
| `jov-think` | 689-693 | Genome-driven AI, single Jovian |
| `gen-jov-sprite` | 627 | Contains loops |
| `beam-trace` | 619 | Contains loop |
| `rg-line` | 571 | Bresenham line draw |
| `spr-draw` | 410-411 | Sprite blit with loops |
| `prox-dmg` | 306 | Proximity scan loop |
| `beam-draw-slice` | 285 | Beam segment draw |
| `beam-restore-slice` | 280 | Beam segment erase |
| `spr-erase-box` | 242-243 | Sprite erase |
| `plot-dots` | 242 | Explosion dots |
| `xyn-pull` | 210 | Gravity pull (assembly) |
| `jov-flee` | 201 | Flee movement calc |
| `rg-char` | 188 | Character draw (8-line loop) |
| `collision-scan` | 177 | Star/bhole collision loop |
| `bg-save` / `bg-restore` | 140 | Background save/restore |
| `bg-save-7` / `bg-restore-7` | 140 | 7-row variant |
| `save-jov-oldpos-n` | 82 | Position copy loop |

## Forth Word Costs

Computed by recursive analysis (DOCOL + body + EXIT).  **Caveat**: words with
IF/ELSE/THEN report the sum of ALL branches as a worst case.  Actual per-invocation
cost depends on which branch is taken.  Words without branches are exact.

### Every Frame (always runs)

| Word | Computed | Old Est. | Notes |
|------|----------|----------|-------|
| `save-ship-pos` | **472cy** | 50 | Exact (no branches). 2 CONSTANT refs (160cy) dominate |
| `move-ship` | **23,969cy** | 400 | Upper bound (many IF branches). Typical: ~3,000-5,000cy |
| `latch-key` | **353cy** | 50 | Exact |
| `tick-missile` | **84,038cy** | 150 | Upper bound. Typical (no missile): ~200cy |
| `tick-jbeam` | **17,357cy** | 200 | Upper bound. Typical (no beam): ~600cy |

### Even Frames (physics + AI)

| Word | Computed | Old Est. | Notes |
|------|----------|----------|-------|
| `gravity-well` | **2,132cy** | 300 | Upper bound (has IF for bhole present) |
| `star-gravity` | **4,183cy** | 160/star | Upper bound (DO/LOOP over stars) |
| `check-collisions` | **2,127cy** | 200 | Upper bound |
| `tick-jovians` | **67,230cy** | 2,100 | Upper bound (DO/LOOP over Jovians) |

**Hot path — single Jovian think:**
- `jov-think` (CODE): 689cy — exact
- `apply-intent`: 27,228cy — upper bound (calls `jov-blocked?` up to 3x)
- `jov-blocked?`: 7,830cy — upper bound (DO/LOOP over stars + Jovians)
- `jov-dist`: 549cy — exact (called per obstacle in jov-blocked?)

### Odd Frames (gravity + background)

| Word | Computed | Old Est. | Notes |
|------|----------|----------|-------|
| `jov-gravity` | **356,558cy** | 1,500 | Upper bound (nested loops: Jovians × stars) |

### Every 8th Frame (background tasks)

| Word | Slot | Computed | Old Est. |
|------|------|----------|----------|
| `jov-check-regen` | 1 | **1,965cy** | 200 |
| `check-dock` | 1 | **3,035cy** | 150 |
| `tick-dock` | 1 | **7,238cy** | 300 |
| `tick-base-attack` | 1 | **83,708cy** | 200 |
| `tick-stardate` | 5 | **20,341cy** | 100 |
| `tick-migrate` | 5 | **27,203cy** | 500 |
| `update-cond` | 5 | **13,491cy** | 200 |

### Post-VSYNC Rendering

| Word | Computed | Old Est. | Notes |
|------|----------|----------|-------|
| `tick-beam-erase` | **3,707cy** | 150 | Forth wrapper + CODE inner |
| `tick-beam-draw` | **2,308cy** | 150 | Forth wrapper + CODE inner |
| `restore-ship-bg` | **525cy** | 100 | Forth wrapper + bg-restore (140cy CODE) |
| `restore-jov-bgs` | **948cy** | 300 | DO/LOOP + bg-restore-7 per Jovian |
| `draw-stars` | **1,938cy** | 250 | DO/LOOP + rg-pset (158cy CODE) per star |
| `save-jov-bgs` | **949cy** | 300 | DO/LOOP + bg-save-7 per Jovian |
| `save-ship-bg` | **696cy** | 100 | Forth wrapper + bg-save (140cy CODE) |
| `draw-jovians-live` | **1,190cy** | 600 | DO/LOOP + spr-draw per Jovian |
| `draw-ship` | **966cy** | 200 | Forth wrapper + spr-draw (410cy CODE) |

## Why the Estimates Were Wrong

The original estimates counted the "work" instructions (C@, !, MDIST, etc.) but
ignored the ITC threading tax.  For example, `save-ship-pos`:

| Component | Old model | Actual |
|-----------|-----------|--------|
| 2× C@ | 2cy | 2 × 32cy = 64cy |
| 2× ! | 2cy | 2 × 37cy = 74cy |
| 2× CONSTANT ref | 0cy | 2 × 80cy = 160cy |
| 2× VARIABLE ref | 0cy | 2 × 29cy = 58cy |
| LIT(1) + ADD | 0cy | 31 + 36 = 67cy |
| DOCOL + EXIT | 0cy | 27 + 22 = 49cy |
| **Total** | **~50cy** | **472cy** |

The CONSTANT and VARIABLE references alone cost 218cy — over 4x the original total
estimate.  This pattern repeats throughout the codebase: the threading overhead is
the dominant cost, not the primitive operations.

## Jitter Sources

1. **Even vs odd frames**: Even frames do physics+AI, odd frames do gravity+BG.
   Even frames are significantly heavier due to `tick-jovians`.

2. **Jovian think frequency**: At level 9, `jov-threshold` returns 2-3 (vs 4-6 at
   moderate levels).  This means all 3 Jovians can think on the same even frame
   every 2-3 frames instead of staggering.  Each think triggers `apply-intent`
   which calls `jov-blocked?` up to 3x.

3. **`jov-blocked?` cost**: Each call loops over all stars (up to 5), black hole,
   base, ship, and other Jovians.  Each obstacle check calls `jov-dist` (549cy).
   With 5 stars: ~5 × 549 = 2,745cy per jov-blocked? call.  Up to 9 calls per
   frame (3 Jovians × 3 retries) = ~24,700cy in jov-blocked? alone.

4. **CONSTANT overhead**: Constants like SHIP-POS, STAR-POS, JOV-POS cost 80cy
   each.  These appear in inner loops and hot paths everywhere.

5. **Rendering path**: Full redraw (jov-moved) triggers restore-jov-bgs +
   draw-stars + save-jov-bgs + draw-jovians-live + draw-ship.  Combined:
   ~5,700cy for 3 Jovians + 5 stars.

## Optimization Opportunities

| Opportunity | Estimated Savings | Complexity | Impact |
|-------------|-------------------|------------|--------|
| `jov-blocked?` → CODE | ~6,000cy/call, 9 calls/frame worst case | High | **Critical** |
| `apply-intent` → CODE | ~25,000cy/call, eliminates jov-blocked? Forth overhead | High | **Critical** |
| Inline CONSTANT values as LIT | 49cy per ref (skip DOCOL+EXIT) | Low (fc.py change) | **High** |
| `jov-dist` → CODE | ~400cy/call, called 10-15x per jov-blocked? | Medium | High |
| Stagger AI: 1 Jovian per frame | Spreads cost across frames | Low | Medium |
| `move-ship` → CODE | ~20,000cy | High | Medium |
| VARIABLE → direct page | 1cy/access saving | Low (kernel change) | Low |

### Quick Win: Inline Constants

The single highest-impact, lowest-effort optimization is to have `fc.py` compile
CONSTANT references as inline LIT values instead of colon-word calls.  This
eliminates the DOCOL+EXIT overhead (49cy) per reference, reducing each CONSTANT
from 80cy to 31cy.  Space Warp uses ~100 constants, many in hot loops.  This
change is entirely within `fc.py` and requires no assembly or game code changes.

## Measurement

Cycle counts generated by `fc.py --cycles`, which computes exact 6809 instruction
timing for CODE words and kernel primitives, and recursive ITC analysis for Forth
colon definitions.  Forth word costs for words with IF/ELSE branches represent the
sum of all paths (worst case upper bound); words without branches are exact.

Note: `fc.py --cycles` reports per-word costs without loop multiplication.  For
words containing DO/LOOP, the reported cost includes one iteration of the loop body.
The appendix below uses manually expanded costs with correct loop iteration counts.

## Appendix: Realistic Per-Component Costs

These costs account for ITC overhead and loop iterations.  Derived from
`fc.py --cycles` primitive costs, then manually traced through hot paths.

### Component Cost Table

| Component | Abbrev | Cycles | Derivation |
|-----------|--------|--------|------------|
| Every-frame base | EF | 5,000 | save-ship-pos(472) + move-ship(~3,000) + process-key(~300) + latch-key(353) + tick-destruct(~150) + tick-missile(~200 idle) + tick-jbeam(~300 idle) |
| Star gravity (5 stars) | SGrv | 4,200 | star-gravity: DO/LOOP 5 stars × (mdist(99) + CONSTANT(80) + overhead ~660) |
| Gravity well (bhole) | GW | 2,100 | gravity-well: mdist + tiered pull + xyn-pull(210 CODE) |
| Check collisions | Coll | 1,000 | collision-scan CODE(177) + Forth wrapper |
| 1 Jovian think | J | 17,000 | jov-think CODE(689) + apply-intent(~2,500) + jov-blocked?(~13,800 w/5 stars) |
| Jov-gravity (3 jov, grav frame) | JGrv | 5,000 | jov-gravity-one × 3: bhole check + 5-star loop per Jovian |
| Jov-gravity (3 jov, non-grav) | JGrv-lite | 1,200 | bhole contact check only (no star loop) |
| Background slot 1 | BG1 | 3,000 | jov-check-regen + check-dock + tick-dock + tick-base-attack (typical paths) |
| Background slot 5 | BG5 | 3,000 | tick-stardate + tick-migrate + check-spawn + update-cond (typical paths) |
| Full render (3 jov + 5 stars) | Rnd-F | 7,200 | restore-jov-bgs(948) + draw-stars(1,938) + save-jov-bgs(949) + draw-jovians(1,190) + draw-ship(966) + save-ship-bg(696) + restore-ship-bg(525) |
| Ship-only render | Rnd-S | 2,200 | restore-ship-bg(525) + save-ship-bg(696) + draw-ship(966) |
| Post-render | Post | 1,500 | beam erase/draw + apply hits + panel updates (typical) |

### `jov-blocked?` Cost Breakdown (the bottleneck)

Each call to `jov-blocked?` loops over ALL obstacles, calling `jov-dist` (549cy) per check:

| Check | Per-item cost | Items | Subtotal |
|-------|---------------|-------|----------|
| Star loop | jov-dist(549) + jov-avoid-dist(539) + compare(~120) + LOOP(83) + overhead(~200) | 5 | 7,455 |
| Black hole | jov-dist(549) + compare(~200) + overhead(~200) | 1 | 949 |
| Base | jov-dist(549) + compare(~200) + overhead(~200) | 1 | 949 |
| Ship | jov-dist(549) + compare(~200) | 1 | 749 |
| Other Jovians | jov-dist(549) + compare(~200) + LOOP(83) + overhead(~200) | 2 | 2,064 |
| Setup + cleanup | | | ~1,600 |
| **Total per call** | | | **~13,800** |

`apply-intent` calls `jov-blocked?` 1-3 times (try both axes, try x-only, try y-only).
Typical (first try succeeds): 1 call = 13,800cy.  Worst case: 3 calls = 41,400cy.

## Appendix: 60-Frame Cycle Map (Moderate Difficulty)

Scenario: 3 Jovians (thresholds 4/6/3, staggered 0/1/2), 5 stars, black hole,
base, no active beam/missile.  Budget: **14,917 cy/frame**.

Gravity gated to every 4th even frame (frames 0, 8, 16, 24, 32, 40, 48, 56).
Background tasks on odd frames: slot 1 at frame `N AND 7 = 1`, slot 5 at `N AND 7 = 5`.

Jovian think schedule (tick increments each even frame, thinks when tick ≥ threshold):
- J0 (threshold 4, start 0): thinks at even frames 6, 14, 22, 30, 38, 46, 54
- J1 (threshold 6, start 1): thinks at even frames 8, 20, 32, 44, 56
- J2 (threshold 3, start 2): thinks at even frames 0, 6, 12, 18, 24, 30, 36, 42, 48, 54

Full render (Rnd-F) on even frames when a Jovian thinks (jov-moved=1).
Ship-only render (Rnd-S) on even frames with no thinks and on most odd frames.
Gravity odd frames get full render if jov-gravity moves a Jovian.

| Fr | E/O | EF | SGrv | GW | Coll | J0 | J1 | J2 | JGrv | BG | Rnd | Post | **Total** | |
|----|-----|----|------|----|------|----|----|----|------|----|-----|------|-----------|---|
| 0 | E/G | 5000 | 4200 | 2100 | 1000 | | | 17000 | | | 7200 | 1500 | **38,000** | **OVER** |
| 1 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 2 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 3 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 4 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 5 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 6 | E | 5000 | | | 1000 | 17000 | | 17000 | | | 7200 | 1500 | **48,700** | **OVER** |
| 7 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 8 | E/G | 5000 | 4200 | 2100 | 1000 | | 17000 | | | | 7200 | 1500 | **38,000** | **OVER** |
| 9 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 10 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 11 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 12 | E | 5000 | | | 1000 | | | 17000 | | | 7200 | 1500 | **31,700** | **OVER** |
| 13 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 14 | E | 5000 | | | 1000 | 17000 | | | | | 7200 | 1500 | **31,700** | **OVER** |
| 15 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 16 | E/G | 5000 | 4200 | 2100 | 1000 | | | | | | 2200 | 1500 | **21,000** | **OVER** |
| 17 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 18 | E | 5000 | | | 1000 | | | 17000 | | | 7200 | 1500 | **31,700** | **OVER** |
| 19 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 20 | E | 5000 | | | 1000 | | 17000 | | | | 7200 | 1500 | **31,700** | **OVER** |
| 21 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 22 | E | 5000 | | | 1000 | 17000 | | | | | 7200 | 1500 | **31,700** | **OVER** |
| 23 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 24 | E/G | 5000 | 4200 | 2100 | 1000 | | | 17000 | | | 7200 | 1500 | **38,000** | **OVER** |
| 25 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 26 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 27 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 28 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 29 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 30 | E | 5000 | | | 1000 | 17000 | | 17000 | | | 7200 | 1500 | **48,700** | **OVER** |
| 31 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 32 | E/G | 5000 | 4200 | 2100 | 1000 | | 17000 | | | | 7200 | 1500 | **38,000** | **OVER** |
| 33 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 34 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 35 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 36 | E | 5000 | | | 1000 | | | 17000 | | | 7200 | 1500 | **31,700** | **OVER** |
| 37 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 38 | E | 5000 | | | 1000 | 17000 | | | | | 7200 | 1500 | **31,700** | **OVER** |
| 39 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 40 | E/G | 5000 | 4200 | 2100 | 1000 | | | | | | 2200 | 1500 | **21,000** | **OVER** |
| 41 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 42 | E | 5000 | | | 1000 | | | 17000 | | | 7200 | 1500 | **31,700** | **OVER** |
| 43 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 44 | E | 5000 | | | 1000 | | 17000 | | | | 7200 | 1500 | **31,700** | **OVER** |
| 45 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 46 | E | 5000 | | | 1000 | 17000 | | | | | 7200 | 1500 | **31,700** | **OVER** |
| 47 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 48 | E/G | 5000 | 4200 | 2100 | 1000 | | | 17000 | | | 7200 | 1500 | **38,000** | **OVER** |
| 49 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 50 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 51 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 52 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 53 | O | 5000 | | | | | | | 1200 | 3000 | 2200 | 1500 | **12,900** | |
| 54 | E | 5000 | | | 1000 | 17000 | | 17000 | | | 7200 | 1500 | **48,700** | **OVER** |
| 55 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |
| 56 | E/G | 5000 | 4200 | 2100 | 1000 | | 17000 | | | | 7200 | 1500 | **38,000** | **OVER** |
| 57 | O/G | 5000 | | | | | | | 5000 | 3000 | 7200 | 1500 | **21,700** | **OVER** |
| 58 | E | 5000 | | | 1000 | | | | | | 2200 | 1500 | **9,700** | |
| 59 | O | 5000 | | | | | | | 1200 | | 2200 | 1500 | **9,900** | |

### Summary (Moderate Difficulty)

| Metric | Value |
|--------|-------|
| **Budget per frame** | 14,917 cy |
| **Light frame (no thinks, no gravity)** | ~9,700 cy (65%) |
| **1 Jovian think** | ~31,700 cy (213%) — **drops 1 frame** |
| **2 Jovians think (frame 6, 30, 54)** | ~48,700 cy (326%) — **drops 2 frames** |
| **Gravity even + 1 think** | ~38,000 cy (255%) — **drops 1-2 frames** |
| **Gravity odd + BG** | ~21,700 cy (145%) — **drops 1 frame** |
| **Frames over budget** | **28 of 60 (47%)** |
| **Frames under budget** | 32 of 60 (53%) |
| **Effective FPS** | ~30-35 fps (every think frame drops) |

Even at moderate difficulty, nearly half of all frames exceed the budget.  The game
runs because VSYNC forces a consistent cadence — overbudget frames simply skip a
VSYNC and complete in 2 frame periods (~33ms instead of ~17ms).  The player
perceives this as ~30fps during combat, which is playable but not 60fps.

### Why Level 9 Is Worse

At level 9, `jov-threshold` drops to 2-3 for all Jovians (vs 3-6 at moderate).
This means:
- Jovians think every 2nd even frame instead of every 3rd-6th
- 2 Jovians frequently think simultaneously
- Think frames (31,700-48,700cy) occur 2-3x more often
- **Effective FPS drops to ~20-25fps**, which is visibly sluggish

The single biggest optimization target is `jov-blocked?`:  converting it to a CODE
word would reduce its cost from ~13,800cy to ~2,000-3,000cy (assembly eliminates
ITC overhead for `jov-dist` calls and the star loop).  This alone would bring think
frames from ~31,700cy down to ~15,000-20,000cy — close to or within budget.
