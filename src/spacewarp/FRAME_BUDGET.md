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

### 60-Frame Cycle Map

See `frame_budget_chart.html` for the interactive chart.

### Summary (after all optimizations)

| Metric | Current | Before optimization |
|--------|---------|---------------------|
| **Budget per frame** | 14,930 cy | 14,930 cy |
| **Light frame (even, no thinks, no gravity)** | see chart | ~10,126 cy (68%) |
| **1 Jovian think (even)** | see chart | ~20,840 cy (140%) |
| **2 Jovians think (even)** | see chart | ~26,466 cy (177%) |
| **Gravity even + 1 think** | see chart | ~27,155 cy (182%) |
| **Gravity even, no think** | see chart | ~16,441 cy (110%) |
| **App size** | 24,080 bytes | 24,501 bytes |
| **Headroom** | 496 bytes | 75 bytes |

See `frame_budget_chart.html` for exact per-frame totals.  Most frames now fit within
the 14,930cy budget.  The chart uses idle-path costs (manually traced from primitive
timing) since fc.py reports worst-case branch sums.  Only 2-think even frames
consistently exceed budget.  This is a dramatic improvement from the pre-optimization
state where 29 of 60 frames (48%) exceeded budget with worst cases at 177-326%.
