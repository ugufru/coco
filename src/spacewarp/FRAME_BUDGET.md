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
| `move-ship` | 922 | Keyboard + velocity update + try-move + ship-jov-blocked? (#244, #343) |
| `jov-think` | 689 | Genome-driven AI, single Jovian |
| `gen-jov-sprite` | 627 | Contains loops |
| `beam-trace` | 619 | Contains loop |
| `rg-line` | 571 | Bresenham line draw |
| `ship-gravity` | 466 | Merged gravity-well + star-gravity, velocity-based (#242, #343) |
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
| `save-ship-bg` | 172 | Inlined bg-save + jbeam-fill gate (#350) |
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

| #349 | Beam/hit idle guards — gate 6 calls with 3 IF checks | 1,544cy idle | 477cy idle | 1,067cy/frame (idle) |
| #350 | `save-ship-bg` CODE — inline bg-save + jbeam fill | 549cy Forth | 172cy CODE | 377cy/frame |

Also fixed: #251 `apply-intent` CMPB 10,S→9,S (Jovians were frozen since #241).
#252 Sprite coordinate underflow clamping (BCS/CLRB).
#253 Two STA ,-S → PSHS A fixes.

## Size Optimizations

| # | What | Savings |
|---|------|---------|
| #268 | Deduplicate repeated string literals | ~161 bytes |
| #280 | Factor `cancel-jbeam cancel-beam` → `cancel-beams` (7 sites) | 6 bytes |
| #281 | Factor backdrop redraw sequence → `draw-backdrop` (5 sites) | 18 bytes |
| #282 | Factor `pcol @ prow @` → `here@` (12 sites) | 54 bytes |
| #283 | Factor clamp patterns → `0max` / `1max` (11 sites) | 132 bytes |

App size: 24,533 bytes at $2000. Headroom to $8000: **~18 bytes**.

## Combat Rebalance Frame Impact (#306, #312-315, #338-348)

The combat rebalance added deflector toggle, shield bleedthrough, damage spread,
repair priority queue, ion disable, and docking shield check.  No new CODE words
were added — all changes are Forth-level logic.

| New work | When it runs | Idle-path cost | Active-path cost |
|----------|-------------|----------------|-----------------|
| `tick-energy` | Every frame | ~175cy (not docked, not 16th frame) | ~800cy (16th frame, all healthy) |
| `tick-dock` | Every frame | ~212cy (not docked) | ~2,528cy (docked, 16th frame: drep × 5 systems) |
| `repair-any` | Every 16 frames from tick-energy | ~600-800cy (all healthy) | ~1,300cy (one system damaged, field cap 75%) |
| `take-damage` | On hit only (event-driven) | 0 | ~1,500-2,000cy |
| `check-dock` | Every 8 frames (BG slot 1) | ~300cy (no base) | ~2,926cy (near base, shield check) |

The every-frame base cost increased by ~387cy (tick-energy idle + tick-dock idle).
This brings the every-frame base from 3,645cy to ~4,032cy.  Still well within budget.

On the 16th-frame repair tick (not docked): +600-800cy additional.  Once every 16
frames = negligible average impact.  When docked, tick-dock uses drep × 5 systems
(~2,528cy) — higher burst but infrequent and only while stationary.

## V0.92 Gameplay Features — Frame Impact

New gameplay features added with zero per-frame idle-path cost (all event-driven):

| Feature | When it runs | Cost |
|---------|-------------|------|
| `jov-edge-flee` (#185) | On fleeing Jovian think tick at screen edge | ~48,661cy (includes refresh-after-kill) |
| `beam-hit-base?` (#323) | On maser beam completion (no Jovian hit) | ~1,565cy |
| `jov-flee-to` (#185) | On Jovian reaching screen edge | ~11,336cy (includes galaxy migration) |
| `destroy-base` (#323) | On friendly fire hit | ~54,838cy (one-shot) |
| `check-sos` (#317) | Once per stardate (~60s) | ~14,614cy |

None of these affect the per-frame budget.  They fire only on specific game events.

## V0.93 Features — Frame Impact

| Feature | When it runs | Cost |
|---------|-------------|------|
| `jov-pos-addr` (#286) | N/A — code factoring only | Zero (same logic, 86 bytes saved) |
| Handedness avoidance (#213) | On blocked-path in `apply-intent` CODE | ~20cy (one genome byte read + branch) |
| Scanner degradation (#310) | Only when viewing LRS overlay | ~30cy per cell (64 rnd + compare) |

None affect per-frame budget. #286 is pure size savings. #213 adds ~20cy only on
the already-expensive blocked path (tier 1 fail). #310 runs only in the LRS overlay.
App: 24,543 bytes, headroom ~8 bytes.

## Remaining Targets

| Area | Notes |
|------|-------|
| #188 Sound | Feature, not optimization (blocked by all-RAM mode) |
| #279 | Factor remaining repeated code patterns | Space recovery |
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

Current measured costs after all optimizations (#166, #215, #241-#253, #306-#350).

| Component | Cycles | Derivation |
|-----------|--------|------------|
| Every-frame base | 3,961 | move-ship CODE(598) + process-key(785†) + save-ship-pos(374) + tick-systems(764†) + loop overhead(429†) + jov-contact CODE(294) + tick-energy(175†) + tick-dock(212†) + tick-destruct(~130†) (#343: velocity-based move) |
| Ship gravity (grav frames) | 466 | ship-gravity CODE(466-469), velocity-based @pull (#242, #343) |
| Check collisions (odd frames) | 1,833 | check-collisions(1,833): collision-scan CODE + Forth wrapper. Moved to odd frames to balance even-frame think load. |
| Per-Jovian think | 1,446 | jov-think CODE(689-693) + apply-intent CODE(752-757 base) |
| Tick-jovians-inner (fire) | ~140 | Slot-based: advance slot + alive/aware + skip counter + genome → fire + mask |
| Tick-jovians-inner (skip) | ~110 | Slot-based: advance slot + alive/aware + skip counter → no fire |
| Jov-gravity-pull (grav frame) | 494 | jov-gravity-pull CODE(489-494) (#243) |
| Jov-gravity-pull (non-grav) | 30 | Early exit on grav-tick gate |
| Background slot 1 (`AND 7 = 7`) | 1,360† | jov-check-regen(~500†) + check-dock(~300†) + tick-dock(~30† not docked) + tick-base-attack(~500†). Moved from `= 3` to `= 7` to avoid gravity collision (#284). |
| Background slot 2 (`AND 7 = 5`) | 1,230† | tick-stardate(~200† not firing) + tick-migrate(~200† not firing) + check-spawn(~30†) + update-cond(~800†). |
| Full render | 7,267 | draw-stars CODE(196) + draw-base(535) (#359) + draw-jovians-live(1,151) + draw-ship(819) + bg-jov Forth(save 900 + restore 909 + oldpos 191 = 2,000) + bg-ship(restore 476 + save CODE 172 = 648) (#350) + overhead(475†) + post-render(1,443†) (#349) |
| Ship-only render | 4,244 | draw-stars(196) (#362) + draw-base(535) (#359) + draw-ship(819) + bg-ship(648) (#350) + overhead(603†) + post-render(1,443†) (#349) |
| Post-render | 1,443† | beam guards 3×(~477† idle) (#349) + panel 3×(~714†) + check-win(~126†) + overlay(~126†) |

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
Background tasks on odd frames: slot 1 at `N AND 7 = 7` (#284), slot 2 at `N AND 7 = 5`.
BG slot 1 moved from `= 3` to `= 7` to avoid collision with gravity frames (#284).
jov-contact (294cy) included in EF.  tick-jovians-inner (~165cy†) runs on even frames.

Component abbreviations: EF=every-frame base(4,032), SGrv=ship-gravity(476),
Coll=check-collisions(1,833, odd frames only),
TJov=tick-jovians-inner(~140 fire/~110 skip), J=per-think(1,449),
JGrv=jov-gravity-pull(494/30), BG=background(slot1: 1,360†, slot2: 1,230†),
Rnd-F=full render(8,458), Rnd-S=ship-only(4,957).

| Fr | E/O | EF | SGrv | TJov | Think | Coll | JGrv | BG | Rnd | **Total** | |
|----|-----|----|------|------|-------|------|------|----|-----|-----------|---|
| 0 | E/G | 3961 | 466 | 140 | J1 1449 |  |  |  | 7267 | **13,283** | |
| 1 | O/G | 3961 |  |  |  | 1833 | 494 |  | 7267 | **13,555** | |
| 2 | E | 3961 |  | 140 | J2 1449 |  |  |  | 7267 | **12,817** | |
| 3 | O | 3961 |  |  |  | 1833 | 30 |  | 4244 | **10,068** | |
| 4 | E | 3961 |  | 140 | J0 1449 |  |  |  | 7267 | **12,817** | |
| 5 | O | 3961 |  |  |  | 1833 | 30 | 1230 | 4244 | **11,298** | |
| 6 | E | 3961 |  | 140 | J1 1449 |  |  |  | 7267 | **12,817** | |
| 7 | O | 3961 |  |  |  | 1833 | 30 | 1360 | 4244 | **11,428** | |
| 8 | E/G | 3961 | 466 | 140 | J2 1449 |  |  |  | 7267 | **13,283** | |
| 9 | O/G | 3961 |  |  |  | 1833 | 494 |  | 7267 | **13,555** | |
| 10-15 | | (repeats: E=12,817 / O=10,068 or 11,298 or 11,428) | | | | | | | | |
| 16 | E/G | 3961 | 466 | 140 | J0 1449 |  |  |  | 7267 | **13,283** | |
| 17 | O/G | 3961 |  |  |  | 1833 | 494 |  | 7267 | **13,555** | |
| 18-23 | | (repeats) | | | | | | | | |
| 24 | E/G | 3961 | 466 | 140 | J1 1449 |  |  |  | 7267 | **13,283** | |
| 25 | O/G | 3961 |  |  |  | 1833 | 494 |  | 7267 | **13,555** | |
| 26-55 | | (repeats 8-frame pattern) | | | | | | | | |
| 56 | E/G | 3961 | 466 | 140 | J2 1449 |  |  |  | 7267 | **13,283** | |
| 57 | O/G | 3961 |  |  |  | 1833 | 494 |  | 7267 | **13,555** | |
| 58 | E | 3961 |  | 140 | J0 1449 |  |  |  | 7267 | **12,817** | |
| 59 | O | 3961 |  |  |  | 1833 | 30 |  | 4244 | **10,068** | |

Key observation: **all frames under budget** (14,930cy).  #359/#362 added draw-base
(535cy) and draw-stars (196cy) to both render paths to prevent backdrop erasure.
Peak frame is the gravity odd frame at 13,555cy (90.8%).
**Zero over-budget frames.**

Also see `frame_budget_chart.html` for the interactive visual chart.

### Summary (after #349-350 optimizations, combat rebalance #306-348, slot-based scheduling)

| Metric | Current | Pre-#349/350 | Pre-rebalance | Before optimization |
|--------|---------|--------------|---------------|---------------------|
| **Budget per frame** | 14,930 cy | 14,930 cy | 14,930 cy | 14,930 cy |
| **Even, 1 think, no gravity** | 12,817 cy (86%) | 14,079 cy (94%) | 13,692 cy (92%) | 20,840 cy (140%) |
| **Even, 1 think + gravity** | 13,283 cy (89%) | 14,555 cy (98%) | 14,168 cy (95%) | 27,155 cy (182%) |
| **Gravity odd + Coll** | 13,555 cy (91%) | 14,817 cy (99%) | 14,430 cy (97%) | 22,214 cy (149%) |
| **BG1 odd + Coll** | 11,428 cy (77%) | 12,212 cy (82%) | 11,825 cy (79%) | — |
| **Light odd + Coll** | 10,068 cy (67%) | 10,852 cy (73%) | 10,465 cy (70%) | — |
| **Frames over budget (L10)** | **0 of 60 (0%)** | 0 of 60 (0%) | 0 of 60 (0%) | 29 of 60 (48%) |
| **Peak frame cost** | **13,555 cy (91%)** | 14,817 cy (99%) | 14,430 cy (97%) | 27,155 cy (182%) |
| **Jitter (peak - trough, even)** | 476 cy (gravity only) | 476 cy | 476 cy | 17,029 cy |

tick-jovians-inner measured at 254cy by `fc.py --cycles` (path sum / upper bound).
Actual execution: ~140cy (fire path) / ~110cy (skip path), no loop.

**Key improvement**: #349 beam idle guards eliminated 1,067cy/frame by gating 6
beam/hit word calls with 3 IF checks (idle frames skip all beam dispatch).  #350
converted save-ship-bg from Forth wrapper (549cy) to CODE (172cy), saving 377cy/frame.
Combined savings: ~1,444cy/frame, dropping peak from 14,817cy (99%) to 13,091cy (88%).
This opens ~1,839cy of headroom on the peak frame for new gameplay features.

At moderate difficulty (mixed skip factors), some even frames skip their Jovian's
turn, producing a mix of 13,805cy and 9,101cy frames — but never exceeding
13,805cy.  The ceiling is hard-capped at 1 think per even frame.

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

1. **Full render (7,267cy)**: The dominant cost on think frames.  bg-jov Forth
   (save 900 + restore 909 + oldpos 191 = 2,000cy) is the largest sub-component.
   Converting to CODE (#248, needs @bgcalc debug) would save ~1,200cy.
   draw-base(535cy) added every frame to prevent erasure by beams/missiles (#359).

2. **Gravity odd frames (13,555cy, 91%)**: jov-gravity-pull(494) +
   check-collisions(1,833) + full render(7,267) + EF(3,961).  1,375cy headroom.

3. **Even think + gravity (13,283cy, 89%)**: ship-gravity(466) + think(1,449) +
   tick-jovians-inner(140) + full render(7,267) + EF(3,961).  1,647cy headroom.
