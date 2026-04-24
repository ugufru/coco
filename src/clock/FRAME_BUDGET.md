# Clock Frame Budget

**CPU: 6809 @ 0.8949 MHz (14.31818 MHz crystal ÷ 16)**
**NTSC field rate: 59.94 Hz → ~14,917 cycles/frame budget**

At the NTSC field rate of 59.94 Hz, the 6809 has ~14,917 CPU cycles
between VSYNC events.  The clock's main loop must complete all work
within this budget, or frames drop and the local clock runs slow.

The clock is arguably the most CPU-sensitive Forth demo in the repo:
all three hands smooth-sweep (proportional to wall time, no visible
tick events), the digital readout updates in three independent groups,
and everything has to fit in one vblank between page flips.  Several
rounds of optimization (issues #452, #448, #453, #455) got us there.

## ITC Threading Overhead

All cycle counts computed by `fc.py --cycles` from the MC6809E
instruction timing tables.

| Mechanism | Cycles | What it does |
|-----------|--------|-------------|
| **NEXT** | 15 | `LDY ,X++` (9) + `JMP [,Y]` (6) — dispatches next word |
| **DOCOL** | 27 | `PSHS X` (7) + `LEAX 2,Y` (5) + NEXT (15) — enters colon def |
| **EXIT** | 22 | `PULS X` (7) + NEXT (15) — returns from colon def |
| **DOVAR** | 29 | `LEAY 2,Y` (5) + `STY ,--U` (9) + NEXT (15) — pushes var addr |
| **LIT** | 31 | In-line constant push |

Every colon word call pays DOCOL + EXIT = **49cy** overhead before
doing any work.  Every VARIABLE reference costs **29cy** just to push
its address.

## Clock-Specific CODE Words

Assembly CODE words bypass ITC overhead — their costs are exact:

| CODE Word | Cycles | Notes |
|-----------|--------|-------|
| `flip-state` | 341 | 15 × (LDD/LDX/STX/STD) swapping fr/bk pointer pairs + all 3 hands' lasts |
| `compute-angles` | 282 | Full sc/mn/hr angle calc as one CODE word (#455) |
| `set-sam-f-fast` | 169 | Unrolled 7-bit SAM-F write for the page flip |
| `dw-read` | 114 | HDB-DOS DWRead vector wrapper |
| `dw-write` | 95 | HDB-DOS DWWrite vector wrapper |
| `*-tx-tab` / `*-ty-tab` | 29 each | Push address of precomputed 360-byte endpoint table (sec/mn/hr) |
| `time-buf` | 29 | Push address of 6-byte time buffer |

All within the vblank window (~680 cycles), so the page flip is
atomic from the raster's perspective.

## Clock-Specific Forth Words (measured, upper bounds)

| Word | Cycles | Runs when |
|------|--------|-----------|
| `redraw-hands` | 8,251 | Worst case (hr+mn+sc all moved); typical mid-frame is base + dedup ~1,900cy |
| `tick-hands` | 5,410 | Boot only via paint-back-full — full repaint of all 3 hands |
| `render-datetime` | 7,223 | Full repaint (boot only) |
| `trace-line` | 6,720 | Used by face-circle / face-ticks (init) |
| `ep1` / `ep2` | 5,923 | Used by face-circle / face-ticks (init) |
| `sync-from-fn` | 5,387 | Once per minute (production) or boot (fake-time) |
| `render-date` | 4,301 | Day/boot only (see #448) |
| `tick-frame` | 2,897 | Every frame (worst case — rollover) |
| `angle-dx` | 2,824 | Used by ep1 / ep2 |
| `angle-dy` | 2,741 | Used by ep1 / ep2 |
| `tick-second` | 2,376 | Rollover only |
| `render-year` | 2,278 | Part of render-date |
| `hr-angle` | 1,924 | Boot only since #455 — per-frame work folded into `compute-angles` |
| `render-hm` | 1,961 | Minute/boot only (see #448) |
| `render-sync-flash` | 1,780 | With ss-pending |
| `mn-angle` | 1,268 | Boot only since #455 |
| `render-ss` | 912 | Every sec change |
| `sc-angle` | 751 | Boot only since #455 |
| `tick-frame` (no rollover) | ~304 | Every frame typical |
| `sc-angle` | 751 | Every frame |
| `vsync+` | 152 | Every frame (plus the vblank wait itself) |

## Why ep2 Is Expensive (~6,000cy)

`ep2` computes the endpoint of a hand in a single call:

```
: ep2  ( angle len -- )
  2DUP angle-dx 2/ CX + tx2 !
  angle-dy CY + ty2 ! ;
```

The cost decomposes as:

| Component | Cycles |
|-----------|--------|
| 2DUP | 43 |
| angle-dx | 2,824 (sin table lookup + MUL + signed /128) |
| 2/, CX, +, tx2 ! | ~150 |
| angle-dy | 2,741 |
| CY, +, ty2 ! | ~110 |
| DOCOL/EXIT | 49 |
| **Total** | **~5,917** |

`angle-dx`/`angle-dy` each perform a 7-bit sin lookup (via the `sin`
word at 1,889cy) plus a 16x16 multiply and signed shift.  That's half
the cost of the whole hand render.

A 6809 CODE word implementation could cut this to ~500cy — future
optimization opportunity (see Remaining Targets).

## Frame Composition (post-#452/#448/#453/#454/#455)

### Per-frame baseline (every frame, regardless of what changed)

| Component | Cycles | Notes |
|-----------|--------|-------|
| `vsync+` | 152 | wait + count |
| `set-sam-f-fast` | 169 | page flip CODE |
| `flip-state` | 341 | 15 pair-swaps (vram, hr/mn/sc bufs+lens+lasts) |
| `tick-frame` (no rollover) | ~304 | vs-cnt += 16 |
| KVAR-RGVRAM set | ~60 | retarget back buffer |
| per-sec IF (no-op) | ~100 | clk-sc == last-sc |
| ss/hm/date-pending checks (no-op) | ~90 | three counters |
| `redraw-hands` base | ~1,900 | `compute-angles` CODE (282cy) + 3 lookups + 3 compares + lasts |
| Loop/branch overhead | ~100 | AGAIN branch |
| **Total baseline** | **~3,216** | ~22% of budget |

This is what every "nothing changed" frame costs.  At our angular
resolution, ~57 of 60 frames in a typical second are exactly this.
Pre-#455 baseline was ~6,876cy (46%) — the angle-math CODE conversion
cut per-frame work by ~3,660cy.

### Optional add-ons (each fires only when its condition triggers)

| Add-on | Cycles | Frequency |
|--------|--------|-----------|
| `redraw-hands` sec-draw | +2,000 | Sec endpoint moved (~3 frames/sec) |
| `redraw-hands` mn-draw | +2,000 | Mn endpoint moved (~6 frames/min, ~0 in a typical sec) |
| `redraw-hands` hr-draw | +1,400 | Hr endpoint moved (~30/hour, vanishingly rare per sec) |
| `tick-second` (folds into tick-frame) | +2,376 | Frame 0 of every sec |
| `render-ss` + `render-sync-flash` | +2,692 | ss-pending fires for 2 frames after each sec change |
| `render-hm` | +1,961 | hm-pending fires for 2 frames after each min change |
| `render-date` | +4,301 | date-pending fires for 2 frames after each day change |
| `sync-from-fn` | +5,387 | clk-sc=59, FN-enabled, once per minute |

## Frame Cost Scenarios

### Mid-minute second (58 of every 60 sec)

| Frame | Components | Total | % budget |
|-------|------------|-------|----------|
| 0 | baseline + tick-second + sc-draw + render-ss + flash | ~10,300 | 69% |
| 1 | baseline + render-ss + flash | ~7,900 | 53% |
| ~3 mid-second | baseline + sc-draw | ~5,220 | 35% |
| ~55 idle | baseline | ~3,220 | 22% |

**All under budget.**  ~3,600cy average per frame.  Plenty of headroom.

### Minute-boundary second (1 of every 60 sec, the :59 → :00 transition)

Frames 0 and 1 trigger the simultaneous sec rollover + min rollover:

| Frame | Components | Total | % budget |
|-------|------------|-------|----------|
| 0 | baseline + tick-second + sc-draw + mn-draw + render-ss + render-hm + flash | ~14,260 | **96%** |
| 1 | baseline + sc-draw + mn-draw + render-ss + render-hm + flash | ~11,880 | 80% |
| 2-59 | mid-minute pattern | ~3,220-5,220 | 22-35% |

**All under budget post-#455.**  Frame 0 at 96% is the tightest,
with only ~660cy of headroom.  Pre-#455 both frames overran (120%
and 104%); the angle-math CODE conversion pulled them under the line.

### :59 sync second (production only, when FN-enabled)

Frame 59 of the minute fires `sync-from-fn` (5,387cy):

| Frame | Components | Total | % budget |
|-------|------------|-------|----------|
| sync frame | baseline + sync-from-fn | ~8,600 | 58% |

Adaptive `calibrate-vps` runs inside `sync-from-fn` to keep the
clock in step with the external real-time source.

### Day-boundary second (1 per day, midnight)

Frame 0 of the new day adds `render-date` (4,301cy) to the
minute-boundary pattern, pushing it to ~18,560cy (124%).  Two frames
over budget, once per 24 hours; 4 lost vblanks total.  Trivial.

## Drift Analysis (fake-time mode)

With all optimizations landed:

| Scenario | Real vblanks per fake-sec | Drift per minute |
|----------|---------------------------|------------------|
| Mid-minute second | 60 (perfect) | 0 |
| Minute-boundary second | 60 (perfect) — was 64 pre-#455 | 0 |
| Day-boundary second | 62 (frames 0/1 still over at ~18.5k) | 0.003s/day |

Every frame now fits in one vblank except at midnight.  In fake-time
dev mode drift is essentially zero; on real hardware with FN sync,
`calibrate-vps` covers any residual scheduler variance.

## Remaining Targets

| Area | Estimated savings | Notes |
|------|-------------------|-------|
| `redraw-hands` as a single CODE word (#456) | ~2,000cy | Inlines the cascade dispatch and lasts updates.  Baseline drops from 3,200 to ~1,200cy (8% budget).  Nice-to-have; no over-budget conditions left to solve. |
| `angle-dx` / `angle-dy` → CODE (#447) | ~2,500cy per call | Speeds up boot-only `tick-hands` and face init.  No per-frame impact. |
| Smarter day-boundary redraw | minor | Split render-date across multiple frames, or trigger hm/ss pending counters sequentially instead of simultaneously. |

Post-#455 the clock is comfortably under budget on every frame
except the once-per-day midnight overrun.  Further optimization is
quality-of-life, not correctness.

## Measurement

Cycle counts generated by `fc.py --cycles`, which computes exact 6809
instruction timing for CODE words and kernel primitives, and
recursive ITC analysis for Forth colon definitions.  Forth word
costs for words with IF/ELSE branches represent the sum of all paths
(worst case upper bound); typical per-frame costs above are derived
manually from the no-rollover / no-pending branches.

Also see `frame_budget_chart.html` for the interactive visual chart.
