# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
- **`src/spacewarp/SPEC.md`** — Game architecture and data structures
- **`src/spacewarp/FRAME_BUDGET.md`** — Cycle accounting and optimization history

## Issue Workflow

- Issues are tracked in `issues.jsonl` at project root, NOT GitHub
- **Create the issue BEFORE starting work** — never retroactively. Each distinct change (bug fix, kernel enhancement, new demo, CODE conversion) gets its own issue.
- Issues must be tested before resolved, resolved before push
- No pushing until v1.0 — public repo, keep commits local until Space Warp is complete

## Development Workflow

- Kill XRoar before relaunching: `pkill -9 xroar` before `make run`
- Never spam-launch XRoar — one launch, check result, wait
- Auto-launch XRoar for testing: pkill, build, and run automatically when ready to test
- Write theoretical analysis first, then implement, then re-measure
- Measure cycle counts, don't estimate — use `fc.py --cycles` and exact hardware constants
- Check app binary size after adding code — it can overflow into VRAM or data region
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
- App binary can overflow into VRAM — check sizes after adding code
- No pushing until v1.0 — public repo, keep commits local until Space Warp is complete
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
- Development archives for blog — screenshot diary in archives/ for storytelling
- Autonomous workflow — don't pause unless judgement/visual/physical feedback needed

## Project
CoCo Renovation — on-device Forth development environment for the TRS-80 Color Computer.
Primary doc: `COCO_RENOVATION.md`. Tech reference: `coco_technical_reference.pdf`.

## Current State (2026-03-30)
Tutorial series, calculator, Getting Started ch1–13: all COMPLETE.
Space Warp: core gameplay complete, nearing v1.0.
App size: 24,569 bytes, headroom 7 bytes. Data at $8000+, font at $6000.
Budget: 14,930cy/frame. Slot-based think scheduling (3 Jovians, skip 1-6).
HSYNC beam-chasing (#262): after VSYNC, wait for beam to pass sprites before VRAM writes.
Beam system (#259): paint-black erase + draw-stars redraw + beam-scrub-sprites.
Bounce demo (src/bounce/): HSYNC testbed, 4 balls, mode switching, font HUD.
fc.py: inline_constants(), FVAR_* EQU export. Lacks BEGIN/WHILE/REPEAT.
fc.py quirks: preprocess_asm strips blank CODE block lines; ASCII-only comments.

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

## Memory Map (current, 2026-03-23)
- `$0050–$0082` — Kernel variables (direct page, 51 bytes)
- `$0600–$1FFF` — RG6 VRAM (6144 bytes, set by rg-init after boot)
- `$0E00` — Bootstrap (copies staged kernel to $E000, enables all-RAM)
- `$1000` — Staged kernel (DECB load addr; copied to $E000 at boot)
- `$2000–$7FF9` — App code (24,569 bytes, 7 bytes headroom to $8000)
- `$8000–$8EF4` — Game data (all-RAM region, see spacewarp.fs CONSTANTs)
- `$8050` — BASE-POS (2 bytes: x, y) — NOT $8056!
- `$8054` — SHIP-POS (2 bytes: x, y)
- `$8056` — JOV-DMG (3 bytes, one per Jovian) — NOT $8050!
- `$80B0–$80B3` — QCOUNTS shadow bytes (nstars, njovians, hasbase, hasbhole) for CODE word access
- `$9000–$91D8` — Font glyphs (59 glyphs × 8 bytes, all-RAM region)
- `$DE00` — Data stack base (U, grows down)
- `$E000` — Return stack init (S, grows down from $DFFF)
- `$E000–$E869` — Kernel code (final location, all-RAM mode, 53 primitives + DOVAR data)
- `$E86A–$FEFF` — Static data / kernel growth (~5.7K)
- fc.py remaps kernel DECB records from $E000+ to $1000 (staging)
- All-RAM mode via `STA $FFDF` (NOT $FFDE — $FFDF sets TY, $FFDE clears)
- **Headroom**: App ends $7FF9, data starts $8000 — 7 bytes free
- Data relocated from $75xx–$7Exx to $8000+ all-RAM region (commit 68e10b9)

## Architecture
- Threading: ITC (Indirect Threaded Code)
- X = IP, U = DSP, S = RSP, Y = scratch
- Kernel at $E000 (all-RAM mode); bootstrap at $0E00 copies staged kernel from $1000
- App at $2000 is cross-compiled Forth; combined into single DECB binary
- VRAM at $0600 (set by rg-init); app is contiguous (no --hole needed)

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
- Architecture SVG — how to update the Space Warp mind map
- [AI Diversity Strategy](src/spacewarp/AI_DIVERSITY_STRATEGY.md) — Jovian genome system spec (issues #172–#179)
- XRoar interaction — osascript keystroke injection, game controls, testing loop

## ASCII → VDG Encoding
`screen_byte = $40 | (ascii & $3F)`
For uppercase A-Z: screen_byte == ascii (no change needed).
