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

## CoCo Keyboard Matrix

The `forth/lib/keyboard.fs` file has the matrix layout WRONG. The source of truth is `forth/kernel/kernel.asm` KEY_TABLE (~line 763).

Each key has its own column — keys are NOT grouped by column as keyboard.fs claims.

| Key | Column strobe | Row bit |
|-----|---------------|---------|
| UP  | `$F7` | `$08` |
| DN  | `$EF` | `$08` |
| LT  | `$DF` | `$08` |
| RT  | `$BF` | `$08` |

Arrow keys: all row `$08`, each in a separate column. Number keys 0–9: columns `$FE`–`$DF`, all row `$10`.

## Confusable Addresses

These addresses are 6 bytes apart and have been mixed up in CODE words, causing real bugs:

| Address | Constant | Contents |
|---------|----------|----------|
| `$8050` | `BASE-POS` | Base x, y (2 bytes) |
| `$8054` | `SHIP-POS` | Ship x, y (2 bytes) |
| `$8056` | `JOV-DMG`  | Jovian health, 3 bytes (one per Jovian) |

## fc.py Gotchas

- **EXIT inside IF/THEN may miscompile** — avoid using EXIT inside IF blocks in fc.py Forth
- **ASCII-only in CODE blocks** — Unicode characters (arrows, em dashes) in CODE block comments break lwasm
- **Blank lines in CODE blocks** — `preprocess_asm` strips blank lines from CODE blocks due to an lwasm local label quirk
- **fc.py lacks BEGIN/WHILE/REPEAT** — only BEGIN/AGAIN and BEGIN/UNTIL are supported

## 6809 Additional Gotchas

- **D register byte order**: For 8-bit values used with ADDD, the value goes in B (low byte), not A (high byte). `CLRA` + `LDB value` → `ADDD`.
- **Pre-decrement by 1 is illegal for stores**: `STA ,-S` and `CLR ,-S` are undefined on the 6809. Use `PSHS A` instead.
- **No CMPA B instruction**: Cannot directly compare two 8-bit registers. Push one to the stack first: `PSHS B` + `CMPA ,S+`.

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
