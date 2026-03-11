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
