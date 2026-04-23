# Clock Frame Budget

**CPU: 6809 @ 0.8949 MHz (14.31818 MHz crystal ÷ 16)**
**NTSC field rate: 59.94 Hz → ~14,917 cycles/frame budget**

At the NTSC field rate of 59.94 Hz, the 6809 has ~14,917 CPU cycles
between VSYNC events.  The clock's main loop must complete all work
within this budget, or frames drop and the local clock runs slow.

The clock is more CPU-bound than Space Warp because its smooth-sweep
second hand runs the beam trace/draw/restore pipeline on every frame,
not just on gameplay events.

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
| `flip-state` | 245 | 9 × (LDD/LDX/STX/STD) swapping fr-/bk- pointer pairs + sec-lt lasts |
| `set-sam-f-fast` | 169 | Unrolled 7-bit SAM-F write for the page flip |
| `dw-read` | 114 | HDB-DOS DWRead vector wrapper |
| `dw-write` | 95 | HDB-DOS DWWrite vector wrapper |
| `sec-tx-tab` / `sec-ty-tab` | 29 each | Push address of precomputed 360-byte endpoint table |
| `time-buf` | 29 | Push address of 6-byte time buffer |

All within the vblank window (~680 cycles), so the page flip is
atomic from the raster's perspective.

## Clock-Specific Forth Words (measured, upper bounds)

| Word | Cycles | Runs when |
|------|--------|-----------|
| `tick-hands` | 24,558 | mn change (2 frames per minute) |
| `redraw-sc-back` (draw path) | 1,968 | When tx/ty moved — pixel-dedup saves most frames (was 8,396 via ep2/angle-dx/dy; see #452) |
| `redraw-sc-back` (skip path) | ~180 | When endpoint unchanged — tables lookup + compare, no beam work |
| `render-datetime` | 7,174 | 2 frames after each sec change |
| `trace-line` | 6,720 | Inside each hand redraw |
| `ep1` / `ep2` | 5,923 | Inside trace-line |
| `sync-from-fn` | 5,387 | Once per minute (production) or boot (fake-time) |
| `render-date` | 4,301 | Part of render-datetime |
| `tick-frame` | 2,897 | Every frame (worst case — rollover) |
| `angle-dx` | 2,824 | Inside ep1 / ep2 |
| `render-time` | 2,824 | Part of render-datetime |
| `angle-dy` | 2,741 | Inside ep1 / ep2 |
| `tick-second` | 2,376 | Rollover only |
| `render-year` | 2,278 | Part of render-datetime |
| `render-sync-flash` | 1,780 | With dig-pending |
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

## Frame Composition

### Typical frame (sec unchanged, no mn change)

| Component | Cycles | Notes |
|-----------|--------|-------|
| `vsync+` | 152 | wait + count |
| `set-sam-f-fast` | 169 | page flip |
| `flip-state` | 197 | pointer swap |
| `tick-frame` (no rollover) | ~304 | increment vs-cnt |
| KVAR-RGVRAM set | ~60 | target draw to back |
| per-sec IF check (no-op) | ~100 | clk-sc == last-sc |
| dig-pending check (no-op) | ~30 | 0 path |
| `redraw-sc-back` | 8,396 | smooth-sweep sec redraw |
| mn-pending check + ELSE | ~100 | 0 path |
| Loop/branch overhead | ~100 | AGAIN branch |
| **Total** | **~9,608** | ~64% of budget |

Fits comfortably in one frame.  ~5,300 cycles of headroom.

### Seconds change frame (sec rolled over; 2 frames per sec)

Adds to typical:

| Component | Cycles | Notes |
|-----------|--------|-------|
| `tick-second` | 2,376 | cascade sec→min→hr→day |
| `render-datetime` | 7,174 | dig-pending = 2 triggers this |
| `render-sync-flash` | 1,780 | also fires during dig-pending |
| `sync-from-fn` | 5,387 | *only* at clk-sc=59 in production |
| **Typical sec-change frame** | **~18,562** | ~124% of budget |
| **:59 sec-change frame (FN sync)** | **~23,949** | ~160% of budget |

The :59 frame is the peak and takes ~2 real frames to render.

### Minute-change frame (every 60 seconds)

Adds to sec-change:

| Component | Cycles | Notes |
|-----------|--------|-------|
| `tick-hands` | 24,558 | full 3-hand redraw replaces `redraw-sc-back` |
| (removes redraw-sc-back saved) | −8,396 | |
| **Minute-change frame** | **~34,724** | ~233% of budget |

Takes ~3 real frames.  Fires on 2 consecutive frames (mn-pending
covers both back buffers), total ~6 real frames consumed per minute
boundary.

## Why Local Seconds Drift Slow

With ~60 Hz target:

| Scenario | Ideal | Actual | Drift/min |
|----------|-------|--------|-----------|
| Typical frame (9.6k cy) | 60 frames/sec | 60 frames/sec | 0 |
| Sec-change frames (18.6k cy) | 60 × 1 per sec = 60 real frames | 60 × 2 (4 per sec over 2 frames) = 62 | +2 frames/sec |
| Minute-change frames (34.7k cy) | 2 total × 1 = 2 real frames | 2 × 3 = 6 | +4 frames/min |
| :59 sync frame (24k cy) | 1 × 1 real | 1 × 2 = 2 | +1 frame/min |

The fake-time path (no sync at :59) loses ~2 frames per second from
digital rendering = ~120 frames/min = 2 seconds/min slow.

Adaptive `vps` calibration at each FN sync corrects for this on
hardware: measured `vsync+` calls per real second → scaled-by-16
`vps` sets the tick-frame rollover threshold accordingly.  See the
`vps` / `calibrate-vps` words in `clock.fs`.

## 60-Frame Cycle Map (Typical Second)

Budget: **14,917 cy/frame**.  Scenario: mid-minute (no mn change
during this second; no :59 sync).  sec rolls over at frame 0;
dig-pending=2 drives rendering on frames 0 and 1.

Component abbreviations: EF=every-frame base(9,028),
TS=tick-second(2,376), RD=render-datetime(7,174),
RSF=render-sync-flash(1,780).

EF = vsync+(152) + set-sam-f-fast(169) + flip-state(197) +
     tick-frame-no-roll(304) + KVAR set(60) + per-sec IF check(100) +
     dig-pending check(30) + redraw-sc-back(8,396) + mn-pending
     check(100) + AGAIN(100) = **9,608**.

(On sec-change frames, tick-frame rolls with tick-second, +2,376cy.)

| Fr | Type | EF | Extra | Total | % |
|----|------|------|-------|---------|------|
| 0 | sec change + dig | 9,608 + 2,376 | RD 7,174 + RSF 1,780 | 20,938 | 140% |
| 1 | dig render (pending) | 9,608 | RD 7,174 + RSF 1,780 | 18,562 | 124% |
| 2-59 | typical | 9,608 | 0 | 9,608 | 64% |

Frame 0 and 1 go over budget → each takes 2 real frames.  Net cost:
2 extra real frames per second.

Minute-change (every 60 sec), on 2 consecutive frames, tick-hands
replaces redraw-sc-back: +24,558 − 8,396 = +16,162cy per frame.
Total ~34.7k cy ≈ 3 real frames each, 6 real frames consumed.

## Remaining Targets

| Area | Estimated savings | Notes |
|------|-------------------|-------|
| `angle-dx` / `angle-dy` → CODE | ~2,500cy per call (×2 per hand) | Biggest single win.  Would drop redraw-sc-back from 8,396 to ~3,500. |
| `render-datetime` on dig-pending only (done) | — | Already applied. |
| Skip `render-datetime` if digits unchanged | ~7,000cy on 2 frames/sec | Only the sec digits change each second; skip year/mo/dy/hr/mn re-render when they haven't changed. |
| `tick-hands` → CODE | ~18,000cy | Only fires 2 frames/min; probably not worth the complexity. |
| Eliminate double `render-sync-flash` during non-flash periods | minor | Currently rendering spaces each flash-idle frame. |

The biggest win is **angle math as CODE**.  That alone would bring
the typical frame from 9,608cy down to ~4,500cy (30% of budget) and
the sec-change frame under budget.

## Measurement

Cycle counts generated by `fc.py --cycles`, which computes exact 6809
instruction timing for CODE words and kernel primitives, and
recursive ITC analysis for Forth colon definitions.  Forth word
costs for words with IF/ELSE branches represent the sum of all paths
(worst case upper bound); typical per-frame costs above are derived
manually from the no-rollover / no-pending branches.

Also see `frame_budget_chart.html` for the interactive visual chart.
