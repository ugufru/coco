# Bare Naked Forth 1.1 — Modern Bytecode for the Color Computer

A cross-compiled Forth toolchain for the TRS-80 Color Computer. Write Forth
on a modern machine. Run it natively on the CoCo's 6809.

![Ten demos running on the CoCo](demos.png)

---

## How it works

No interpreter sitting between you and the metal. Your Forth compiles to threaded code on a modern machine and runs at full 6809 speed on the CoCo.

```
source.fs  →  fc.py (cross-compiler)  →  DECB binary  →  CoCo 6809
```

The kernel is a minimal ITC Forth executor written in 6809 assembly. It
implements the inner interpreter and a set of primitives. Everything else —
applications, games, tools — is cross-compiled Forth bytecode that the kernel
executes natively. The CoCo never sees source text.

---

## What's here

A kernel, a compiler, a tutorial, and a growing library of programs that prove it all works.

### Toolchain

| Path | What it is |
|---|---|
| `forth/kernel/kernel.asm` | 6809 ITC Forth executor kernel |
| `forth/tools/fc.py` | Forth cross-compiler (source → DECB binary) |
| `forth/hello/hello.fs` | Hello World — the first application |
| `forth/run.sh` | One-command build and run script |
| `forth/lib/` | Shared Forth libraries (graphics, fonts, sprites, RNG, sound, FujiNet) |
| `make/demo.mk` | Shared per-demo build rules (kernel selection, XRoar launch) |

### Documentation

| Path | What it is |
|---|---|
| `forth/PROJECT_SETUP.md` | **Project setup guide** — Makefile, fc.py options, kernel build overrides, common scenarios |
| `forth/kernel/README.md` | Kernel architecture, primitives, memory maps (both build profiles) |
| `forth/tools/README.md` | Cross-compiler internals, ITC threading, inline `CODE` syntax |
| `docs/tutorial.html` | Tutorial book: *Getting Started with Bare Naked Forth* (13 chapters) |
| `docs/reference.html` | Language reference — all words, stack effects, memory maps |
| `COCO_RENOVATION.md` | Original vision document |
| `ROADMAP.md` | Phased roadmap, organized by theme |
| `coco_technical_reference.pdf` | TRS-80 CoCo technical reference |

### Demos (`src/`)

All ten ship in `build/demos.dsk` (run `make dsk`).

| Path | Demo | Mode |
|---|---|---|
| `src/bounce/` | Four bouncing balls with HUD scoring | RG6 |
| `src/calculator/` | RPN calculator — 1234-key entry, M/R memory | text + SG4 digits |
| `src/clock/` | Analog + digital clock, FujiNet RTC sync | RG6 (double-buffered) |
| `src/fujinet-time/` | FujiNet wall-clock probe | text |
| `src/kaleidoscope/` | Symmetric pattern generator | SG4 |
| `src/rain/` | Digital rain (Matrix-style falling glyphs) | text |
| `src/rg-test/` | RG6 + sprite/line/sin-fan visual test | RG6 |
| `src/tetris/` | Bare Naked Tetris | SG4 |
| `src/typewriter/` | Bare-metal keyboard echo (PIA0 scan) | text |
| `src/vdg-modes/` | Cycles all 11 MC6847 display modes | every mode |

---

## Quick start

From zero to "HELLO, WORLD!" on a CoCo 2 screen in under a minute.

Requires `lwtools` and `xroar` (`brew install lwtools xroar`). CoCo 2 ROM
images (`bas12.rom`, `extbas11.rom`) in `~/.xroar/roms/`.

```sh
cd forth
./run.sh hello/hello.fs           # cross-compile + launch hello.fs
```

To run any Forth program:

```sh
./run.sh path/to/program.fs
```

To build and run a specific demo:

```sh
cd src/tetris && make run
cd src/clock  && make run         # clock needs HDB-DOS-CC cart, see Makefile
```

To build all ten demos as a CoCo disk image (`build/demos.dsk`):

```sh
make dsk                          # from project root; needs Toolshed's `decb`
```

For starting your own project, see [`forth/PROJECT_SETUP.md`](forth/PROJECT_SETUP.md).

---

## Status

**Version 1.1** — Released April 2026.

The foundation is solid. The kernel boots, the compiler works, and real programs run on real (emulated) hardware.

Working: kernel boots, clears screen, executes cross-compiled Forth bytecode.
All 13 tutorial chapters complete with working example programs.
Ten demo applications: Tetris, Kaleidoscope, Rain, Bounce, Typewriter, RPN Calculator, RG-Test, VDG Modes, Clock, FujiNet Time.

**1.1 highlights:** ROM-mode default kernel (32K CoCo, BASIC ROMs alive,
clean BREAK to OK prompt). Build profiles: ROM (kernel at `$2000`,
default) and all-RAM (kernel at `$E000`, opt-in via `KERNEL_VARIANT=allram`).
Kernel-driven build constants (`font-base`, `vram-base`, `app-base`,
`trig-base`) keep apps profile-agnostic. See [`forth/PROJECT_SETUP.md`](forth/PROJECT_SETUP.md).

### Kernel primitives (84 words)

| Group | Words |
|---|---|
| Threading | DOCOL, DOVAR, EXIT, LIT, LIT0, LIT1, LIT2, LIT3, LIT4, LITM1 |
| Stack | DUP, DROP, SWAP, OVER, ?DUP, 2DUP, 2DROP, ROT, PICK |
| Arithmetic | +, -, \*, /MOD, NEGATE, MIN, MAX, ABS, 2\*, 2/, 0MAX, 0MIN, WITHIN |
| Memory | @, !, C@, C!, FILL, CMOVE, +! |
| Bitwise | AND, OR, XOR, INVERT, LSHIFT, RSHIFT |
| I/O | EMIT, CR, TYPE, COUNT, KEY, KEY?, KBD-SCAN |
| Control flow | 0BRANCH, BRANCH, DO, LOOP, +LOOP, I, J, UNLOOP |
| Comparison | =, <>, <, >, U<, 0= |
| Return stack | >R, R>, R@ |
| Screen | AT, VSYNC, WAIT-PAST-ROW, COUNT-BLANKING |
| RG6 graphics | RG-PSET, RG-LINE, RG-CHAR, SPR-DRAW, SPR-ERASE-BOX |
| Beam system | BEAM-TRACE, BEAM-DRAW-SLICE, BEAM-FIND-OBSTACLE, BEAM-SCRUB-POS |
| Spatial | MDIST |
| Data | sprite-data, font-data |
| System | HALT |

Library-level CODE words (not in kernel) include `prox-scan`, `beam-restore-slice`,
`basic-cold`, the `dw-write`/`dw-read` FujiNet primitives, and per-app
inline assembly. See [`forth/lib/`](forth/lib/) and individual demo
sources for examples.

### Build-mode constants

`fc.py` exposes these as Forth literals so source builds against either
profile unchanged:

| Forth name | What it locates | ROM mode | All-RAM |
|---|---|---|---|
| `app-base` | App code start | `$3000` | `$2000` |
| `vram-base` | RG6 VRAM (kernel-reserved 6K) | `$0600` | `$0600` |
| `font-base` | `init-font` glyph table | `$5800` | `$9000` |
| `trig-base` | `init-sin` sine lookup | `$7800` | `$86CC` |

### Cross-compiler (fc.py)

| Feature | Status |
|---|---|
| Literals, word calls | done |
| Colon definitions, EXIT | done |
| VARIABLE, @, ! | done |
| CHAR | done |
| DO, LOOP, I | done |
| IF, ELSE, THEN | done |
| BEGIN, AGAIN, UNTIL | done |
| Constants (CONSTANT) | done |
| INCLUDE | done |
| CODE ... ;CODE (inline 6809 assembly) | done |
| S" (string literals), ." (print string) | done |

---

## Tutorial

Thirteen chapters take you from your first stack push to a working game running on vintage hardware. No prior Forth or 6809 experience needed.

`docs/` contains *Getting Started with Bare Naked Forth* — a 13-chapter beginner's
book. Each chapter has a working example program and DIY exercises.

| # | Chapter | Concepts |
|---|---------|----------|
| 1 | Meet Your Stack | stack, EMIT, HALT, CHAR |
| 2 | Say Something | colon definitions, CR |
| 3 | Make Your Own Words | building a vocabulary |
| 4 | The Stack Is Your Friend | DUP, DROP, SWAP, OVER |
| 5 | Remember Things | VARIABLE, @, ! |
| 6 | Count and Loop | DO, LOOP, I |
| 7 | Decisions | IF, ELSE, THEN, comparisons |
| 8 | Read the Keyboard | KEY, interactive programs |
| 9 | Numbers on Screen | \*, /MOD, printing numbers |
| 10 | The Calculator | BEGIN…AGAIN, RPN calculator |
| 11 | Anywhere on Screen | AT, fixed screen layouts |
| 12 | The Guessing Game | a complete interactive game |
| 13 | Getting It onto Your CoCo | CoCoSDC, DriveWire, cassette |

Open `docs/index.html` in a browser to read it locally.

---

## Roadmap

### Platform

Where this goes next — from software-only to cartridge hardware.

- Serial loader — bit-banged via PIA ($FF20/$FF22) for loading bytecode over RS-232
- ROM cartridge image — kernel + loader burned to flash, bootable from the pak slot
- RP2350 co-processor — one core on the 6809 bus, the other managing storage and services

### Documentation & demos

Turning the docs into a live, interactive showroom for the toolchain.

- Tutorial flow audit — re-read all 13 chapters against the current kernel and smooth the seams where new material has made the old flow rough
- Demo appendix — a featured page per demo, built into the tutorial, showing what each one exercises and why
- Runnable docs — embed XRoar (WASM) so every demo runs inline in the browser, no install required
- Static site — host `docs/` as a public website once the embedded player is wired up
- Bespoke illustrations — replace tutorial placeholders with original artwork
- DSK workflow — document the `make dsk` path from Forth source to a CoCoSDC-loadable disk image

### Workflow

Writing about how we built this, so others can do the same.

- Working with Claude — a guide to the modern Bare Naked Forth development loop with Claude Code as a collaborator

---

*This repository — code, documentation, and tutorial — was generated with [Claude](https://claude.ai/) (Anthropic).*
