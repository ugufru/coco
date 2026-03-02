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

The first wave.
