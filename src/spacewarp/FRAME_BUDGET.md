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
| `tick-jovians-inner` | 254 | Slot-based scheduling; 254=path sum, actual ~110-140/call |
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

1. **Even vs odd frames**: Even frames do physics+AI, odd frames do collisions+gravity+BG.
   check-collisions (1,833cy) moved to odd frames to balance load.

2. **Jovian think frequency**: Slot-based scheduling assigns each even frame to
   exactly one Jovian (`think-slot` rotates 0→1→2→0...).  The genome-derived skip
   factor (1-6) controls how many of its slots each Jovian actually uses.  At most
   1 Jovian thinks per even frame — zero collisions by construction.

3. **Rendering path**: Full redraw (jov-moved) costs ~8,458cy.  Ship-only render
   costs ~4,957cy.  A think that moves a Jovian triggers full render (+3,501cy).

## Completed Optimizations

| # | What | Before | After | Savings |
|---|------|--------|-------|---------|
| #166 | `tick-jovians-inner` CODE (slot-based rewrite) | 200cy (old CODE) | ~110-140cy | 60-90cy/frame + eliminates 2-think frames |
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
| Background slots | Typical-path costs already reasonable (~3,000cy) |

Note: 2-think frames are eliminated by slot-based scheduling.  Peak even frame
is now 1-think (~15,550cy = 104%), uniform at level 10.

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
| Check collisions (odd frames) | 1,833 | check-collisions(1,833): collision-scan CODE + Forth wrapper. Moved to odd frames to balance even-frame think load. |
| Per-Jovian think | 1,446 | jov-think CODE(689-693) + apply-intent CODE(752-757 base) |
| Tick-jovians-inner (fire) | ~140 | Slot-based: advance slot + alive/aware + skip counter + genome → fire + mask |
| Tick-jovians-inner (skip) | ~110 | Slot-based: advance slot + alive/aware + skip counter → no fire |
| Jov-gravity-pull (grav frame) | 494 | jov-gravity-pull CODE(489-494) (#243) |
| Jov-gravity-pull (non-grav) | 30 | Early exit on grav-tick gate |
| Background slot 1 | 1,360† | jov-check-regen(~500†) + check-dock(~300†) + tick-dock(~30† not docked) + tick-base-attack(~500†). fc.py upper bounds: 1,818 + 2,790 + 7,238 + 77,554. |
| Background slot 5 | 1,230† | tick-stardate(~200† not firing) + tick-migrate(~200† not firing) + check-spawn(~30†) + update-cond(~800†). fc.py upper bounds: 19,802 + 26,321 + 13,246. |
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

### Slot-Based Think Scheduling

The old threshold system incremented per-Jovian tick counters each even frame,
firing when tick >= threshold.  With fast thresholds (2-3), multiple Jovians could
fire on the same frame, causing 2-think spikes at 17,160cy (115%).

The new slot-based system assigns each even frame to exactly one Jovian via a
rotating `think-slot` counter (0→1→2→0...).  A genome-derived **skip factor**
(1-6) controls how many of its turns each Jovian actually uses:

```
skip = (12 - (pilot_skill + speed_mod)) >> 1, clamped [1, 6]
```

| Sum (skill+speed) | Skip | Interval (even frames) | Thinks/sec | Reaction |
|-------------------|------|----------------------|-----------|----------|
| 10 (genius) | 1 | 3 | 10.0 | 100ms |
| 8 | 2 | 6 | 5.0 | 200ms |
| 6 | 3 | 9 | 3.33 | 300ms |
| 4 | 4 | 12 | 2.5 | 400ms |
| 2 | 5 | 15 | 2.0 | 500ms |
| 0 (dullest) | 6 | 18 | 1.67 | 600ms |

**Zero collisions by construction**: at most 1 Jovian is ever considered per frame.

### 60-Frame Cycle Map (Level 10 — Slot-Based)

Scenario: 3 Jovians (all skip=1), 5 stars, black hole, base, no active beam/missile.
Budget: **14,930 cy/frame**.  think-slot starts at 0, stagger counters 0/1/2.

With skip=1, every Jovian fires on every slot.  Slot rotation: J1, J2, J0, J1, J2, J0...
Every even frame has exactly 1 think.  No 2-think frames ever.

Gravity gated to every 4th even frame (frames 0, 8, 16, 24, 32, 40, 48, 56).
Background tasks on odd frames: slot 1 at `N AND 7 = 1`, slot 5 at `N AND 7 = 5`.
jov-contact (294cy) included in EF.  tick-jovians-inner (~165cy†) runs on even frames.

Component abbreviations: EF=every-frame base(3,645), SGrv=ship-gravity(476),
Coll=check-collisions(1,833, odd frames only),
TJov=tick-jovians-inner(~140 fire/~110 skip), J=per-think(1,449),
JGrv=jov-gravity-pull(494/30), BG=background(slot1: 1,360†, slot5: 1,230†),
Rnd-F=full render(8,458), Rnd-S=ship-only(4,957).

| Fr | E/O | EF | SGrv | TJov | Think | Coll | JGrv | BG | Rnd | **Total** | |
|----|-----|----|------|------|-------|------|------|----|-----|-----------|---|
| 0 | E/G | 3645 | 476 | 140 | J1 1449 |  |  |  | 8458 | **14,168** | |
| 1 | O/G | 3645 |  |  |  | 1833 | 494 | 1360 | 8458 | **15,790** | **OVER** |
| 2 | E | 3645 |  | 140 | J2 1449 |  |  |  | 8458 | **13,692** | |
| 3 | O | 3645 |  |  |  | 1833 | 30 |  | 4957 | **10,465** | |
| 4 | E | 3645 |  | 140 | J0 1449 |  |  |  | 8458 | **13,692** | |
| 5 | O | 3645 |  |  |  | 1833 | 30 | 1230 | 4957 | **11,695** | |
| 6 | E | 3645 |  | 140 | J1 1449 |  |  |  | 8458 | **13,692** | |
| 7 | O | 3645 |  |  |  | 1833 | 30 |  | 4957 | **10,465** | |
| 8 | E/G | 3645 | 476 | 140 | J2 1449 |  |  |  | 8458 | **14,168** | |
| 9 | O/G | 3645 |  |  |  | 1833 | 494 | 1360 | 8458 | **15,790** | **OVER** |
| 10-15 | | (repeats: E=13,692 / O=10,465 or 11,695) | | | | | | | | |
| 16 | E/G | 3645 | 476 | 140 | J0 1449 |  |  |  | 8458 | **14,168** | |
| 17 | O/G | 3645 |  |  |  | 1833 | 494 | 1360 | 8458 | **15,790** | **OVER** |
| 18-23 | | (repeats) | | | | | | | | |
| 24 | E/G | 3645 | 476 | 140 | J1 1449 |  |  |  | 8458 | **14,168** | |
| 25 | O/G | 3645 |  |  |  | 1833 | 494 | 1360 | 8458 | **15,790** | **OVER** |
| 26-55 | | (repeats 8-frame pattern) | | | | | | | | |
| 56 | E/G | 3645 | 476 | 140 | J2 1449 |  |  |  | 8458 | **14,168** | |
| 57 | O/G | 3645 |  |  |  | 1833 | 494 | 1360 | 8458 | **15,790** | **OVER** |
| 58 | E | 3645 |  | 140 | J0 1449 |  |  |  | 8458 | **13,692** | |
| 59 | O | 3645 |  |  |  | 1833 | 30 |  | 4957 | **10,465** | |

Key observation: **all even frames under budget**.  Moving check-collisions to
odd frames shifts 1,833cy off the critical think path.  Only the 8 gravity+BG1
odd frames (1, 9, 17, 25, 33, 41, 49, 57) exceed budget at 15,790cy (106%).

Also see `frame_budget_chart.html` for the interactive visual chart.

### Summary (slot-based scheduling, after #166, #215, #241-#253)

| Metric | Slot-based | Old threshold | Before optimization |
|--------|-----------|---------------|---------------------|
| **Budget per frame** | 14,930 cy | 14,930 cy | 14,930 cy |
| **Even, 1 think, no gravity** | 13,692 cy (92%) | 15,711 cy (105%) | 20,840 cy (140%) |
| **Even, 1 think + gravity** | 14,168 cy (95%) | 16,187 cy (108%) | 27,155 cy (182%) |
| **2 Jovians think (eliminated)** | — | 17,160 cy (115%) | 26,466 cy (177%) |
| **Even, no think (skip > 1)** | 8,712 cy (58%) | 10,761 cy (72%) | 10,126 cy (68%) |
| **Gravity odd + BG1 + Coll** | 15,790 cy (106%) | 13,957 cy (93%) | 22,214 cy (149%) |
| **Light odd + Coll** | 10,465 cy (70%) | 8,632 cy (58%) | — |
| **Frames over budget (L10)** | **8 of 60 (13%)** | 19-30 of 60 | 29 of 60 (48%) |
| **Peak frame cost** | **15,790 cy (106%)** | 17,160 cy (115%) | 27,155 cy (182%) |
| **Jitter (peak - trough, even)** | 476 cy (gravity only) | 6,399 cy | 17,029 cy |

tick-jovians-inner measured at 254cy by `fc.py --cycles` (path sum / upper bound).
Actual execution: ~140cy (fire path) / ~110cy (skip path), no loop.

**Key improvement**: At level 10, jitter between even frames drops from 6,399cy
(the swing between 10,761 and 17,160) to just 476cy (the gravity cost on every
4th even frame).  All even frames have nearly identical cost.

At moderate difficulty (mixed skip factors), some even frames skip their Jovian's
turn, producing a mix of 15,550cy and 10,545cy frames — but never exceeding
15,550cy.  The ceiling is hard-capped at 1 think per even frame.

### Think Frequency by Difficulty Level

| Level | Skill bias | Speed bias | Typical sum | Skip | Thinks/sec | Reaction |
|-------|-----------|-----------|-------------|------|-----------|----------|
| 1 | +0 | +0 | 3-5 | 4-5 | 2.0-2.5 | 400-500ms |
| 3 | +1 | +0 | 4-6 | 3-4 | 2.5-3.3 | 300-400ms |
| 5 | +2 | +1 | 5-8 | 2-4 | 2.5-5.0 | 200-400ms |
| 7 | +3 | +2 | 7-9 | 1-3 | 3.3-10.0 | 100-300ms |
| 10 | +4 | +3 | 9-10 | 1 | 10.0 | 100ms |

6:1 range (1.67-10.0 thinks/sec) vs old 4:1 range (3.75-15.0 thinks/sec).
Dumb Jovians are genuinely sluggish; smart ones are still snappy.

### Remaining Bottlenecks

1. **Full render (8,458cy)**: The dominant cost on think frames.  bg-jov Forth
   (save 900 + restore 909 + oldpos 191 = 2,000cy) is the largest sub-component.
   Converting to CODE (#248, needs @bgcalc debug) would save ~1,200cy.

2. **Gravity odd + BG + Coll (15,790cy, 106%)**: jov-gravity-pull(494) +
   BG tasks(1,360†) + check-collisions(1,833) + full render(8,458) + EF(3,645).
   The only frames over budget.  8 of 60 frames (13%).

3. **Even think frames now under budget**: 13,692cy (92%) without gravity,
   14,168cy (95%) with.  Moving check-collisions to odd frames freed 1,833cy.
