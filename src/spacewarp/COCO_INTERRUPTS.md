# CoCo Interrupt System — Research Notes

Research for Space Warp project. How the CoCo's interrupt hardware works
and what we might use it for.

## Current State

**Interrupts are completely disabled.** The kernel bootstrap masks IRQ and
FIRQ (`ORCC #$50`) and never re-enables them. All timing is polled.

**VSYNC** is a Forth busy-wait loop in `forth/lib/screen.fs`:
```forth
: vsync  BEGIN  $FF03 C@ $80 AND  UNTIL  $FF02 C@ DROP ;
```
Polls PIA0 CRB bit 7 until the VDG sets the field sync flag, then clears
it by reading the data register. Burns CPU in a tight loop while waiting.

## Hardware: Two PIAs, Two Interrupt Lines

### PIA0 ($FF00-$FF03) — drives CPU IRQ pin

| Register | Bit 7 flag | Bit 0 enable | Source | Period |
|----------|-----------|-------------|--------|--------|
| $FF01 (CRA) | HSYNC flag | IRQ enable | Horizontal sync | **63.5 us** (15,720/sec) |
| $FF03 (CRB) | VSYNC flag | IRQ enable | Field sync | **16.667 ms** (60/sec) |

Both PIA0 outputs are OR'd onto the IRQ line. To determine which fired,
read bit 7 of $FF01 (HSYNC) and $FF03 (VSYNC).

Clear HSYNC flag by reading $FF00. Clear VSYNC flag by reading $FF02.

Currently: both configured with `$04` in control registers (bit 0 = 0 =
IRQ disabled, bit 2 = 1 = data register selected).

### PIA1 ($FF20-$FF23) — drives CPU FIRQ pin

| Register | Bit 7 flag | Bit 0 enable | Source |
|----------|-----------|-------------|--------|
| $FF21 (CRA) | CD flag | FIRQ enable | Cassette / RS-232 |
| $FF23 (CRB) | CART* flag | FIRQ enable | Cartridge interrupt |

FIRQ is the 6809's "fast" interrupt — saves only CC and PC (not full
register set), so entry/exit is faster (10cy push + 10cy RTI vs 17+15
for IRQ). Currently unused. The CART* line could be driven by custom
cartridge hardware.

### 6809 Interrupt Behavior

**IRQ**: Pushes all registers (CC, A, B, DP, X, Y, U, PC = 12 bytes, 17cy).
Sets I flag (masks further IRQs). Fetches vector from $FFF8. RTI restores
all registers (15cy). **Total overhead: ~40cy minimum.**

**FIRQ**: Pushes only CC and PC (3 bytes, 10cy). Sets I and F flags.
Fetches vector from $FFF6. RTI restores CC+PC (10cy). **Total overhead:
~25cy.** Handler must save/restore any registers it uses.

**NMI**: Like IRQ (saves all), but non-maskable. Vector at $FFFC. We don't
have a hardware NMI source on the CoCo.

## Interrupt Vectors and All-RAM Mode

### The Address Mapping

The 6809 fetches vectors from $FFF2-$FFFF. But the CoCo's address decoder
maps this region to **$BFF2-$BFFF** in the BASIC ROM address space. When
the CPU reads $FFFE (RESET vector), it physically reads address $BFFE.

In **normal mode**: $BFF2-$BFFF is BASIC ROM. Vectors point to BASIC's
handlers. Read-only.

In **all-RAM mode** (TY=1, our mode): $8000-$FEFF is RAM. Since $BFFx is
in this range, **writing to $BFF2-$BFFF installs custom vectors** that the
CPU reads through the $FFF2-$FFFF hardware mapping.

We're already in all-RAM mode. Just write handler addresses to these locations.

### Vector Table

| Write to | CPU fetches from | Vector | 6809 response |
|----------|-----------------|--------|---------------|
| $BFFE | $FFFE | **RESET** | Full register push, fetch vector |
| $BFFC | $FFFC | NMI | Full push, non-maskable |
| $BFFA | $FFFA | SWI | Full push, software trap |
| $BFF8 | $FFF8 | **IRQ** | Full push, maskable |
| $BFF6 | $FFF6 | **FIRQ** | CC+PC push, fast maskable |
| $BFF4 | $FFF4 | SWI2 | Full push, software trap |
| $BFF2 | $FFF2 | SWI3 | Full push, software trap |

## HSYNC: Scan Line Tracking

### The Opportunity

HSYNC fires once per scan line. NTSC has 262 lines per field. If we can
count lines from VSYNC, we know where the electron beam is on screen.
Drawing behind the beam eliminates flicker and tearing.

### Continuous Counting: Not Feasible

At 0.895 MHz, there are only **~57 CPU cycles between HSYNC pulses**.
An IRQ handler costs ~40cy minimum (push + handler + RTI). That leaves
~17cy for game code — the CPU would spend **70-88% of its time in the
interrupt handler**. This kills performance.

### Selective Polling: Feasible

Don't count every line. Instead, **poll HSYNC only when you need to know
the beam position** — right before drawing a sprite:

```asm
; Wait for beam to pass row N (counted from VSYNC)
; Call after VSYNC has been detected
wait_line
        LDA     #N              ; target line count
@wt     LDB     $FF01           ; read HSYNC flag (bit 7)
        BPL     @wt             ; flag not set, keep polling
        LDB     $FF00           ; clear flag (read data reg)
        DECA
        BNE     @wt             ; count down
        RTS
```

Cost: ~20cy per line polled. To skip 50 lines: ~1,000cy. To skip 100
lines: ~2,000cy. Affordable within the frame budget.

### Mapping Lines to VRAM Rows

In RG6 mode (256x192, VRAM at $0600):
- 192 visible pixel rows out of 262 total lines per field
- The VDG starts scanning VRAM ~35 lines after VSYNC (vertical blanking)
- Each VRAM row is 32 bytes (256 pixels / 4 pixels per byte / 2 colors)
- VRAM row N starts at: $0600 + N * 32

To draw at VRAM row R without flicker:
1. After VSYNC, wait for `35 + R` HSYNC pulses
2. The beam has now passed row R
3. Write to VRAM row R — the beam won't revisit it this field

### Practical Use: Split Rendering

Instead of drawing all sprites after VSYNC (risking the beam catching up),
draw them in order from top to bottom:
1. VSYNC fires
2. Draw topmost sprite (beam is still in blanking)
3. Poll HSYNC to wait for beam to pass the next sprite's row
4. Draw next sprite
5. Repeat

This interleaves rendering with beam travel, using idle wait time that
would otherwise be wasted.

## RESET Vector: Game Restart

### The Problem

In all-RAM mode, $BFFE contains whatever RAM had. Pressing physical RESET
jumps to garbage. User must power-cycle.

### The Catch

The RESET signal **resets the SAM to power-on defaults**, clearing the TY
bit (back to ROM mode). Now $BFFE reads from BASIC ROM, not our RAM
vector. The CPU jumps to BASIC's reset handler.

This is a chicken-and-egg problem: RESET undoes the condition (all-RAM)
that would let us control RESET.

### Possible Workarounds

1. **Cartridge autostart**: If we eventually ship on a ROM cartridge,
   BASIC's reset handler checks $C000 for the autostart signature and
   jumps to the cartridge. Our cartridge code could re-initialize.

2. **NMI instead**: NMI doesn't reset the SAM. If we had hardware to
   generate NMI (button wired to the NMI pin), we could use it for a
   clean restart. But stock CoCo has no NMI source.

3. **Software restart**: Implement a game command (e.g., "RESET" or
   key combo) that jumps back to the bootstrap/title screen. No hardware
   involvement. **This is the practical approach.**

## VSYNC Interrupt: Replace Polling

### Setup

```asm
; Install IRQ handler at $BFF8 (IRQ vector)
        LDD     #VSYNC_IRQ
        STD     $BFF8

; Enable VSYNC IRQ: set $FF03 bit 0 = 1
        LDA     $FF03
        ORA     #$01
        STA     $FF03

; Unmask CPU IRQ
        ANDCC   #$EF            ; clear I bit in CC
```

### Handler

```asm
VSYNC_IRQ
        INC     <vsync_flag     ; direct page, 1 byte
        LDA     $FF02           ; clear VSYNC flag
        RTI                     ; restore all regs, ~15cy
```

Total handler cost: ~40cy per VSYNC (17cy push + ~8cy handler + 15cy RTI).
At 60Hz this is 2,400cy/sec — negligible (0.3% of CPU).

### Revised vsync Word

```forth
: vsync  BEGIN  vsync-flag C@  UNTIL  0 vsync-flag C! ;
```

Same polling loop but the flag is set by hardware interrupt, not by
reading the PIA directly. The practical difference is small unless we
restructure the game loop to do useful work between the "wait for vsync"
point and the actual VSYNC event.

### Real Benefit: Knowing When VSYNC Happened

With interrupt-driven VSYNC, the flag tells us VSYNC happened even if we
weren't polling at the exact moment. This enables:
- Detecting missed frames (flag was already set when we checked)
- Starting work immediately on the VSYNC edge (ISR sets flag, main loop
  picks it up on the next instruction)
- Frame-skip logic: if flag is already set, we missed a frame and should
  skip rendering

## Timing Reference

| Event | Period | CPU cycles between |
|-------|--------|--------------------|
| HSYNC (one scan line) | 63.5 us | ~57 cy |
| VSYNC (one field) | 16.667 ms | ~14,930 cy |
| Vertical blanking | ~35 lines | ~2,000 cy |
| Active display | 192 lines | ~10,900 cy |
| Bottom blanking + return | ~35 lines | ~2,000 cy |

The ~2,000cy of vertical blanking after VSYNC is "free" drawing time —
the beam isn't scanning VRAM, so any VRAM writes are flicker-free.

## Discussion: Keyboard Responsiveness

There's no keyboard interrupt on the CoCo. The keyboard is purely passive:
strobe columns via $FF02, read rows from $FF00. Data only when you ask.

Using a VSYNC ISR to poll the keyboard would give consistent 60Hz sampling,
but `latch-key` already runs every frame in the hot path doing the same
thing. An ISR would move the work from main loop to ISR — same cycles,
different timing. The only win: if we drop a frame, the ISR still catches
the key. But a 1-frame (16ms) delay is imperceptible.

The real keyboard issue is **command responsiveness**. Arrow keys feel fine
because `move-ship` reads the matrix directly every frame. But command keys
(digits for warp/shields/etc) go through `latch-key` -> `process-key` ->
`process-cmd-input` with ITC overhead, checked once per frame. If a digit
is pressed and released during a heavy frame, it can be missed entirely.
The player has to press "deliberately."

This improves substantially as we approach consistent 60fps — fewer heavy
frames means fewer missed polls. But it suggests command input should be
sampled as early as possible in the frame, or latched in hardware-timed
ISR if we ever enable interrupts.

## Discussion: Exploiting the Vertical Blanking Window

### Current Rendering Architecture

```
1. Game logic (AI, physics, input)     <- variable cost
2. VSYNC                               <- busy-wait for blanking
3. Rendering (erase, draw, post)       <- runs INTO visible scan area
```

The ~2,000cy after VSYNC is when the beam is in top blanking — no VRAM is
being scanned. After that, the beam enters the visible area and scans
top-to-bottom at one line per 63.5us (~57cy).

We burn some of those precious blanking cycles in the Forth `vsync` polling
loop. Then rendering starts at the top of VRAM and works down. If rendering
exceeds ~2,000cy, the beam catches up and we write pixels the beam is about
to scan. That's where tearing comes from.

### The "Sprites Per Scan Line" Problem

On NES/SMS, the hardware sprite engine scans a fixed number of sprite
entries per line during HSYNC. Too many sprites sharing a row exhausts the
time budget and sprites get dropped — the classic "8 sprites per line" limit.

The CoCo has no sprite engine — we software-render to a flat VRAM buffer.
No hard per-line limit. But we have the analogous problem: if we write to a
VRAM row the beam is currently scanning, the viewer sees a half-old,
half-new row. Multiple sprites on the same row means more writes in that
band, increasing the chance the beam catches us mid-write.

### How HSYNC Tracking Would Help

Our sprites are 7x5 pixels. Each erase+redraw touches 5 VRAM rows. With
3 Jovians + ship + missile, that's up to 25 rows of writes per frame. If
two Jovians share a horizontal band, their writes overlap — prime tearing
territory.

With HSYNC polling, the rendering loop becomes:

1. VSYNC fires. ~35 blank lines (~2,000cy) of free drawing time.
2. Draw the topmost sprites immediately — beam hasn't reached them.
3. For sprites lower on screen, poll HSYNC to wait for the beam to pass.
4. Draw each sprite band only after the beam clears it.

Cost: ~20cy per line of waiting. Sprite at row 96 (middle screen) needs
~60 lines past blanking = ~1,200cy of polling. But that's time we'd
otherwise spend either drawing (risking tearing) or idling.

### What HSYNC Tracking Won't Fix

**Logical artifacts** like the red jbeam streaks (#259): the beam path was
traced at the ship's old position, and beam-restore-slice writes stale
pixels back after the ship moved. This is a data bug, not a timing bug.
HSYNC tracking won't help.

**What it WILL fix**: the brief black flash when a sprite is erased and
redrawn 1-2 pixels away. The erase writes black, then redraw writes the
sprite. If the beam scans between erase and redraw, you see the black
frame. HSYNC tracking ensures the erase+redraw pair completes before the
beam reaches that row.

### Practical Approach: `wait-past-row`

A minimal prototype to test whether beam-tracking helps:

```asm
; wait-past-row ( row -- )
; After VSYNC, poll HSYNC until beam passes the given VRAM row.
; Row 0 = top of tactical view. Assumes VSYNC just happened.
CODE wait-past-row
        PSHS    X
        LDB     1,U             ; row number (0-191)
        LEAU    2,U             ; pop arg
        ADDB    #35             ; add blanking lines
        ; Count down HSYNC pulses
@wt     LDA     $FF01           ; check HSYNC flag (bit 7)
        BPL     @wt             ; not set yet
        LDA     $FF00           ; clear flag
        DECB
        BNE     @wt
        PULS    X
        ;NEXT
;CODE
```

Cost: B * ~20cy. For row 96: (96+35) * 20 = ~2,620cy. Significant but
bounded, and the time is otherwise idle (waiting for beam to pass).

### Rendering Rewrite Sketch (Top-to-Bottom)

Instead of erase-all-then-draw-all, sort sprites by Y and interleave
with beam tracking:

```
VSYNC
; -- Blanking window (~2,000cy) --
; Draw stars (they're static, 400cy CODE)
; Erase + redraw topmost sprite

; -- Beam enters visible area --
for each sprite (sorted top to bottom):
    wait-past-row sprite_y + sprite_height
    erase old position
    draw new position

; -- Post-render (panels, beams, etc.) --
; Panel is below row 144, beam reaches it late — safe to draw anytime
```

The sort overhead is minimal (3 Jovians = 3-element insertion sort).

## Discussion: Double Blanking Window (6,000cy Approach)

### The Insight

The blanking interval exists on **both sides** of VSYNC. The NTSC field
has 262 lines total, 192 active display, leaving 70 lines of blanking.
If roughly symmetric:

- ~35 lines before VSYNC (beam past bottom of VRAM)
- VSYNC fires
- ~35 lines after VSYNC (beam returning to top)

That's ~4,000cy of safe VRAM access (70 * 57cy).

But we can do better. If we detect (via HSYNC polling) when the beam is
~35 lines from the **bottom of active display** (row ~157), we can start
rendering the **top of VRAM** immediately — the beam is at the bottom and
won't revisit the top until it wraps around:

```
35 lines (beam finishing bottom of display)     ~2,000cy
35 lines (bottom blanking)                      ~2,000cy
35 lines (top blanking, after VSYNC)            ~2,000cy
─────────────────────────────────────────────────────────
Total before beam reaches row 0 again:          ~6,000cy
```

### Does 6,000cy Fit Our Sprite Work?

| Operation | Cycles |
|-----------|--------|
| draw-stars CODE | 400 |
| bg-restore (Jovians) | 909 |
| bg-save-oldpos | 191 |
| bg-save (Jovians) | 900 |
| draw-jovians-live | 1,151 |
| bg-restore (ship) | 476 |
| bg-save (ship) | 549 |
| draw-ship | 819 |
| **Total sprite ops** | **~5,400cy** |

5,400cy fits in 6,000cy with 600cy to spare. If bg-jov becomes CODE
(#248 re-attempt), that saves ~1,000cy more.

### Revised Game Loop

Instead of waiting for VSYNC and then rendering:

```
1. Game logic (AI, physics, input)
2. Wait for beam to reach row ~157 (HSYNC polling)
3. Render sprites top-to-bottom (~5,400cy)
   -- beam wraps through blanking during this --
4. VSYNC fires somewhere during step 3 (we don't need to poll it)
5. Post-render (beams, panel) — panel is at bottom, safe
```

The VSYNC event happens naturally during our rendering. We don't poll
for it explicitly. Our sync point becomes the HSYNC-counted position
instead.

### Implementation: wait-for-bottom

```asm
wait-for-bottom:
    ; First, sync to VSYNC to establish line count reference
    @vs  LDA  $FF03       ; poll VSYNC flag
         BPL  @vs
         LDA  $FF02       ; clear VSYNC flag
    ; Count 157 HSYNC pulses (35 blanking + 122 active = row 157)
    ; Actually: 35 top blanking + 157 active lines = 192 pulses
    ; to reach display row 157
         LDA  #192
    @hs  LDB  $FF01       ; poll HSYNC flag
         BPL  @hs
         LDB  $FF00       ; clear HSYNC flag
         DECA
         BNE  @hs
    ; Beam is now at row 157. Render from top — 6,000cy safe window.
```

Wait — this still polls for VSYNC first, then counts forward. That means
we wait for the start of blanking, then count through ALL of blanking plus
157 active lines. Total wait: 70 + 157 = 227 lines * ~20cy = ~4,500cy
just polling. That's expensive.

### Better: Count Backward from VSYNC

If we know VSYNC fires at the end of active display (after row 191):
- After VSYNC: 70 lines of blanking = 4,000cy before row 0
- Start rendering immediately after VSYNC → 4,000cy window
- Then use HSYNC polling only if we need more time past row 0

If VSYNC gives us the full 4,000cy of blanking directly, we might not
need the HSYNC approach at all for the 5,400cy of sprite work. The
question is: does the VDG's FS fire at the end of active display, or
somewhere in the middle of blanking?

### Key Question to Verify

**Where exactly does VSYNC (FS) fire relative to active display?**

If FS fires at end of line 191 → we get ~4,000cy before line 0 resumes.
If FS fires mid-blanking → we get only ~2,000cy.

This can be tested: after VSYNC, count HSYNC pulses until the beam
starts scanning VRAM (detect by checking if VRAM reads produce tearing).
Or write a known pattern to VRAM row 0 and time when it becomes visible.

### Open Questions

1. **Verify FS timing**: count HSYNC pulses between VSYNC and first
   visible line on XRoar. This determines whether we need HSYNC at all.
2. **Prototype wait-past-row**: add to kernel, test with a simple sprite
   draw to see if tearing is reduced.
3. If 4,000cy after VSYNC is confirmed, we may only need to restructure
   the render order (sprites first, post-render second) without any
   HSYNC tracking.
