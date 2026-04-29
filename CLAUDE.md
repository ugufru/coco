# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## IMPORTANT: No Memory Files
Do NOT use the `~/.claude/projects/*/memory/` system. Do not create MEMORY.md or any files in the memory directory. All persistent knowledge belongs in THIS file (CLAUDE.md) and nowhere else.

## Project Overview

What if the TRS-80 Color Computer had shipped with real developer tools instead of a cramped BASIC prompt? That's the question driving **CoCo Renovation** — a from-scratch development environment delivered on a ROM cartridge, giving the CoCo the toolchain it deserved in 1982 but never got. The real hardware runs. Only the software gets an upgrade.

The project includes a **Forth kernel and cross-compiler** (`forth/`), a growing collection of **SG4 semigraphics demos** (`src/`), and a **tutorial series** (`getting-started/`) that walks through building programs from scratch. The long-term vision lives in `COCO_RENOVATION.md`.

## Target Platform

The 6809 is one of the most elegant 8-bit CPUs ever made — two stack pointers, a real multiply instruction, and an addressing mode for every occasion. The CoCo wrapped it in a machine that sold for under $200 and brought computing home to millions.

- **CPU**: Motorola 6809
- **Hardware**: TRS-80 Color Computer (CoCo), any model with the pak (cartridge) slot
- **Delivery mechanism**: ROM cartridge with flash storage + microSD card
- **Memory constraint**: 64K ROM for the toolchain, 64K RAM reserved for user programs
- **Storage**: microSD card holds source files, assembled binaries, and projects (FAT16 or custom FS)

## Planned Components

Everything a developer needs, nothing they don't. Each component earns its bytes in the 64K ROM budget — no bloat, no compromise.

| Component | Role |
|-----------|------|
| Shell | Replaces the BASIC prompt; command line with path, history |
| Filesystem driver | FAT16 or custom FS on microSD |
| Screen editor | Real cursor movement, undo, search (32/40 column) |
| Assembler | Macros, labels, local scopes, include files, conditional assembly |
| Linker | Multi-object-file linking (the key gap vs. EDTASM+) |
| Debug monitor | Breakpoints, memory inspection, register display, disassembly |
| Transfer utility | XMODEM or serial bridge for PC file exchange |

## Existing Ecosystem to Build On / Integrate With

The CoCo community never stopped building. Four decades of accumulated tools, emulators, and OS projects mean we're not starting from zero — we're standing on shoulders.

- **lwasm** — modern 6809 assembler (cross-development); likely the cross-assembler used during development
- **XRoar / MAME** — accurate CoCo emulators with debugging; primary test environment before real hardware
- **CoCoSDC** — SD card interface for real CoCo hardware
- **DriveWire 4** — PC as disk server over serial connection
- **NitrOS-9** — modernized OS-9 multitasking OS for CoCo (reference for how a real OS handles the hardware)
- **CMOC** — working C compiler targeting 6809/CoCo (reference/alternative for higher-level components)
- **toolshed** — cross-development utilities

## Development Approach (Anticipated)

Write on a modern machine, test in an emulator, deploy to real iron. The classic embedded loop, applied to a machine from 1980.

Components will likely be written in **6809 assembly**, cross-assembled on a modern machine using **lwasm**, and tested under **XRoar or MAME** before deployment to real hardware. The workflow mirrors embedded development: write → cross-assemble → deploy to emulator → iterate → test on hardware.

The 64K ROM budget means each component must be designed with size discipline. The assembler, linker, and editor are the highest-complexity components and likely candidates to start with independently before integration.

## Key Design Principles (from COCO_RENOVATION.md)

The constraints aren't obstacles — they're the whole point. A 64K ceiling and an 8-bit register file force every design choice to be deliberate.

- **Renovation, not emulation** — the hardware runs authentically; only the software layer changes
- **Constraints are features** — the 64K limit and 6809 register pressure are intentional; they produce discipline
- **On-device workflow** — assembling on the CoCo itself (rather than cross-compiling) is a first-class design goal
- **Non-destructive delivery** — cartridge can be removed to restore a stock CoCo at any time

## Documentation Structure

Official documentation is HTML (`docs/`), not markdown. Markdown files are working notes for orientation.

- **`docs/tutorial.html`** — Getting Started tutorial (13 chapters), table of contents, entry point
- **`docs/reference.html`** — Language reference: kernel primitives, compiler directives, library words, memory map
- **`docs/00-about-forth.html` through `13-onto-your-coco.html`** — Tutorial chapters

Markdown working notes:
- **`forth/kernel/README.md`** — Kernel architecture, primitives, memory map, 6809 gotchas
- **`forth/tools/README.md`** — fc.py compiler pipeline and CODE word syntax

## Issue Workflow

- Issues are tracked in `issues.jsonl` at project root, NOT GitHub
- **Create the issue BEFORE starting work** — never retroactively. Each distinct change (bug fix, kernel enhancement, new demo, CODE conversion) gets its own issue.
- Issues must be tested before resolved, resolved before push

## Development Workflow

- Kill XRoar before relaunching: `pkill -9 xroar` before `make run`
- Never spam-launch XRoar — one launch, check result, wait
- Auto-launch XRoar for testing: pkill, build, and run automatically when ready to test
- Write theoretical analysis first, then implement, then re-measure
- Measure cycle counts, don't estimate — use `fc.py --cycles` and exact hardware constants
- Don't pause unless judgement, visual, or physical feedback is needed

## Access Boundaries

Only read, write, or execute files within these directories:
- `/Users/paul/github/coco` — project root
- `~/.claude/` — Claude Code config and memory

Do not access, search, or modify files outside these paths. If a task appears to require files elsewhere, ask first.


# CoCo Project Reference

## User
- Collaboration style — how we work best together

## Feedback
- Issues are in issues.jsonl, not GitHub
- Issue workflow rules — must exist before work, tested before resolved, resolved before push
- EXIT inside IF may miscompile — avoid EXIT inside IF/THEN in fc.py Forth
- Always track issues — create before starting, update during, never batch at end
- Create issues before work — never retroactively; separate issues for distinct changes
- Kill XRoar before relaunching — pkill xroar before make run
- Video-capture no longer works for screenshots — only captures webcam, not XRoar
- Use screen-capture MCP for XRoar testing — capture XRoar window to debug crashes and visual anomalies
- Never spam-launch XRoar — one launch, check result, wait
- SAM all-RAM register is $FFDF — $FFDF sets TY, $FFDE clears (even=clear, odd=set)
- Auto-launch XRoar for testing — pkill, build, and run automatically when ready to test
- Access boundaries — only access files in project root and ~/.claude/
- D register byte order — 8-bit values for ADDD go in B (low), not A (high)
- Accurate cycle calculations — measure, don't estimate; use fc.py --cycles and exact hardware constants
- Pre-decrement by 1 illegal for stores — STA ,-S / CLR ,-S undefined; use PSHS A instead
- Update docs before code — write theoretical analysis first, then implement, then re-measure
- Autonomous workflow — don't pause unless judgement/visual/physical feedback needed
- decb tool location — `~/bin/decb` (from Toolshed 2.2). The top-level `make dsk` target shells out to bare `decb` and will fail with "decb not found on PATH" if `~/bin` isn't on PATH. Use `PATH=~/bin:$PATH make dsk` (or invoke `~/bin/decb` directly when scripting) — do NOT report it as missing.

## Project
CoCo Renovation — on-device Forth development environment for the TRS-80 Color Computer.
Primary doc: `COCO_RENOVATION.md`. Tech reference: `coco_technical_reference.pdf`.

## Current State (2026-04-28)
Tutorial series complete: Getting Started ch1–13, all demos, calculator.
Kernel: 80+ primitives, parameterized for ROM mode (default) and all-RAM mode.
ROM-mode kernel ORGs at $2000, no bootstrap, BREAK→BASIC OK via exit-basic.
All 10 demos run in ROM mode on 32K (clock+fujinet-time need HDB-DOS-CC cart).
fc.py: inline_constants(), FVAR_* EQU export, auto-detect ROM vs all-RAM,
exposes build constants (font-base, vram-base, app-base, trig-base) as Forth
literals. Lacks BEGIN/WHILE/REPEAT.
fc.py quirks: preprocess_asm strips blank CODE block lines; ASCII-only
comments; CONSTANT requires a literal (not another constant).
fc.py entry point: top-level `main` call required at end of .fs file.

## Getting Started — Layout
- `getting-started/style.css` — landscape format, max-width: 1100px
- Chapter opener uses CSS grid (2-col): illustration right (260px), label+title left, green bar spans full width at bottom
- Mobile ≤640px reverts to flex-column with illustration full-width on top
- Horizontal padding throughout: 64px

## Getting Started — Adding Real Images to Illustration Placeholders
Images live in `getting-started/images/`. To replace a placeholder `div.illustration`:
1. Container div: add `style="width:Xpx; height:auto; padding:0; border:none; background:none;"`
   - opener: ~220px, aside: ~220px, small: ~200px (all landscape images need more width than defaults)
2. `<img>` tag: `style="width:100%; height:auto; display:block;"` + descriptive `alt` text
3. If a `.rule-box` immediately follows a floated illustration, add `style="overflow:hidden"` to
   prevent its border from bleeding under the float.

## Key Files
- `forth/kernel/kernel.asm` — 6809 Forth executor, assembles with lwasm
- `forth/kernel/Makefile` — `make` builds, `make run` launches XRoar
- `forth/tools/fc.py` — Forth cross-compiler (Forth source → DECB binary)
- `forth/hello/hello.fs` — Hello World source

## XRoar Setup
- Machine: `coco2bus` (CoCo 2, NTSC)
- ROMs: `~/.xroar/roms/bas12.rom` + `~/.xroar/roms/extbas11.rom`
- Run: `xroar -machine coco2bus -bas bas12.rom -extbas extbas11.rom -run build/combined.bin`

## Memory Map (current, 2026-04-28)

Two build profiles. ROM mode is the default; all-RAM is opt-in.

### ROM mode (default — 32K CoCo, BASIC ROMs alive)
- `$0050–$007F` — Kernel direct-page variables
- `$0400–$05FF` — VDG text/SG VRAM
- `$0600–$1DFF` — RG6 VRAM reservation (6 KB, kernel-reserved)
- `$2000–$2EAF` — Kernel code (KERNEL_ORG, ~3.7 KB)
- `$3000–$57FF` — App code + variables (APP_BASE, ~10 KB)
- `$5800–$59D7` — Font glyphs (FONT_BASE)
- `$5A00–$7DFF` — App heap / extra data (~9 KB)
- `$7800` — TRIG_BASE (sin table, 91 B)
- `$7E00` — Data stack base (U, grows down)
- `$8000` — Return stack base (S, grows down)
- `$8000–$BFFF` — Extended Color BASIC ROM
- `$C000–$DFFF` — Disk Extended Color BASIC ROM / cart ROM
- DECB exec = `START`, no bootstrap, no staging copy
- Built by `make` (default) — selects via `KERNEL_VARIANT=` (unset)

### All-RAM mode (opt-in — 64K CoCo, ROMs paged out)
- `$0050–$007F` — Kernel DP vars
- `$0400–$05FF` — VDG text VRAM
- `$0600–$1DFF` — RG6 VRAM reservation
- `$0E00` — Bootstrap (enables all-RAM via `STA $FFDF`, copies kernel)
- `$1000–$1EAF` — Staged kernel (DECB load addr)
- `$2000–$8FFF` — App code + vars (APP_BASE, 24 KB contiguous)
- `$9000–$91D7` — Font glyphs (FONT_BASE)
- `$86CC` — TRIG_BASE (sin table)
- `$DE00` — Data stack
- `$E000` — Return stack + kernel final location after copy
- DECB exec = `BOOTSTRAP`, fc.py remaps `$E000+` records to `$1000` staging
- Built by `make allram` — selects via `KERNEL_VARIANT=allram`

### Architecture
- Threading: ITC (Indirect Threaded Code), X=IP U=DSP S=RSP Y=scratch
- Kernel parameterized via lwasm `-DALL_RAM=1` flag and `KERNEL_ORG`,
  `APP_BASE`, `VRAM_BASE`, `FONT_BASE`, `TRIG_BASE`, `RSP_INIT`,
  `DSP_INIT` overrides
- fc.py auto-detects mode (no records ≥ `$E000` → ROM mode), reads
  `APP_BASE` from kernel.map, exposes build constants as Forth literals
  (`app-base`, `vram-base`, `font-base`, `trig-base`)
- `IFEQ KERNEL_ORG-$E000` in `fujinet.fs` gates SAM TY toggles to
  all-RAM only
- All-RAM SAM register: `STA $FFDF` sets TY (NOT `$FFDE` — even=clear)

## CoCo Keyboard Matrix
Each key has its OWN column — keys are NOT grouped by column as keyboard.fs claims.
Arrow keys: UP=$F7/$08, DN=$EF/$08, LT=$DF/$08, RT=$BF/$08 (all row $08, separate columns).
Number 0-9: columns $FE-$DF, all row $10.
Source of truth: kernel.asm KEY_TABLE (line ~763), NOT forth/lib/keyboard.fs.

## 6809 / lwasm Gotchas
- `,-U` (postbyte $C2) = **1-byte** pre-decrement — WRONG for 16-bit stack push
- `,--U` (postbyte $C3) = **2-byte** pre-decrement — correct; use `STD ,--U` to push
- Use `LDD ,U` + `LEAU 2,U` instead of `LDD ,U++` for explicit, readable stack pop

## 6809 Comparison Primitive Pattern
Do NOT load D before a conditional branch — `LDD #0` clobbers N/Z/V set by CMPD/SUBD.
Branch on flags first, then load result.

## DO/LOOP Implementation Notes
- IR: `('do',)` + `('label', name)` (label AFTER do, pointing to first body word)
- `do_counter` is global across all definitions — do NOT reset it inside `:`
- LOOP offset = label_addr − (offset_cell_addr + 2); negative = branch back
- Return stack during loop: TOS=index, NOS=limit (both 16-bit)

## Reference
- XRoar interaction — osascript keystroke injection, testing loop

## ASCII → VDG Encoding
`screen_byte = $40 | (ascii & $3F)`
For uppercase A-Z: screen_byte == ascii (no change needed).
