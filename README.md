# Bananas — Modern Bytecode for the Color Computer

This repository is the starting point for something that hasn't been done:
writing fully realized, modern applications for the TRS-80 Color Computer
using a cross-compiled Forth bytecode toolchain — no traditional native
development, no on-device assembler, no 1982 workflow.

This is Bananas.

The CoCo runs the code. Everything else happens on modern hardware.

---

## The idea

The Color Computer community has done extraordinary work. Perfect emulation.
Perfect preservation. Every disk image archived. Every cartridge documented.
The archaeology is complete.

What doesn't exist is anything new.

This project is the U-turn. Same hardware. Different direction.

The 6809 is an elegant processor that was let down by its software ecosystem.
The cassette interface, the 32-column editor, the assembler that couldn't
handle a real project — those were accidental limitations, not essential ones.
This project removes them without touching the hardware.

---

## How it works

**Write Forth on a modern machine. Run it natively on the CoCo.**

```
hello.fs  →  fc.py (cross-compiler)  →  threaded bytecode  →  CoCo 6809
```

The kernel is a minimal Forth executor (~100 bytes of 6809 assembly) that
lives in ROM or gets loaded into RAM. It implements the inner interpreter and
a small set of primitives. Everything else — applications, games, tools — is
cross-compiled Forth bytecode that the kernel executes natively.

The bytecode is the distribution format. It can be:
- Sent over serial
- Loaded from SD card
- Burned to a ROM cartridge
- Distributed as a disk image
- Downloaded and run

The 6809 never knows how it got there. It just executes.

---

## The hardware target

The near-term target is the CoCo 2 running the kernel from a ROM cartridge,
with an RP2350 co-processor providing storage, services, and dynamic memory
mapping. One RP2350 core handles the 6809 bus; the other manages the outside
world. The 6809 executes native bytecode at full speed with access to
capabilities that didn't exist in 1987.

The kernel is the portability layer. The same bytecode binary runs on a CoCo 1,
CoCo 2, CoCo 3, or any machine with a compatible kernel. Hardware differences
are the kernel's problem, not the application's.

---

## What's here

| Path | What it is |
|---|---|
| `forth/kernel/kernel.asm` | 6809 ITC Forth executor kernel |
| `forth/tools/fc.py` | Forth cross-compiler (source → DECB binary) |
| `forth/hello/hello.fs` | Hello World — the first application |
| `forth/kernel/README.md` | Kernel architecture and build instructions |
| `getting-started/` | Tutorial book: *Getting Started with Color Forth* |
| `COCO_RENOVATION.md` | Original vision document |
| `coco_technical_reference.pdf` | TRS-80 CoCo technical reference manual |

### Build and run

```sh
cd forth/kernel
make        # assemble kernel + compile hello.fs → build/combined.bin
make run    # launch in XRoar
```

Requires `lwtools` and `xroar` (`brew install lwtools xroar`). CoCo 2 ROM
images (`bas12.rom`, `extbas11.rom`) in `~/.xroar/roms/`.

---

## Status

Working: kernel boots, clears screen, executes cross-compiled Forth bytecode.
`HELLO, WORLD!` runs on a CoCo 2 from a Forth source file written on a Mac.

Six tutorial chapters complete.

---

## Roadmap

This project has three tracks in flight. Here's where each one stands.

---

### Track 1 — Tutorial book (`getting-started/`)

A complete beginner's guide: *Getting Started with Color Forth*. Eleven
chapters, each with a chapter program and DIY exercises.

| # | Chapter | Status |
|---|---------|--------|
| 1 | Meet Your Stack | done |
| 2 | Say Something | done |
| 3 | Make Your Own Words | done |
| 4 | The Stack Is Your Friend | done |
| 5 | Remember Things | done |
| 6 | Count and Loop | done |
| 7 | Decisions | not started |
| 8 | Read the Keyboard | not started |
| 9 | Anywhere on Screen | not started |
| 10 | Guess My Number | not started |
| 11 | Getting It onto Your CoCo | not started |

Each chapter requires:
- A chapter HTML file in `getting-started/`
- A working example `.fs` file in `getting-started/examples/NN/`
- Any new kernel primitives needed by that chapter
- Corresponding `fc.py` compiler support for any new control structures

---

### Track 2 — Kernel primitives (`forth/kernel/kernel.asm`)

The kernel must grow to support the tutorial chapters and eventual applications.

| Primitive group | Status |
|----------------|--------|
| Stack: DUP, DROP, SWAP, OVER | done |
| Arithmetic: +, - | done |
| Memory: @, ! | done |
| Output: EMIT, CR | done |
| Control: DO, LOOP, I | done |
| Comparison: =, <>, <, >, 0= | not started |
| Branching: IF/ELSE/THEN support (0BRANCH, BRANCH) | not started |
| Input: KEY | not started |
| Cursor: AT (row/col positioning) | not started |
| Math: *, /, MOD, ABS, MIN, MAX | not started |
| Logic: AND, OR, XOR, NOT | not started |
| Return stack: R>, >R, R@ | not started |
| String output: TYPE, COUNT | not started |

---

### Track 3 — Compiler (`forth/tools/fc.py`)

The cross-compiler must support every control structure used in the tutorials.

| Feature | Status |
|---------|--------|
| Literals, word calls | done |
| Colon definitions, EXIT | done |
| VARIABLE, @, ! | done |
| CHAR | done |
| DO, LOOP, I | done |
| IF, ELSE, THEN (forward branch + backpatch) | not started |
| BEGIN, UNTIL / BEGIN, WHILE, REPEAT | not started |
| String literals (S", .") | not started |
| Constants (CONSTANT) | not started |

---

### Beyond the tutorial

Once the eleven chapters and their supporting infrastructure are complete,
the next phase is real hardware deployment:

- Serial loader (bit-banged via PIA at $FF20/$FF22) for loading bytecode over RS-232
- ROM cartridge image: kernel + loader burned to flash, bootable from the pak slot
- SD card integration: store and load `.bin` files from a CoCoSDC-compatible interface
