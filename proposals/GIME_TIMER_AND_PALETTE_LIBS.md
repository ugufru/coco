# `palette.fs` and `gime-timer.fs` — Proposal

*September 2026 — Paul Cunningham + Claude*

## Why this exists

[`COCO3_SUPPORT_PLAN.md`](COCO3_SUPPORT_PLAN.md) scopes CoCo 3 support and
sorts it into tiers. Two of its items are the cheapest and the most valuable
respectively, and both are library work rather than kernel surgery:

- **1.2 / 1.3 — the 64-colour palette** (`$FFB0`–`$FFBF`), Tier 1, pure addition.
- **3.1 — the programmable timer** (`$FF90`–`$FF95`, FIRQ), Tier 3, the
  capability the kernel has never had and the one
  [`SOUND_ENGINE_PROPOSAL.md`](SOUND_ENGINE_PROPOSAL.md) wants.

This proposal takes those two from "scoped" to "designed": the word sets, the
register facts each library depends on, and how to test them. It does not
re-argue the plan's governing principle, which stands unchanged:

> Every CoCo 3 feature is gated on a runtime GIME-detection probe. One binary
> runs correctly on both machines.

**What is new since the plan was written.** Both devices have now been
implemented and unit-tested in a second codebase: the RP2350 XRoar port
(`~/github/xroar-waveshare-rp2350-pizero`), where they run as
`lib/coco_machine/src/gime_timer.h` and `coco_palette.h` behind `-DGIME_TIMER`
and `-DGIME_PALETTE`. That work verified the register semantics against XRoar's
own source rather than against recollection, and the numbers below are carried
over from it. It also means a CoCo 3 program written against these libraries has
**two** places to run before it meets real hardware.

---

## `palette.fs` — Tier 1, do this first

### The register facts, verified

| Fact | Value | Source |
|---|---|---|
| Registers | `$FFB0`–`$FFBF`, 16 entries, write-only | Service Manual |
| Value width | 6 bits; the top two read back undriven | XRoar `tcc1014.c:811` masks `& 0x3f` |
| Bit layout | `bit5 R1, bit4 G1, bit3 B1, bit2 R0, bit1 G0, bit0 B0` | XRoar `coco3.c:611-613` |
| RGB intensity levels | **0.000, 0.460, 0.736, 0.920** | XRoar `coco3.c:69` |

Two of these are easy to get wrong and worth stating loudly.

**The bits are interleaved, not grouped.** A palette byte is not `RRGGBB` in the
obvious sense: each channel's two bits sit three apart. Code that treats it as
three adjacent pairs produces colours that look almost right, which is the worst
kind of wrong.

**The intensity ramp is not linear.** The four levels are 0%, 46%, 74%, 92%, not
0/33/66/100. The top level is not full scale. Any RGB colour table built on a
linear ramp will be wrong in every mid-tone against both real hardware and
XRoar, while still looking plausible in isolation. This matters directly for the
plan's item 1.3, which wants a shipped colour table.

### Proposed words

```forth
\ palette.fs — GIME 64-colour palette ($FFB0-$FFBF)
\ Requires: gime? (detection, plan item 1.1)

pal!        ( colour index -- )     \ write one palette register
pal@        ( index -- colour )     \ last value written (shadowed; see below)
pal-16!     ( addr -- )             \ load all 16 from a 16-byte table
pal-save    ( addr -- )             \ copy the shadow to addr, 16 bytes
pal-default ( -- )                  \ restore the CoCo 3 power-on palette
rgb         ( r g b -- colour )     \ 0-3 each -> a 6-bit palette value
```

`pal@` needs a **shadow copy in RAM**, because the registers are write-only on
real hardware. Sixteen bytes buys read-back, `pal-save`, and a sane
`pal-default`; without it neither a palette editor nor "restore what I had" is
possible.

`rgb` is the word that hides the interleave, so no caller ever open-codes the
bit layout:

```forth
: rgb  ( r g b -- colour )
  \ r,g,b are 0-3. Result: bit5 R1, bit4 G1, bit3 B1, bit2 R0, bit1 G0, bit0 B0
  ...
;
```

### Composite vs RGB (plan item 1.3)

The same 6-bit value means different colours on a composite monitor than on RGB,
which is why Super Extended BASIC has both `PALETTE RGB` and `PALETTE CMP`. The
proposal is to ship **two constant tables** and a selector word, exactly as the
plan says, with the RGB table generated from the intensity ramp above rather
than hand-picked.

### Why this is worth doing even for CoCo 1/2 demos

The existing RG6 demos hard-mask colour to two bits (`rg-pset` `ANDA #$03`).
On a CoCo 3 those same two-bit values index palette registers, so **loading a
palette recolours existing demos with no change to their drawing code**. That is
the cheapest visible win in the whole CoCo 3 plan, and it is why palette should
land before anything in Tier 2.

---

## `gime-timer.fs` — Tier 3, the interesting one

### The register facts, verified

| Fact | Value | Notes |
|---|---|---|
| Reload | `$FF94` bits 11-8, `$FF95` bits 7-0 | 12-bit value |
| Clock source | `$FF91` (INIT1) bit 5, TINS | 1 = 3.579545 MHz, 0 = horizontal 15.734 kHz |
| Interrupt enables | `$FF92` IRQ, `$FF93` FIRQ | timer is **bit 5** (`$20`) |
| Status | read `$FF92`/`$FF93` | read-to-clear; this is the acknowledge |
| Reload quirk | 1986 part: +2. 1987 part: +1 | `coco-guides/coco3-intro.md`; a count of 1 behaves as 3 or 2 |
| Zero reload | re-asserts the interrupt immediately on real hardware | the "Arkanoid sound bug" |

### The kernel question this raises

The plan is blunt about it: the kernel masks IRQ and FIRQ permanently
(`ORCC #$50`) and installs no handlers. Everything is polled. A timer library is
therefore **not** just a few register writes; it asks the kernel for a
capability it has never had.

This proposal suggests the library be written in two layers so the cheap half is
not held hostage by the expensive half:

**Layer 1 — the timer as a counter, no interrupts.** Program the reload, select
the clock, and poll the status bit. Useful immediately, breaks nothing, and
needs no kernel change:

```forth
\ gime-timer.fs (layer 1)
tmr!        ( n -- )        \ set the 12-bit reload, restart the countdown
tmr-fast    ( -- )          \ TINS=1, 3.58 MHz
tmr-slow    ( -- )          \ TINS=0, horizontal rate
tmr-stop    ( -- )          \ reload 0 (see the caveat below)
tmr-fired?  ( -- flag )     \ read-to-clear status; true if it elapsed
tmr-hz      ( hz -- n )     \ reload value for a wanted rate, quirk applied
```

**Layer 2 — the FIRQ handler.** Install a real handler, unmask FIRQ, and give
the kernel a periodic tick. This is the part that changes the kernel's character
and should be its own issue, its own review, and probably its own proposal. It
is what the sound engine wants; it is also what a scheduler would want later.

Splitting it this way means the library can ship, be documented and be tested
while the interrupt question is still being argued.

### Two caveats that belong in the code, not in folklore

**The reload quirk changes tempo.** A count of 1 behaves as 3 on the 1986 part
and 2 on the 1987, roughly +2 / +1 on every value. Music written against one
revision plays at the wrong speed on the other. `tmr-hz` should apply the
correction, and which revision it assumes must be a visible constant rather than
a hidden `+2`.

**A zero reload is not "stopped" on real hardware.** It re-asserts the interrupt
immediately. The RP2350 port deliberately deviates here and treats zero as
stopped, because a timer firing continuously would wedge the emulator; that is a
reasonable emulator choice and a bad library choice. `tmr-stop` should therefore
disable the source in `$FF92`/`$FF93` rather than write a zero reload.

---

## Testing

Three targets, in increasing order of truth:

1. **XRoar `-m coco3`.** Already the project's emulator of record, and it
   implements both devices, palette masking included.
2. **The RP2350 XRoar port** with `-DGIME_TIMER -DGIME_PALETTE`. A useful second
   opinion precisely because it is an independent implementation, and it carries
   58 host unit tests covering the bit layout, the intensity ramp, all 64
   colours staying distinct, the timer's two clock sources, the read-to-clear
   acknowledge and the enable-register behaviour.
3. **Real hardware**, which is the only place the reload quirk and the
   composite-vs-RGB difference can be settled for good.

A disagreement between (1) and (2) is itself informative: both are
implementations of the same document, so a difference means someone read it
differently, and that is worth resolving before real hardware.

---

## Sequencing

This proposal does not change the plan's Track A / Track B split. It slots in
as:

1. **1.1 GIME detection** — still the keystone; nothing here ships without it.
2. **`palette.fs`** (plan 1.2) then the colour tables (1.3). Days, visible
   immediately, recolours existing demos.
3. **`gime-timer.fs` layer 1** — the polled timer. Cheap, safe, useful on its own.
4. **`gime-timer.fs` layer 2** — the FIRQ handler. Its own issue and its own
   argument, bundled with whatever the sound engine needs.

## What this proposal does not cover

The MMU, the GIME graphics modes, 80-column text and hardware attributes, all of
which stay in Tier 2 of the main plan. Double-speed stays coupled to its sound
fix, per plan item 3.2: it must not ship alone.

## Per the project's commitments

Each item above becomes its own issue in `issues.jsonl` before work starts,
ships with XRoar verification, and updates `reference.html` and `lib/README.md`
alongside the code.
