# Space Warp Frame Budget

**Target: 16,667 cycles per frame (60fps NTSC VSYNC)**
**CPU: 6809 @ 0.895 MHz (CoCo 2 NTSC) = ~14,917 cycles/frame**

The 6809 runs at 0.895 MHz, giving ~14,917 CPU cycles between VSYNC events.
The game loop must complete all pre-VSYNC work within this budget, or frames drop.

## Frame Schedule

```
Frame N (even)                    Frame N+1 (odd)
========================          ========================
EVERY FRAME (~1,000cy)            EVERY FRAME (~1,000cy)
  save-ship-pos         50cy        save-ship-pos         50cy
  move-ship            400cy        move-ship            400cy
  process-key          200cy        process-key          200cy
  tick-missile         150cy        tick-missile          150cy
  tick-jbeam           200cy        tick-jbeam            200cy

EVEN: PHYSICS + AI (~3,400cy)     ODD: JOVIAN GRAVITY + BG (~3,000cy)
  gravity-well         300cy        jov-gravity      0-1,500cy
  star-gravity       0-800cy        latch-key             50cy
  check-collisions     200cy        tick-destruct         50cy
  tick-jovians     0-2,100cy
  latch-key             50cy      BACKGROUND slot 1 or 5 (~950cy)
  tick-destruct         50cy        slot 1: regen+dock+base  850cy
                                    slot 5: time+migrate+cond 950cy

─── VSYNC ─────────────────       ─── VSYNC ─────────────────

POST-VSYNC RENDER (~2,500cy)      POST-VSYNC RENDER (~500cy)
  beam erase/draw      300cy        beam erase/draw      300cy
  restore-ship-bg      100cy        restore-ship-bg      100cy
  restore-jov-bgs      300cy        (ship-only path)
  draw-stars           250cy        save-ship-bg         100cy
  save-jov-bgs         300cy        draw-ship            200cy
  save-ship-bg         100cy
  draw-jovians         600cy
  draw-ship            200cy
  draw-missile         100cy

POST-RENDER (~500cy)              POST-RENDER (~500cy)
  apply-beam-hit       200cy        apply-beam-hit       200cy
  apply-jbeam-hit      100cy        apply-jbeam-hit      100cy
  panel updates        150cy        panel updates        150cy
  win/lose checks      100cy        win/lose checks      100cy
```

## Worst-Case Analysis

| Scenario | Even Frame | Odd Frame | Budget | Margin |
|----------|-----------|-----------|--------|--------|
| **Idle (no Jovians)** | 3,400 | 2,000 | 14,917 | 77-87% |
| **1 Jovian, 2 stars** | 5,600 | 3,000 | 14,917 | 63-80% |
| **3 Jovians, 5 stars** | 7,900 | 5,000 | 14,917 | 47-66% |
| **3 Jov, 5 star, bhole + BG** | 7,900 | 5,950 | 14,917 | 47-60% |
| **Above + beam + missile** | 8,400 | 6,450 | 14,917 | 44-57% |

## Per-Word Cost Estimates

### Every Frame (always runs)

| Word | Type | Est. Cycles | Notes |
|------|------|-------------|-------|
| `save-ship-pos` | Forth | 50 | 2 C@ + 2 ! |
| `move-ship` | Forth | 400 | 4 key scans + try-move (includes ship-jov-blocked?) |
| `process-key` | Forth | 200 | KEY? + debounce + dispatch |
| `tick-missile` | Forth | 150 | position update + bounds check + hit check |
| `tick-jbeam` | Forth | 200 | cooldown check + fire logic |

### Even Frames (physics + AI)

| Word | Type | Est. Cycles | Varies With | Notes |
|------|------|-------------|-------------|-------|
| `gravity-well` | Forth | 300 | bhole present | 1 mdist + tiered pull |
| `star-gravity` | Forth | 160/star | 0-5 stars | mdist + conditional pull per star |
| `check-collisions` | CODE | 200 | stars + bhole | Assembly loop, fast |
| `tick-jovians` | CODE+Forth | 700/jovian | 0-3 alive+aware | jov-think (CODE) + apply-intent (Forth with jov-blocked? x3) |

**Hot path**: `tick-jovians` with 3 engaged Jovians = ~2,100cy.
`apply-intent` calls `jov-blocked?` up to 3x per Jovian (3-tier fallback).
`jov-blocked?` loops stars (0-5) + checks bhole, base, ship, other Jovians = ~200cy per call.
Jovian tick counters staggered at init (0, 1, 2) to reduce simultaneous thinks.

### Odd Frames (gravity + background)

| Word | Type | Est. Cycles | Varies With | Notes |
|------|------|-------------|-------------|-------|
| `jov-gravity` | Forth | 500/jovian | 0-3 alive x stars | bhole check + star loop per Jovian |

### Every 8th Frame (background tasks, odd frames only)

| Word | Slot | Est. Cycles | Notes |
|------|------|-------------|-------|
| `jov-check-regen` | 1 | 200 | Check emotion band crossing |
| `check-dock` | 1 | 150 | Proximity check to base |
| `tick-dock` | 1 | 300 | 5 system repairs + energy |
| `tick-base-attack` | 1 | 200 | Jovian-near-base check |
| `tick-stardate` | 5 | 100 | Frame counter |
| `tick-migrate` | 5 | 500 | Galaxy-wide search (64 bytes) |
| `check-spawn` | 5 | 150 | Deferred spawn execution |
| `update-cond` | 5 | 200 | State check + text redraw |

### Post-VSYNC Rendering

| Word | Type | Est. Cycles | Notes |
|------|------|-------------|-------|
| `tick-beam-erase` | CODE | 150 | beam-restore-slice per bolt segment |
| `tick-beam-draw` | CODE | 150 | beam-draw-slice per bolt segment |
| `restore-ship-bg` | CODE | 100 | bg-restore 4x5 bytes |
| `restore-jov-bgs` | CODE | 100/jovian | bg-restore-7 4x7 bytes each |
| `draw-stars` | CODE | 50/star | rg-pset per star |
| `save-jov-bgs` | CODE | 100/jovian | bg-save-7 4x7 bytes each |
| `save-ship-bg` | CODE | 100 | bg-save 4x5 bytes |
| `draw-jovians-live` | CODE | 200/jovian | spr-draw per living Jovian |
| `draw-ship` | CODE | 200 | spr-draw |
| `draw-missile` | CODE | 100 | spr-draw (3x3) |

## Jitter Sources

1. **Even vs odd frames**: Even frames do physics+AI, odd frames do gravity+BG. Gravity loops gated to every 4th frame, so most frames are lightweight.

2. **Jovian tick synchronization**: Tick counters staggered at init (I=0,1,2). Max overlap is 2 Jovians thinking on the same even frame when LCM of thresholds aligns. Each think = ~700cy.

3. **`jov-blocked?` cascades**: Each Jovian think calls `apply-intent` which calls `jov-blocked?` up to 3x (try both axes, try x-only, try y-only). Each call loops all stars + checks 4 other obstacle types.

4. **Gravity gating**: All gravity distance loops (star-gravity, jov-gravity star loop, gravity-well pull, jov-gravity bhole pull) gated on `grav-tick @ 3 AND 0=`. Only runs every 4th even frame. Contact-range kills (<3px star, <6px bhole) still check every frame.

5. **Rendering path**: Full cycle (jov-moved) = ~2,500cy. Ship-only = ~500cy. The full cycle runs when any Jovian moves, which is most even frames during combat.

6. **Frame drops**: Confirmed 0 drops via vsync-check instrumentation. Peak utilization ~35% of budget. All jitter is visual (rendering path variation), not temporal.

## Optimization Opportunities

| Opportunity | Estimated Savings | Complexity |
|-------------|-------------------|------------|
| `jov-blocked?` -> CODE word | ~400cy per call (3-9x/frame) | High (variable address issue) |
| `move-ship` -> CODE word | ~300cy | Medium |

## Appendix: 60-Frame Cycle Map

Worst case scenario: 3 Jovians (thresholds 4/6/3, staggered 0/1/2), 5 stars, black hole, base, active beam + missile.

Columns: `EF` = every-frame tasks, `SGrv` = star-gravity (ship), `GW` = gravity-well (bhole), `Coll` = check-collisions, `J0/J1/J2` = Jovian think, `JGrv` = jov-gravity (all Jovians), `BG` = background slot, `Rnd` = rendering, `Post` = post-render. All values in cycles.

Even frames: ship physics + AI thinking. Odd frames: Jovian gravity + background tasks.
Gravity loops gated: only run when `grav-tick MOD 4 = 0` (grav-tick increments on even frames).
`G` suffix marks frames where gravity distance loops are active.

| Fr | E/O | EF | SGrv | GW | Coll | J0 | J1 | J2 | JGrv | BG | Rnd | Post | **Total** |
|----|-----|----|------|----|------|----|----|----|------|----|-----|------|-----------|
| 0 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 1 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 2 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 3 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 4 | E | 1000 | | 50 | 200 | 700 | | | | | 2500 | 500 | **4950** |
| 5 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 6 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 7 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 8 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 9 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 10 | E | 1000 | | 50 | 200 | 700 | | 700 | | | 2500 | 500 | **5650** |
| 11 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 12 | E | 1000 | | 50 | 200 | | 700 | | | | 2500 | 500 | **4950** |
| 13 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 14 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 15 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 16 | E/G | 1000 | 800 | 300 | 200 | 700 | | | | | 2500 | 500 | **6000** |
| 17 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 18 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 19 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 20 | E | 1000 | | 50 | 200 | | 700 | | | | 2500 | 500 | **4950** |
| 21 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 22 | E | 1000 | | 50 | 200 | 700 | | 700 | | | 2500 | 500 | **5650** |
| 23 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 24 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 25 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 26 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 27 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 28 | E | 1000 | | 50 | 200 | 700 | 700 | | | | 2500 | 500 | **5650** |
| 29 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 30 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 31 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 32 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 33 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 34 | E | 1000 | | 50 | 200 | 700 | | 700 | | | 2500 | 500 | **5650** |
| 35 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 36 | E | 1000 | | 50 | 200 | | | | | | 2500 | 500 | **4250** |
| 37 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 38 | E | 1000 | | 50 | 200 | | 700 | 700 | | | 2500 | 500 | **5650** |
| 39 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 40 | E/G | 1000 | 800 | 300 | 200 | 700 | | | | | 2500 | 500 | **6000** |
| 41 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 42 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 43 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 44 | E | 1000 | | 50 | 200 | | 700 | | | | 2500 | 500 | **4950** |
| 45 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 46 | E | 1000 | | 50 | 200 | 700 | | 700 | | | 2500 | 500 | **5650** |
| 47 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 48 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 49 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 50 | E | 1000 | | 50 | 200 | | | 700 | | | 2500 | 500 | **4950** |
| 51 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 52 | E | 1000 | | 50 | 200 | 700 | | | | | 2500 | 500 | **4950** |
| 53 | O | 1000 | | | | | | | 50 | 950 | 500 | 500 | **3000** |
| 54 | E | 1000 | | 50 | 200 | | 700 | 700 | | | 2500 | 500 | **5650** |
| 55 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |
| 56 | E/G | 1000 | 800 | 300 | 200 | | | | | | 2500 | 500 | **5300** |
| 57 | O/G | 1000 | | | | | | | 1500 | 850 | 500 | 500 | **4350** |
| 58 | E | 1000 | | 50 | 200 | 700 | | 700 | | | 2500 | 500 | **5650** |
| 59 | O | 1000 | | | | | | | 50 | | 500 | 500 | **2050** |

### Summary

| Metric | Value |
|--------|-------|
| **Budget per frame** | 14,917 cy |
| **Light odd frame** | ~2,050 cy (14% budget) |
| **Light even frame (no thinks)** | ~4,250 cy (28% budget) |
| **Even + 1 think** | ~4,950 cy (33% budget) |
| **Even + 2 thinks** | ~5,650 cy (38% budget) |
| **Gravity even (no thinks)** | ~5,300 cy (36% budget) |
| **Peak: gravity even + 1 think** | ~6,000 cy (40% budget) |
| **Gravity odd + BG** | ~4,350 cy (29% budget) |
| **Odd + BG (no gravity)** | ~3,000 cy (20% budget) |
| **Headroom at peak** | ~8,917 cy (60%) |
| **Frame load range** | 2,050 - 6,000 (~3:1) |

With gravity gated to every 4th frame, 6 of every 8 frames run at 2,050-5,650cy (14-38% budget).
Only 2 of every 8 frames hit the gravity loops at 4,350-6,000cy (29-40% budget).
Confirmed 0 frame drops via instrumentation — 60% headroom at absolute peak.
