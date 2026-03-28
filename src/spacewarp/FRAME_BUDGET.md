# Space Warp Frame Budget

**CPU: 6809 @ 0.8949 MHz (14.31818 MHz crystal ÷ 16)**
**NTSC field rate: 59.94 Hz → ~14,930 cycles/frame budget**

The CoCo 2 NTSC crystal is 14.31818 MHz.  Divided by 16, the 6809 runs at
894,886 Hz.  At the NTSC field rate of 59.94 Hz, that gives ~14,930 CPU cycles
between VSYNC events.  The game loop must complete all work within this budget,
or frames drop.

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
| `jov-blocked?` | ~750 | Obstacle check, 5 stars + bhole + base + ship + 2 Jovians |
| `jov-blocked?` | 488 | Obstacle avoidance (loops: ~77cy/star, ~116cy/Jovian) |
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
- `apply-intent`: 4,937cy — upper bound (calls `jov-blocked?` up to 3x)
- `jov-blocked?` (CODE): 488cy base + 77cy/star + 116cy/Jovian = 1,105cy (5 stars, 2 Jovians)
- Total per think: 5,626cy upper bound (was 17,000cy — 67% reduction)

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
| `restore-ship-bg` | **476cy** | 100 | Forth wrapper + bg-restore (140cy CODE) |
| `restore-jov-bgs` | **948cy** | 300 | DO/LOOP + bg-restore-7 per Jovian |
| `draw-stars` | **1,938cy** | 250 | DO/LOOP + rg-pset (158cy CODE) per star |
| `save-jov-bgs` | **949cy** | 300 | DO/LOOP + bg-save-7 per Jovian |
| `save-ship-bg` | **647cy** | 100 | Forth wrapper + bg-save (140cy CODE) |
| `draw-jovians-live` | **1,190cy** | 600 | DO/LOOP + spr-draw per Jovian |
| `draw-ship` | **918cy** | 200 | Forth wrapper + spr-draw (410cy CODE) |

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

3. **`jov-blocked?` cost** (resolved): Converted to CODE word (#215).  Each call
   now costs ~750cy (was ~13,800cy in Forth).  All obstacle distance checks use
   inline 6809 unsigned abs-diff with BCC, eliminating ITC overhead entirely.

4. **CONSTANT overhead**: Constants like SHIP-POS, STAR-POS, JOV-POS cost 80cy
   each.  These appear in inner loops and hot paths everywhere.

5. **Rendering path**: Full redraw (jov-moved) triggers restore-jov-bgs +
   draw-stars + save-jov-bgs + draw-jovians-live + draw-ship.  Combined:
   ~5,700cy for 3 Jovians + 5 stars.

## Optimization Opportunities

Ranked by total impact (savings × frames affected per second).  All savings
estimates are measured vs idle-path costs or `fc.py --cycles` upper bounds.

| # | Opportunity | Saves/frame | ×Frames | Total/sec | Effort |
|---|-------------|-------------|---------|-----------|--------|
| | ~~`jov-blocked?` → CODE~~ | ~~13,800→1,105cy~~ | | | **DONE (#215)** |
| | ~~`jov-dist` → CODE~~ | ~~removed~~ | | | **DONE** (inlined) |
| | ~~Selective CONSTANT inline~~ | ~~49cy × 23 refs~~ | | | **DONE (#245)** saved 262 bytes |
| #246 | Inline high-ref CONSTANTs | ~2,000cy | 60 | 120,000cy | Low (needs ~115 more bytes) |
| #244 | `move-ship` → CODE | ~1,233cy | 60 | 73,980cy | Medium |
| #241 | `apply-intent` → CODE | ~2,500cy | 19 | 47,500cy | Medium |
| #247 | Deduplicate latch-key (3x→1x) | 706cy | 60 | 42,360cy | **Trivial** |
| #215 | `ship-jov-blocked?` → CODE | ~700cy | 60 | 42,000cy | Medium (subsumed by #244) |
| #248 | bg-jov CODE (save+restore+oldpos) | ~1,488cy | 27 | 40,176cy | Medium |
| #249 | draw-stars CODE/optimize | ~1,438cy | 27 | 38,826cy | Medium |
| #243 | `jov-gravity-one` → CODE | ~3,000cy | 8 | 24,000cy | Medium |
| #242 | `star-gravity` → CODE | ~2,000cy | 8 | 16,000cy | Medium |

### Quick Win: Deduplicate latch-key (#247)

`latch-key` (353cy) is called 3 times per frame: inside `process-key`, standalone
at line 3856, and post-VSYNC at line 3914.  Only one call is needed per frame.
Removing two redundant calls saves 706cy on every frame for zero risk.

### Quick Win: Inline High-ref Constants (#246)

Blocked on ~115 bytes of headroom (have 315, need ~430).  CODE word conversions
(#244, #241) typically produce net byte savings (assembly smaller than ITC), which
should unblock this.  SHIP-POS alone (52 refs × 49cy = 2,548cy) is the single
largest constant overhead.

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
| Every-frame base | EF | 4,436 | move-ship(1,633 idle) + process-key(785) + save-ship-pos(472) + latch-key(353) + tick-systems(764: msl 212 + jbeam 340 + destruct 212) + loop overhead(429: frame-tick 103 + 2× overlay check 326) |
| Star gravity (5 stars) | SGrv | 4,183 | star-gravity: DO/LOOP 5 stars × (mdist(99) + overhead). fc.py --cycles: 4,183cy |
| Gravity well (bhole) | GW | 2,132 | gravity-well: mdist + tiered pull + xyn-pull(210 CODE). fc.py --cycles: 2,132cy |
| Check collisions | Coll | 1,000 | collision-scan CODE(177) + Forth wrapper |
| 1 Jovian think | J | 5,626 | jov-think CODE(689) + apply-intent(4,937 upper bound, includes 3x jov-blocked? CODE at 1,105/call) |
| Jov-gravity (3 jov, grav frame) | JGrv | 5,000 | jov-gravity-one × 3: bhole check + 5-star loop per Jovian |
| Jov-gravity (3 jov, non-grav) | JGrv-lite | 1,200 | bhole contact check only (no star loop) |
| Background slot 1 | BG1 | 3,000 | jov-check-regen + check-dock + tick-dock + tick-base-attack (typical paths) |
| Background slot 5 | BG5 | 3,000 | tick-stardate + tick-migrate + check-spawn + update-cond (typical paths) |
| Full render (3 jov + 5 stars) | Rnd-F | 7,732 | bg-jov(2,088: restore 948 + save 949 + oldpos 191) + draw-stars(1,938) + draw-jovians(1,190) + bg-ship(1,123: restore 476 + save 647) + draw-ship(918) + overhead(475: conditionals) |
| Ship-only render | Rnd-S | 2,644 | bg-ship(1,123: restore 476 + save 647) + draw-ship(918) + overhead(603: conditionals) |
| Post-render | Post | 2,046 | panel-checks(672: 3× var compare) + latch-key(353) + apply-beam-hit(259 idle) + tick-beam-draw(212 idle) + tick-jbeam-draw(212 idle) + apply-jbeam-hit(212 idle) + check-win(126) |

### `jov-blocked?` Cost Breakdown (CODE word, #215)

Converted from Forth to 6809 assembly.  All manhattan distance checks inlined using
unsigned abs-diff (BCC/NEGA).  No ITC overhead — pure register operations.

| Check | Per-item cost | Items | Subtotal |
|-------|---------------|-------|----------|
| Star loop | ~77cy/iter (SUBA, BCC, NEGA, TFR, ADDA, CMPA) | 5 | 385 |
| Black hole | BSR @mdst (~42cy) + CMPA | 1 | ~50 |
| Base | BSR @mdst + CMPA | 1 | ~50 |
| Ship | BSR @mdst + CMPA | 1 | ~50 |
| Other Jovians | ~116cy/iter (ABX, ASLB, inline manhattan, CMPA) | 2 | 232 |
| Setup + genome lookup + cleanup | | | ~338 |
| **Total per call (5 stars, 2 Jovians)** | | | **1,105** |

Previously: ~13,800cy/call in Forth (12.5x reduction).

`apply-intent` calls `jov-blocked?` 1-3 times (try both axes, x-only, y-only).
Worst case: 3 calls = 3,315cy.  `apply-intent` total (fc.py --cycles): 4,986cy upper bound.

## Appendix: 60-Frame Cycle Map (Moderate Difficulty)

Scenario: 3 Jovians (thresholds 4/6/3, staggered 0/1/2), 5 stars, black hole,
base, no active beam/missile.  Budget: **14,930 cy/frame**.

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
| 0 | E/G | 4436 | 4183 | 2132 | 1000 | | | 5626 | | | 7732 | 2046 | **27,155** | **OVER** |
| 1 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 2 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 3 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 4 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 5 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 6 | E | 4436 | | | 1000 | 5626 | | 5626 | | | 7732 | 2046 | **26,466** | **OVER** |
| 7 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 8 | E/G | 4436 | 4183 | 2132 | 1000 | | 5626 | | | | 7732 | 2046 | **27,155** | **OVER** |
| 9 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 10 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 11 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 12 | E | 4436 | | | 1000 | | | 5626 | | | 7732 | 2046 | **20,840** | **OVER** |
| 13 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 14 | E | 4436 | | | 1000 | 5626 | | | | | 7732 | 2046 | **20,840** | **OVER** |
| 15 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 16 | E/G | 4436 | 4183 | 2132 | 1000 | | | | | | 2644 | 2046 | **16,441** | **OVER** |
| 17 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 18 | E | 4436 | | | 1000 | | | 5626 | | | 7732 | 2046 | **20,840** | **OVER** |
| 19 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 20 | E | 4436 | | | 1000 | | 5626 | | | | 7732 | 2046 | **20,840** | **OVER** |
| 21 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 22 | E | 4436 | | | 1000 | 5626 | | | | | 7732 | 2046 | **20,840** | **OVER** |
| 23 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 24 | E/G | 4436 | 4183 | 2132 | 1000 | | | 5626 | | | 7732 | 2046 | **27,155** | **OVER** |
| 25 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 26 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 27 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 28 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 29 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 30 | E | 4436 | | | 1000 | 5626 | | 5626 | | | 7732 | 2046 | **26,466** | **OVER** |
| 31 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 32 | E/G | 4436 | 4183 | 2132 | 1000 | | 5626 | | | | 7732 | 2046 | **27,155** | **OVER** |
| 33 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 34 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 35 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 36 | E | 4436 | | | 1000 | | | 5626 | | | 7732 | 2046 | **20,840** | **OVER** |
| 37 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 38 | E | 4436 | | | 1000 | 5626 | | | | | 7732 | 2046 | **20,840** | **OVER** |
| 39 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 40 | E/G | 4436 | 4183 | 2132 | 1000 | | | | | | 2644 | 2046 | **16,441** | **OVER** |
| 41 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 42 | E | 4436 | | | 1000 | | | 5626 | | | 7732 | 2046 | **20,840** | **OVER** |
| 43 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 44 | E | 4436 | | | 1000 | | 5626 | | | | 7732 | 2046 | **20,840** | **OVER** |
| 45 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 46 | E | 4436 | | | 1000 | 5626 | | | | | 7732 | 2046 | **20,840** | **OVER** |
| 47 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 48 | E/G | 4436 | 4183 | 2132 | 1000 | | | 5626 | | | 7732 | 2046 | **27,155** | **OVER** |
| 49 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 50 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 51 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 52 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 53 | O | 4436 | | | | | | | 1200 | 3000 | 2644 | 2046 | **13,326** | |
| 54 | E | 4436 | | | 1000 | 5626 | | 5626 | | | 7732 | 2046 | **26,466** | **OVER** |
| 55 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |
| 56 | E/G | 4436 | 4183 | 2132 | 1000 | | 5626 | | | | 7732 | 2046 | **27,155** | **OVER** |
| 57 | O/G | 4436 | | | | | | | 5000 | 3000 | 7732 | 2046 | **22,214** | **OVER** |
| 58 | E | 4436 | | | 1000 | | | | | | 2644 | 2046 | **10,126** | |
| 59 | O | 4436 | | | | | | | 1200 | | 2644 | 2046 | **10,326** | |

### Summary (Moderate Difficulty, after #215 + #245)

All cycle counts from `fc.py --cycles` with measured primitive costs.  EF, Rnd, and
Post costs traced through idle game-loop paths using kernel primitive timing.

| Metric | Value | Before #215 |
|--------|-------|-------------|
| **Budget per frame** | 14,930 cy | 14,930 cy |
| **Light frame (no thinks, no gravity)** | 10,126 cy (68%) | ~9,700 cy |
| **1 Jovian think** | 20,840 cy (140%) | ~31,700 cy (213%) |
| **2 Jovians think (frame 6, 30, 54)** | 26,466 cy (177%) | ~48,700 cy (326%) |
| **Gravity even + 1 think** | 27,155 cy (182%) | ~38,000 cy (255%) |
| **Gravity even, no think** | 16,441 cy (110%) | ~21,000 cy (141%) |
| **Gravity odd + BG** | 22,214 cy (149%) | ~21,700 cy (145%) |
| **Gravity odd, no BG** | 19,214 cy (129%) | ~18,700 cy |
| **Frames over budget** | **29 of 60 (48%)** | 28 of 60 (47%) |
| **Frames under budget** | 31 of 60 (52%) | 32 of 60 (53%) |
| **Effective FPS** | ~40-45 fps | ~30-35 fps |

The count of over-budget frames is nearly unchanged because the gravity-odd frames
(22,214cy) were already over budget before #215.  However, the *magnitude* of
overruns dropped dramatically: worst-case frames went from 326% to 177% of budget.
The game no longer drops 2+ frames on a single think — overbudget frames now fit
in 2 VSYNC periods (~33ms) instead of 3-4.

Note: light frames are higher than the old estimate (10,126 vs 9,700) because the
old EF=5,000 was a rough guess.  Measured idle pre-VSYNC is 4,436cy; post-VSYNC
overhead (beam/panel early exits, latch-key, conditionals) adds 2,046cy; ship-only
render adds 2,644cy.  The old Rnd-S=2,200 and Post=1,500 underestimated Forth
dispatch overhead on early-exit paths.

### Remaining Bottlenecks

1. **Gravity odd + BG (21,700cy)**: Unchanged by #215.  `jov-gravity` (star distance
   loops) and background tasks dominate.  These frames always drop 1 VSYNC.

2. **`apply-intent` Forth overhead (~3,500cy)**: Now the dominant think cost.
   Converting to CODE would save ~2,500cy per think, bringing 1-think frames to
   ~18,000cy (closer to single-frame budget).

3. **Gravity even frames (16,000cy)**: Slightly over budget even without any thinks.
   Star gravity (4,200cy) + gravity well (2,100cy) = 6,300cy of gravity alone.

4. **CONSTANT overhead**: Still 80cy per reference in all Forth code.  Inlining
   constants as LIT values (31cy) would save ~49cy × ~100 refs = ~4,900cy/frame
   across all paths.
