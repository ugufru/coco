# CoCo Renovation — Modernizing the 6809 Development Experience

*February 19, 2026*

---

## The Idea

The difference between *emulation* and *renovation*. Emulation runs the old thing in a new context. Renovation keeps the hardware authentic but upgrades the tooling layer. Almost nobody does renovation, even when the hardware supports it completely.

The TRS-80 Color Computer had 64K of RAM, a Motorola 6809 processor, a ROM cartridge slot, and a development experience defined by cassette tapes, 32-column editors, and assemblers that couldn't handle a project of any real size (EDTASM+ on ROM cartridge, saving to cassette or disk). The hardware was never the embarrassing part. The software ecosystem of 1982 was.

The question: what if you answered "what if Tandy had made better software decisions" with a ROM cartridge you can plug in today?

---

## Why the 6809 Is Worth Renovating

The 6809 is not nostalgia — it's an objectively elegant processor:

- Two 16-bit index registers (X, Y)
- Two stack pointers (S and U) — call stack and data stack as first-class concepts
- PC-relative addressing throughout — position-independent code is natural, not a hack
- Clean orthogonal ISA, clearly influenced by the PDP-11
- Rich addressing modes without the irregularities of the Z80 or 6502

Writing 6809 assembly is actually pleasant. The ISA isn't the limitation of a vintage CoCo. The cassette interface, the 32-column screen editor, the assembler that couldn't handle a project of real size — those are the limitations. And they're all software.

---

## The ROM Cartridge as Delivery Mechanism

The CoCo pak slot is exactly the right vehicle. It's:

- Non-destructive — pull the cartridge, you have a stock CoCo
- Clean-booting — maps into a well-defined address space
- Designed for this — Tandy intended cartridges to replace or extend the OS

A modern cartridge with flash storage and an SD card controller could provide a complete self-contained development environment. The flash holds the OS/tools ROM. The SD card holds source files, assembled binaries, and projects.

---

## What the Cartridge Provides

A complete on-device renovation environment, fitting comfortably in ROM:

| Component | Description |
|---|---|
| Shell | Replaces the BASIC prompt; command line with path, history |
| Filesystem driver | FAT16 or a simple custom FS on microSD |
| Screen editor | Real cursor movement, undo, search — even at 32/40 columns |
| Assembler | Macros, labels, local scopes, include files, conditional assembly |
| Linker | The thing EDTASM+ most painfully lacked |
| Debug monitor | Breakpoints, memory inspection, register display, disassembly |
| Transfer | XMODEM or serial bridge for moving files to/from a modern machine |

That's the complete toolchain. It fits in 64K of ROM, leaving the full 64K of RAM for your program.

---

## The Workflow

Write code → save to SD → assemble on-device → run → inspect → iterate.

Similar in structure to the PyGamer workflow (write externally, deploy, test), except the assembler runs *on the CoCo itself*. That distinction matters. There's something fundamentally different about programming a machine on itself versus cross-compiling from a laptop. The constraints are present in your hands, not abstracted away. You feel the 64K. You feel the register pressure. The feedback is immediate and embodied.

---

## What Already Exists

The CoCo community has gotten partway there. The pieces exist, mostly as cross-development tools rather than a cohesive on-device experience:

| Tool | What it does |
|---|---|
| CoCoSDC | SD card storage interface for real CoCo hardware |
| DriveWire 4 | Modern PC acts as disk server over serial connection |
| CMOC | Working C compiler targeting 6809/CoCo |
| NitrOS-9 | Modernized OS-9 — a real multitasking OS for the CoCo |
| lwasm | Excellent modern 6809 assembler (cross-development) |
| toolshed | Cross-development utilities |
| XRoar / MAME | Accurate emulators with debugging support |

What doesn't exist: a self-contained on-device environment that makes the whole workflow feel cohesive and modern without leaving the machine.

---

## The Philosophical Point

Why *wouldn't* you do this?

The hardware works. The processor is elegant. The only things that made it feel limited were the ecosystem choices of 1982. A ROM cartridge is just a different set of software ROM choices. You're not modifying the hardware, you're not emulating anything — you're answering the question "what if they'd made better software decisions" with a physical object you can plug in and pull out.

This isn't about making the CoCo into something it isn't. It's about making it into what it always *could* have been. The 6809 was running multitasking operating systems (OS-9) in the early 1980s. The hardware had headroom. The tooling didn't keep up.

Renovating the tooling layer of a functioning vintage platform preserves authenticity — you're still writing 6809 assembly, still constrained to 64K, still running on real hardware — while removing the friction that was always accidental rather than essential.

---

## Connection to This Project

The PyGamer Sequencer is a useful frame. We've been doing embedded development the "modern" way on a constrained microcontroller: clean architecture, hardware/logic separation, comprehensive tests, careful memory management. The constraints produced discipline, not bad engineering.

The CoCo with renovated tooling would offer the same: real constraints that produce real discipline, but without the accidental frustrations of 1982 software. The 64K limit is interesting. The cassette interface is not.

---

*Initial notes — to be developed further*
