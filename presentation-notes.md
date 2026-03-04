# CoCo / Bananas — Presentation Speaking Notes

*10-minute talk*

---

## Opening (1 min)

*Start with the hook — the central provocation.*

> "What if Tandy had made better software decisions in 1982?"

That question launched this project. The TRS-80 Color Computer had a genuinely elegant processor — the Motorola 6809. Two stack pointers. PC-relative addressing. Clean, orthogonal ISA. The hardware was never the problem. The cassette interface, the 32-column editor, the assembler that couldn't handle a project of any real size — those were. And they were all software.

---

## The Idea: Renovation, Not Emulation (1.5 min)

Almost everyone who revisits vintage hardware does one of two things: emulation (run the old thing in a new context) or preservation (archive what existed). This project is neither.

**Renovation** means the hardware runs authentically — you're still writing 6809 assembly, still constrained to 64K, still running on real hardware — but the software tooling layer gets replaced with something that was always *possible*, just never built.

The delivery mechanism: a ROM cartridge. Non-destructive. Pull it out, you have a stock CoCo. The original COCO_RENOVATION.md laid out the full vision: shell, filesystem driver, screen editor, assembler, linker, debug monitor.

---

## The Pivot to Forth (1.5 min)

As the project developed, a better answer emerged. Instead of reimplementing all those components from scratch in 6809 assembly — a years-long project — Forth offers a more elegant path.

The 6809 has two hardware stack pointers, S and U. Forth is defined by two stacks: a return stack and a data stack. This is not a coincidence — it's a perfect fit. The 6809 was practically designed to run Forth.

The architecture became **Indirect Threaded Code (ITC)**: a small kernel (~100 bytes of 6809 assembly) acts as the inner interpreter. Everything else — applications, programs, the tutorial programs — is cross-compiled Forth bytecode that the kernel executes natively.

The name for this became **Bananas**.

---

## What We Built: The Proof of Concept (2 min)

Three components:

**1. The kernel** (`forth/kernel/kernel.asm`) — 6809 assembly, cross-assembled with lwasm. Implements the ITC inner interpreter plus ~25 primitives: stack operations (DUP, DROP, SWAP, OVER), arithmetic (+, -, \*, /MOD), memory (@, !), I/O (EMIT, CR, KEY), control flow (DO, LOOP, 0BRANCH, BRANCH), comparisons (=, <>, <, >, 0=), and screen positioning (AT).

**2. The cross-compiler** (`forth/tools/fc.py`) — Python script that compiles Forth source to a DECB binary that loads directly on the CoCo. Handles colon definitions, variables, literals, DO/LOOP, IF/ELSE/THEN, BEGIN/AGAIN/UNTIL.

**3. Hello World** — the validation. Write a `.fs` file on a Mac, run `make`, launch XRoar, and "HELLO, WORLD!" appears on a CoCo 2 screen. That moment — working, real output on real (emulated) hardware — was the proof that the architecture holds.

The workflow: `hello.fs → fc.py → threaded bytecode → CoCo 6809`. The 6809 never knows how the bytecode got there. It just executes.

---

## The Tutorial: Getting Started with Color Forth (2 min)

The second major deliverable is a complete beginner's book: **Getting Started with Color Forth** — 13 chapters, styled as a proper period-appropriate manual with a cover, illustrations, and chapter programs.

The chapters take someone with no Forth experience from first principles to a complete interactive game:

1. Meet Your Stack — the foundation
2. Say Something — output
3. Make Your Own Words — colon definitions
4. The Stack Is Your Friend — stack manipulation
5. Remember Things — variables, @, !
6. Count and Loop — DO, LOOP
7. Decisions — IF/ELSE/THEN
8. Read the Keyboard — interactive programs
9. Numbers on Screen — arithmetic display
10. The Calculator — an RPN calculator (BEGIN…AGAIN)
11. Anywhere on Screen — AT, screen layouts
12. The Guessing Game — a complete game
13. Getting It onto Your CoCo — real hardware deployment

Each chapter has a working example program and DIY exercises. The tutorial is designed to run in XRoar now and on real hardware later.

---

## Future Potential (1.5 min)

The foundation is solid. Natural next directions:

**Near-term**: Serial loader (bit-banged via the CoCo's PIA) so bytecode can be sent over RS-232 from a modern machine. ROM cartridge image — kernel burned to flash, bootable from the pak slot. SD card integration via CoCoSDC.

**Hardware expansion**: An RP2350 co-processor — one core handling the 6809 bus, the other managing storage and services. The 6809 gets capabilities that didn't exist in 1987 without knowing anything changed.

**Portability**: The same bytecode binary runs on CoCo 1, CoCo 2, CoCo 3 — hardware differences are the kernel's problem, not the application's. That's a real distribution format for new CoCo software.

**The bigger picture**: The CoCo community has done extraordinary preservation work. What doesn't exist is anything genuinely new. This project is the U-turn — same hardware, different direction.

---

## Closing (30 sec)

The constraints here are features, not bugs. You feel the 64K. You feel the register pressure. The feedback is immediate. That's what makes this interesting — it's not about nostalgia, it's about what happens when you take a constrained, elegant system seriously.

---

## Things to Consider Adding

- **A demo moment** — if you can show XRoar running the guessing game live, even briefly, that lands better than any description
- **A slide of the kernel architecture** — the ITC threading diagram (X=IP, U=DSP, S=RSP) is clarifying for a technical audience
- **The tutorial cover page** — it's a strong visual artifact showing what the project *looks* like, not just what it does
- **The 6809 two-stack insight** — worth slowing down on; it's the "aha" moment that explains why Forth and this hardware are a natural pair
