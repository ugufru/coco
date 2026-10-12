# Umbilical Forth: a live host-to-target compile link

*August 2026, Paul Cunningham + Claude. Tracking issue: #552.*

*Status: proposal. No code written. The first implementation slice is described
at the end and should be filed as its own issue before work starts.*

## Relationship to #551 (read this first)

This proposal is **development infrastructure for** the on-device self-hosting
Forth epic (#551), not an alternative to it.

That distinction matters because an earlier draft of this analysis argued the
opposite. Reading ROADMAP.md (reset 2026-06-16) and COCO_RENOVATION.md in
isolation suggests the on-device question is still open, and that the manifesto
leans against it ("Development belongs on a modern machine ... The CoCo is the
target. Your desktop is the workshop", and of the RP2350 boards, "None of these
do on-device compilation, and they do not need to"). Issue #551, filed
2026-07-19, settled the fork the other way and carries the detailed design.
ROADMAP.md was never updated to match. See #553.

So: the CoCo is going to compile on itself. This proposal is about making that
easier to build, and about making real hardware pleasant to iterate on in the
meantime.

## The idea

Put an RP2350 or RP2040 cartridge on the expansion bus as a live umbilical
between the host toolchain and a running CoCo.

The host keeps fc.py and the dictionary. The CoCo gains a small resident port
driver and nothing else. Individual words are compiled on demand and poked
straight into the running target, then executed, with results reported back. The
edit-compile-run loop stops being "build an image, relaunch the emulator" and
becomes "define a word, run it, look at the stack", on real iron.

## Why the existing architecture already fits

The decisive fact is that our dictionary is **headerless**.

In `compile_forth` (tools/fc.py:864) the target layout is:

```
colon word:   [DOCOL][body ...][CFA_EXIT]
CODE word:    [FDB self+2][machine code]
variable:     [DOVAR][data cell]
```

There are no name fields and no link fields on the target. `word_cfa` is a
host-side Python dict, and ITC threads reference absolute CFAs.

The consequence is that adding a word to a running target is:

1. assemble the thread against already-known CFAs,
2. poke the bytes at a fresh address,
3. record `word_cfa[name] = addr` on the host.

There is no target-side dictionary linking to implement. This is the property
that makes the whole idea cheap, and it falls out of a design decision that was
made for completely different reasons.

The kernel cooperates as well. kernel.asm:768 notes "Uses direct PIA0 scanning,
no ROM dependency", so the kernel can be loaded and run without BASIC ever
initializing. The only ROM-mode feature lost is the clean BREAK exit to `OK`,
which does not matter when the host is driving the machine.

## What this buys #551

1. **The first spike gets an apparatus.** #551's sharpest de-risk is the postfix
   6809 assembler plus indexed-addressing postbyte encoder, proved by
   "assembling a known CODE word on-device and diffing emitted bytes against
   lwasm's output for the same instructions". That is inherently a host-target
   diff. The umbilical makes it an automated loop against real hardware instead
   of a manual XRoar exercise. Roughly 16 addressing modes times the operand
   variants is a lot of cases to check by hand.

2. **The symbol sidecar and `word_cfa` are the same object.** #551 point (3)
   wants fc.py to emit a `.sym` sidecar per binary. The umbilical host is
   already maintaining exactly that table for a live session. The incremental
   compile work below produces the `session.sym` case as a side effect.

3. **The cartridge is already #551's assumed platform.** #551 frames the
   renovation cartridge as the delivery vehicle (flash for the resident image,
   microSD for bulk). Bringing that hardware path up early is work #551 needs
   done regardless.

## Effect on #406, #404, #405

#406 (RP2350 co-processor, icebox/high) is currently filed as downstream of #405
(ROM cartridge), which is filed as needing #404 (bit-banged RS-232 loader). The
cartridge collapses that chain:

- **#404 becomes unnecessary.** Its stated purpose is iterating on real hardware
  without pulling the cart each cycle. USB plus a spoonfed loader does that with
  no target code and no PIA bit-banging.
- **#405 stops being a prerequisite.** Loading the kernel by feeding the halted
  6809 instructions is itself the delivery vehicle, with no burning and no
  pull-the-cart cycle. A ROM cart remains desirable as a *product*, but it is no
  longer on the critical path to a working co-processor link.

#406 is therefore no longer blocked by anything.

## What Track 3 gets nearly free

- **#27 WORDS.** Its description assumes fc.py must emit a word table into the
  binary. In this model it needs nothing on the target at all: it is a query
  against `word_cfa`.
- **#26 `.S`.** Read U and dump cells over the peek path. No target code.
- **#25 SEE / disassembler.** The host holds both the source and the thread, so
  decompilation happens host-side. No 6809 disassembler on the target.

These are worth having on their own, and they are also the introspection layer
#551 eventually wants natively. Building them host-side first pins down the
behaviour before it has to fit in 6809.

## How the hardware does it

The reference implementation is Henry Strickland's copico-bonobo (MIT, and
credited by name in COCO_RENOVATION.md). The mechanism worth understanding:

On reset the cartridge asserts HALT, feeds the 6809 a synthetic reset vector,
then repeatedly unhalts it for exactly six bus cycles at a time while driving
the data bus directly. Each six-cycle window carries two three-byte
instructions, `LDD #immediate` followed by `STD extended`. The effect is a write
of arbitrary data to an arbitrary address, with no cooperating software on the
CoCo at all. Run that over a table and you have loaded a program into RAM. A
final spoonfed `JMP extended` transfers control to it.

Throughput is roughly 20 microseconds per byte in the reference firmware,
dominated by a fixed inter-instruction delay rather than by the bus cycles. Our
4K kernel lands in about 80 milliseconds. There is an available factor of two in
the converter, which currently emits `STD` but fills only one of the two bytes.

Once the kernel is running, a resident driver talks to the cartridge through a
decoded control/status/data port trio, and the link becomes an ordinary
request-response channel.

## What still has to be built

**A target resident.** The port driver plus a loop that executes a CFA and
reports back status, stack depth, and the top few cells. That return path is
what makes this a development environment rather than a write-only pipe. It is
the only new 6809 in the plan and it is small.

**Chunked loading.** The reference firmware caps a transfer at 64 bytes, so the
kernel image needs a chunked loader on the host side. Individual words usually
fit in a single transfer, which is a pleasant accident of Forth's granularity.

**Host session service.** A persistent process owning the serial link, the
dictionary, and a target-RAM allocator, exposing define / run / peek / words /
reset. Whether that is a plain CLI or an MCP server is an open question and does
not need answering yet.

## Risks and unknowns

- **Crash recovery.** The kernel runs `ORCC #$50` (kernel.asm:1699), so a wedged
  word hangs rather than trapping. Reset re-triggers the loader automatically,
  which is the recovery path, and it is fast. An `/NMI` escape hatch exists in
  the hardware but is gated by the cartridge's spoon-mode signal. Unverified.
- **Machine target.** The reference boot data is a scraped CoCo 3 GIME init
  sequence, and the firmware carries a TODO about CoCo 1 and 2 support. Our ROM
  mode targets a 32K CoCo, so this needs settling early.
- **Holding HALT.** Whether the CPU can be held halted indefinitely depends on
  the SAM's video fetch refreshing DRAM independently of the CPU. Believed true,
  and it is why bus-mastering cartridges work at all, but not verified here.
- **It is not standalone.** The umbilical requires the host to be attached. That
  is fine for development and is explicitly *not* what #551 is chasing. Nothing
  here should be allowed to soften the standalone goal.
- **Someone else's hardware.** copico-bonobo is Strickland's repo. MIT licensed
  so reuse is fine, but it is upstream, it is outside this project's access
  boundaries, and it will change without notice.

## First slice

**Incremental compilation mode in fc.py.** Refactor `compile_forth` to accept an
incoming `(word_cfa, cursor)` and return updated state plus only the delta
bytes, instead of laying out the whole image from `app_base` every time.

This is the right first move because it is pure host-side work with no hardware
dependency, and it is verifiable entirely in XRoar: compile a base image, then
compile a delta against it, load both, and run the delta. It is also the long
pole. Everything else in this proposal is plumbing by comparison, and #551 wants
the same state model for its symbol sidecars regardless of whether the umbilical
gets built.

File it as its own issue before starting.
