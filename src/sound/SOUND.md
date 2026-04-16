# Sound on the CoCo — Investigation Notes

*March 26, 2026 — Paul Cunningham + Claude*

## Goal

Add short sound effects to demos: beeps, zaps, booms. A few milliseconds each, synchronous (blocking the caller briefly).

## The CoCo Audio Hardware

The CoCo has two potential sound output paths:

1. **6-bit DAC** — PIA1 port A ($FF20), bits 7-2. Writes a 6-bit value that drives an analog output. Connected to the cassette port and the TV audio via an analog multiplexer.

2. **Single-bit sound** — PIA1 port B ($FF22), bit 1. A digital toggle that produces a click/square wave.

The analog multiplexer is controlled by:
- PIA0 CR-A ($FF01) bit 3 — MUX select LSB
- PIA0 CR-B ($FF03) bit 3 — MUX select MSB
- PIA1 CR-B ($FF23) bit 3 — sound source enable (CB2)

BASIC's `SOUND` command produces audible tones through the TV speaker.

## What We Tried

### 1. Direct DAC writes from Forth

Wrote `$FC` (high) and `$00` (low) to `$FF20` in a loop from Forth words. Various frequencies and durations. **No sound.**

### 2. Single-bit toggle on $FF22

Toggled bit 1 of `$FF22` in a loop. **No sound.**

### 3. PIA1 initialization

Configured PIA1 DDR ($FF20 via $FF21 bit 2) to set DAC bits as outputs. Set sound enable ($FF23 bit 3). Various combinations of control register values. **No sound.**

### 4. CODE words with tight assembly loops

Wrote `snd-tone`, `snd-sweep`, `snd-noise` as 6809 CODE words with tight DECB/BNE delay loops for precise timing. **No sound.**

### 5. PIA register dumps

Read all PIA1 registers ($FF20-$FF23) and displayed them. Found:
- DDR was already $FE (bits 7-1 output) — correct for DAC
- CR-A was $34 — correct (CA2=0, data mode)
- CR-B was $37 — bit 3 clear (sound disabled)

Set $FF23 bit 3 to enable sound. Verified registers read back correctly. **Still no sound.**

### 6. MUX select via PIA0

Based on a [working CoCo sound example](https://nowhereman999.wordpress.com/2017/03/30/zilog-z80-to-motorola-6809-transcode-part-020-sound-ideas/), configured PIA0 CR-A ($FF01) and CR-B ($FF03) bit 3 for MUX routing, plus PIA1 CR-B ($FF23) bit 3 for sound enable. **No sound.**

### 7. BASIC verification

Launched XRoar with BASIC ROMs. `SOUND 200,5` command produced clear audio. Confirmed XRoar's audio output works. Then typed a POKE loop to toggle $FF20 directly from BASIC:

```basic
10 POKE 65315,PEEK(65315) OR 8
20 FOR I=1 TO 500
30 POKE 65312,252
40 FOR J=1 TO 10:NEXT
50 POKE 65312,0
60 FOR J=1 TO 10:NEXT
70 NEXT
```

**This made sound from BASIC.** So raw DAC writes DO produce audio — but only when BASIC's environment is intact.

### 8. Bootstrap sound test

Added a DAC toggle loop to the kernel bootstrap at $0E00 — runs immediately on EXEC, before `ORCC #$50` (IRQ mask) and before `STA $FFDF` (all-RAM mode). **This made sound.** The chirp was clearly audible during boot.

### 9. Isolating the cause

Tried playing sound at various points:
- Before ORCC and FFDF: **sound works**
- After ORCC, before FFDF: not tested in isolation
- After both: **no sound**

### 10. Mode-switching approach

Created sound subroutines in the bootstrap area ($0E19) that switch back to ROM mode (`STA $FFDE`), unmask IRQs, play the DAC loop, then restore all-RAM mode (`STA $FFDF`).

**Problem discovered:** The Forth return stack (S register) points to $DFxx, which is in the ROM overlay range ($8000-$FEFF). When ROM mode is restored, $DFxx becomes BASIC ROM instead of our stack data. Solved by saving S and switching to a temporary stack at $0DFE (below the bootstrap) during sound playback.

The subroutine ran and returned successfully (proved by "GO" and "OK" appearing on screen). **But still no sound.**

### 11. Kernel-level call attempt

Tried calling `JSR SND_PLAY` from the kernel START routine (at $E000). **Crashed** — because the return address on S points into $E000 kernel space, which becomes BASIC ROM when `STA $FFDE` switches modes.

## What We Learned

1. **DAC audio works before all-RAM mode.** The bootstrap test at $0E00 produced clear audio. BASIC's environment, including PIA configuration, is fully intact at that point.

2. **All-RAM mode ($FFDF) kills DAC audio.** After `STA $FFDF`, writes to $FF20 produce no audible output, even with correct PIA1 DDR and control register configuration.

3. **Switching back to ROM mode ($FFDE) doesn't restore audio.** Even with IRQs unmasked and sound enable set, DAC writes during a temporary ROM-mode window produce no sound. Something about the transition or the PIA state is not being fully restored.

4. **The return stack lives in ROM overlay space.** S points to $DFxx, which is in the $8000-$FEFF range that toggles between RAM and ROM. Any code that switches modes must save S and use a temporary stack below $8000.

5. **IRQ masking may be a factor.** The working bootstrap test ran before `ORCC #$50`. BASIC's IRQ handler services the 60Hz VSYNC interrupt and may be involved in the audio output path. XRoar might use IRQ-driven audio mixing.

6. **BASIC's SOUND command uses a different mechanism than raw POKE.** The BASIC POKE loop to $FF20 produced sound, but our equivalent Forth/assembly code after all-RAM mode did not. BASIC's SOUND command likely uses an IRQ-driven timer approach that we can't replicate with IRQs masked.

## What We Don't Know

- **Does XRoar's audio emulation depend on specific PIA state or IRQ handling?** The emulator might synthesize audio output based on PIA interrupt events rather than raw register writes.

- **What exactly does `STA $FFDF` change about PIA behavior?** The SAM TY bit should only affect ROM/RAM mapping at $8000-$FEFF. PIA registers at $FF00-$FF3F should be unaffected. But something changes.

- **What does BASIC's SOUND routine actually do?** The Extended BASIC Unravelled disassembly would reveal the exact register sequence. We couldn't access the PDF content during this session.

## Next Steps

1. **Read the Lomont hardware doc** (`Lomont_CoCoHardware.pdf`) — it has the complete PIA/DAC/audio path documentation.

2. **Read XRoar source code** — specifically how `mc6821.c` connects to the audio subsystem. The answer is in how XRoar routes DAC writes to host audio.

3. **Disassemble BASIC's SOUND routine** — use XRoar's debugger or the Unravelled books to trace exactly what registers BASIC writes.

4. **Test with IRQ handler installed** — set up a minimal IRQ handler at $FFF8 that services PIA0 VSYNC, then unmask IRQs during sound. The IRQ handler might be necessary for XRoar's audio pipeline.

5. **Consider alternative: pre-rendered WAV** — generate sound effect WAV files and play them via a different mechanism, bypassing the DAC entirely.

## Files

- `forth/lib/sound.fs` — Sound library with CODE words (snd-tone, snd-sweep, snd-noise). Works conceptually but blocked by all-RAM audio issue.
- `src/sound/sound.fs` — Interactive demo program (keys 1-6 for different effects).
- `src/sound/Makefile` — Build and run the demo.
