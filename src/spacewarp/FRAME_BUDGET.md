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
| `apply-intent` | 1,369 | Intent application with inlined jov-blocked? (#241) |
| `move-ship` | 993 | Keyboard + try-move + ship-jov-blocked? (#244) |
| `jov-think` | 689 | Genome-driven AI, single Jovian |
| `gen-jov-sprite` | 627 | Contains loops |
| `beam-trace` | 619 | Contains loop |
| `rg-line` | 571 | Bresenham line draw |
| `ship-gravity` | 476 | Merged gravity-well + star-gravity (#242, grav frames) |
| `spr-draw` | 410 | Sprite blit with loops |
| `draw-stars` | 400 | Fixed-color star plot (#249) |
| `jov-gravity-pull` | 400 | Per-Jovian star+bhole gravity (#243, grav frames) |
| `prox-dmg` | 306 | Proximity scan loop |
| `jov-contact` | 300 | Ship-Jovian collision (#243, every frame) |
| `beam-draw-slice` | 285 | Beam segment draw |
| `beam-restore-slice` | 280 | Beam segment erase |
| `spr-erase-box` | 242 | Sprite erase |
| `plot-dots` | 242 | Explosion dots |
| `tick-jovians-inner` | 200 | Tick loop + inlined threshold (#166) |
| `jov-flee` | 201 | Flee movement calc |
| `rg-char` | 188 | Character draw (8-line loop) |
| `collision-scan` | 177 | Star/bhole collision loop |
| `bg-save` / `bg-restore` | 140 | Background save/restore |
| `bg-save-7` / `bg-restore-7` | 140 | 7-row variant |
| `save-jov-oldpos-n` | 82 | Position copy loop |

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

2. **Jovian think frequency**: At level 9, think thresholds are 2-3 (vs 4-6 at
   moderate levels).  All 3 Jovians can think on the same even frame, each adding
   ~2,058cy (jov-think 689 + apply-intent 1,369).

3. **Rendering path**: Full redraw (jov-moved) costs ~5,948cy.  Ship-only render
   costs ~2,447cy.

## Completed Optimizations

| # | What | Before | After | Savings |
|---|------|--------|-------|---------|
| #166 | `tick-jovians-inner` CODE | ~4,140cy Forth | 200cy | 3,940cy |
| #215 | `jov-blocked?` CODE (inlined into #241) | ~13,800cy Forth | inlined | -- |
| #241 | `apply-intent` CODE (inlines jov-blocked?) | ~4,937cy Forth | 1,369cy | 3,568cy/think |
| #242 | `ship-gravity` CODE (merges gravity-well + star-gravity) | 5,776cy | 476cy | 5,300cy/grav |
| #243 | `jov-contact` CODE + `jov-gravity-pull` CODE | ~5,000cy | 300+400cy | ~4,300cy |
| #244 | `move-ship` CODE | 1,633cy | 993cy | 640cy/frame |
| #245/#246 | All CONSTANTs inlined as LIT | 80cy/ref | 31cy/ref | ~4,900cy total |
| #247 | Deduplicate latch-key (3x to 1x) | 1,059cy | 353cy | 706cy/frame |
| #248 | `save/restore-jov-bgs` CODE — **REVERTED** (rendering bug) | — | — | — |
| #249 | `draw-stars` CODE with fixed colors | 1,840cy | 400cy | 1,440cy/render |
| #250 | `check-win` bug fix (count not every frame) | 2,443cy/frame | amortized | ~2,400cy saved |

Also fixed: #251 `apply-intent` CMPB 10,S→9,S (Jovians were frozen since #241).
#252 Sprite coordinate underflow clamping (BCS/CLRB).
#253 Two STA ,-S → PSHS A fixes.

## Remaining Targets

| Area | Notes |
|------|-------|
| #188 Sound | Feature, not optimization |
| #181 Emotion edge cases | Behavioral tuning |
| 2-think frames (~16,750cy) | Over budget (112%), acceptable |
| Background slots | Typical-path costs already reasonable (~3,000cy) |

## Measurement

Cycle counts generated by `fc.py --cycles`, which computes exact 6809 instruction
timing for CODE words and kernel primitives, and recursive ITC analysis for Forth
colon definitions.  Forth word costs for words with IF/ELSE branches represent the
sum of all paths (worst case upper bound); words without branches are exact.

Note: `fc.py --cycles` reports per-word costs without loop multiplication.  For
words containing DO/LOOP, the reported cost includes one iteration of the loop body.
The appendix below uses manually expanded costs with correct loop iteration counts.

## Appendix: Component Cost Table

Current measured costs after all optimizations (#166, #215, #241-#253).

| Component | Cycles | Derivation |
|-----------|--------|------------|
| Every-frame base | 3,645 | move-ship CODE(669) + process-key(785†) + save-ship-pos(374) + tick-systems(764†) + loop overhead(429†) + jov-contact CODE(294) |
| Ship gravity (grav frames) | 476 | ship-gravity CODE(473-476) (#242) |
| Check collisions | 1,833 | check-collisions(1,833): collision-scan CODE + Forth wrapper |
| Per-Jovian think | 1,446 | jov-think CODE(689-693) + apply-intent CODE(752-757 base) |
| Tick-jovians-inner | 326 | tick-jovians-inner CODE(324-326) (#166) |
| Jov-gravity-pull (grav frame) | 494 | jov-gravity-pull CODE(489-494) (#243) |
| Jov-gravity-pull (non-grav) | 30 | Early exit on grav-tick gate |
| Background slot 1 | — | jov-check-regen(1,818) + check-dock(2,790) + tick-dock(7,238†) + tick-base-attack(77,554†). Upper bounds; typical paths much cheaper. |
| Background slot 5 | — | tick-stardate(19,802†) + tick-migrate(26,321†) + update-cond(13,246†). Upper bounds; typical paths much cheaper. |
| Full render | 8,458 | draw-stars CODE(196) + draw-jovians-live(1,151) + draw-ship(819) + bg-jov Forth(save 900 + restore 909 + oldpos 191 = 2,000) + bg-ship(restore 476 + save 549 = 1,025) + overhead(475†) + post-render(2,510†) |
| Ship-only render | 4,957 | draw-ship(819) + bg-ship(1,025) + overhead(603†) + post-render(2,510†) |
| Post-render | 2,510† | beam-erase 2×(~424†) + beam-draw 2×(~424†) + apply-beam/jbeam(~471†) + panel 3×(~714†) + check-win(~126†) + overlay(~126†) |

†Upper bound or estimated idle-path cost; fc.py reports worst-case branch sums.
Actual idle-path costs traced manually from primitive timing.

### `apply-intent` Cost Breakdown (CODE word, #241)

Inlines full `jov-blocked?` obstacle check as @chk subroutine.  3-tier fallback:
try (nx,ny), then (nx,cur_y), then (cur_x,ny).  Each tier calls @chk.

| Check | Per-item cost | Items | Subtotal |
|-------|---------------|-------|----------|
| Star loop | ~77cy/iter | 5 | 385 |
| Black hole | BSR @cmd (~42cy) + CMPA | 1 | ~50 |
| Base | BSR @cmd + CMPA | 1 | ~50 |
| Ship | BSR @cmd + CMPA | 1 | ~50 |
| Other Jovians | ~116cy/iter | 2 | 232 |
| Setup + genome lookup + cleanup | | | ~338 |
| **Total per @chk call (5 stars, 2 Jovians)** | | | **~1,105** |

`apply-intent` calls @chk 1-3 times.  fc.py --cycles base: 752-757cy.
With loop iterations: ~1,449cy typical (1 @chk call + overhead).

### 60-Frame Cycle Map (Moderate Difficulty)

Scenario: 3 Jovians (thresholds 4/6/3, staggered 0/1/2), 5 stars, black hole,
base, no active beam/missile.  Budget: **14,930 cy/frame**.

Gravity gated to every 4th even frame (frames 0, 8, 16, 24, 32, 40, 48, 56).
Background tasks on odd frames: slot 1 at `N AND 7 = 1`, slot 5 at `N AND 7 = 5`.
jov-contact (294cy) runs every frame.  tick-jovians-inner (326cy) runs on even frames.

Jovian think schedule (tick increments each even frame, thinks when tick ≥ threshold):
- J0 (threshold 4, start 0): thinks at even frames 6, 14, 22, 30, 38, 46, 54
- J1 (threshold 6, start 1): thinks at even frames 8, 20, 32, 44, 56
- J2 (threshold 3, start 2): thinks at even frames 0, 6, 12, 18, 24, 30, 36, 42, 48, 54

Full render (Rnd-F) on even frames when a Jovian thinks (jov-moved=1).
Ship-only render (Rnd-S) on even frames with no thinks and on most odd frames.
Gravity odd frames get full render if jov-gravity-pull moves a Jovian.

Component abbreviations: EF=every-frame base(3,645), SGrv=ship-gravity(476),
Coll=check-collisions(1,833), TJov=tick-jovians-inner(326), J=per-think(1,449),
JCon=jov-contact(294, included in EF), JGrv=jov-gravity-pull(494/30),
BG=background(3,000†), Rnd-F=full render(8,458), Rnd-S=ship-only(4,957),
Post=post-render(included in Rnd).

| Fr | E/O | EF | SGrv | Coll | TJov | J0 | J1 | J2 | JGrv | BG | Rnd | **Total** | |
|----|-----|----|------|------|------|----|----|----|----|----|----|-----------|---|
| 0 | E/G | 3645 | 476 | 1833 | 326 | | | 1449 | | | 8458 | **16,187** | **OVER** |
| 1 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 2 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 3 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 4 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 5 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 6 | E | 3645 | | 1833 | 326 | 1449 | | 1449 | | | 8458 | **17,160** | **OVER** |
| 7 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 8 | E/G | 3645 | 476 | 1833 | 326 | | 1449 | | | | 8458 | **16,187** | **OVER** |
| 9 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 10 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 11 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 12 | E | 3645 | | 1833 | 326 | | | 1449 | | | 8458 | **15,711** | **OVER** |
| 13 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 14 | E | 3645 | | 1833 | 326 | 1449 | | | | | 8458 | **15,711** | **OVER** |
| 15 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 16 | E/G | 3645 | 476 | 1833 | 326 | | | | | | 4957 | **11,237** | |
| 17 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 18 | E | 3645 | | 1833 | 326 | | | 1449 | | | 8458 | **15,711** | **OVER** |
| 19 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 20 | E | 3645 | | 1833 | 326 | | 1449 | | | | 8458 | **15,711** | **OVER** |
| 21 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 22 | E | 3645 | | 1833 | 326 | 1449 | | | | | 8458 | **15,711** | **OVER** |
| 23 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 24 | E/G | 3645 | 476 | 1833 | 326 | | | 1449 | | | 8458 | **16,187** | **OVER** |
| 25 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 26 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 27 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 28 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 29 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 30 | E | 3645 | | 1833 | 326 | 1449 | | 1449 | | | 8458 | **17,160** | **OVER** |
| 31 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 32 | E/G | 3645 | 476 | 1833 | 326 | | 1449 | | | | 8458 | **16,187** | **OVER** |
| 33 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 34 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 35 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 36 | E | 3645 | | 1833 | 326 | | | 1449 | | | 8458 | **15,711** | **OVER** |
| 37 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 38 | E | 3645 | | 1833 | 326 | 1449 | | | | | 8458 | **15,711** | **OVER** |
| 39 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 40 | E/G | 3645 | 476 | 1833 | 326 | | | | | | 4957 | **11,237** | |
| 41 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 42 | E | 3645 | | 1833 | 326 | | | 1449 | | | 8458 | **15,711** | **OVER** |
| 43 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 44 | E | 3645 | | 1833 | 326 | | 1449 | | | | 8458 | **15,711** | **OVER** |
| 45 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 46 | E | 3645 | | 1833 | 326 | 1449 | | | | | 8458 | **15,711** | **OVER** |
| 47 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 48 | E/G | 3645 | 476 | 1833 | 326 | | | 1449 | | | 8458 | **16,187** | **OVER** |
| 49 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 50 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 51 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 52 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 53 | O | 3645 | | | | | | | 30 | 3000 | 4957 | **11,632** | |
| 54 | E | 3645 | | 1833 | 326 | 1449 | | 1449 | | | 8458 | **17,160** | **OVER** |
| 55 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |
| 56 | E/G | 3645 | 476 | 1833 | 326 | | 1449 | | | | 8458 | **16,187** | **OVER** |
| 57 | O/G | 3645 | | | | | | | 494 | 3000 | 8458 | **15,597** | **OVER** |
| 58 | E | 3645 | | 1833 | 326 | | | | | | 4957 | **10,761** | |
| 59 | O | 3645 | | | | | | | 30 | | 4957 | **8,632** | |

Also see `frame_budget_chart.html` for the interactive visual chart.

### Summary (after #166, #215, #241-#253)

| Metric | Current | Before optimization |
|--------|---------|---------------------|
| **Budget per frame** | 14,930 cy | 14,930 cy |
| **Light frame (even, no thinks, no gravity)** | 10,761 cy (72%) | 10,126 cy (68%) |
| **1 Jovian think (even)** | 15,711 cy (105%) | 20,840 cy (140%) |
| **2 Jovians think (frame 6, 30, 54)** | 17,160 cy (115%) | 26,466 cy (177%) |
| **Gravity even + 1 think** | 16,187 cy (108%) | 27,155 cy (182%) |
| **Gravity even, no think** | 11,237 cy (75%) | 16,441 cy (110%) |
| **Gravity odd + BG** | 15,597 cy (104%) | 22,214 cy (149%) |
| **Gravity odd, no BG** | 12,597 cy (84%) | 19,214 cy (129%) |
| **Frames over budget** | **22 of 60 (37%)** | 29 of 60 (48%) |
| **Frames under budget** | 38 of 60 (63%) | 31 of 60 (52%) |
| **App size** | 24,080 bytes | 24,501 bytes |
| **Headroom** | 496 bytes | 75 bytes |

Worst-case frames dropped from 177% to 115% of budget.  The game no longer drops
2+ frames on a single think.  Over-budget frames fit in 2 VSYNC periods (~33ms).

### Remaining Bottlenecks

1. **Full render (8,458cy)**: The dominant cost on think frames.  bg-jov Forth
   (save 900 + restore 909 + oldpos 191 = 2,000cy) is the largest sub-component.
   Converting to CODE (#248, needs @bgcalc debug) would save ~1,200cy.

2. **Gravity odd + BG (15,597cy)**: jov-gravity-pull(494) + BG tasks(3,000†) +
   full render(8,458) + EF(3,645).  BG tasks are upper bounds; typical paths
   are much cheaper but occasionally spike.

3. **1-think frames (15,711cy, 105%)**: Just over budget.  Deferring 2nd think
   to next frame (1-think-per-frame cap) would eliminate 2-think frames entirely,
   but lwasm branch range issues prevented implementation.  Revisit.
