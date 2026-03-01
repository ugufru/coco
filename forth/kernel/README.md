# CoCo Forth Executor Kernel

A minimal Forth executor kernel for the TRS-80 Color Computer 2, written in
6809 assembly. Builds on macOS with `lwasm` and runs under XRoar.

## What this is

A proof-of-concept for a Forth-based software stack on the CoCo. The kernel
implements the bare minimum needed to execute pre-compiled (cross-compiled)
Forth programs sent to the machine — no interactive REPL, no on-device
compiler. The modern machine does the compilation; the CoCo just runs the
result.

The current prototype has a Hello World application hardcoded. Serial loading
comes next.

## Threading model: Indirect Threaded Code (ITC)

Every Forth word has a **Code Field Address (CFA)** — a 2-byte memory location
containing a pointer to the word's machine code. A compiled Forth program is a
sequence of CFA addresses called a *thread*. The inner interpreter (`NEXT`)
steps through it:

```asm
LDY ,X++    ; fetch next CFA address from thread, advance IP
JMP [,Y]    ; jump through CFA to machine code
```

### Register assignments

| Register | Role |
|---|---|
| `X` | IP — instruction pointer into the thread |
| `U` | DSP — data stack pointer, grows downward |
| `S` | RSP — return stack pointer (hardware stack), grows downward |
| `Y` | scratch — used by NEXT and primitives |
| `D` | scratch accumulator |

The 6809's dual stack pointers (`S` and `U`) map directly onto Forth's
return stack and data stack. This is not a coincidence — the 6809 is one of
the few processors where Forth fits naturally without compromise.

## Primitives

Four words implement the entire kernel:

**`DOCOL`** — enter a colon definition. Saves the current IP on the return
stack, sets IP to the first cell of the word's body.

**`EXIT`** — return from a colon definition. Restores IP from the return stack.

**`LIT`** — read the next cell in the thread as a 16-bit literal, push it onto
the data stack.

**`EMIT`** — pop a character from the data stack, convert ASCII to VDG
encoding, write it directly to video RAM at `$0400`.

### ASCII → VDG character encoding

The CoCo's MC6847 VDG uses a 6-bit character code with a normal-video bit:

```
screen_byte = $40 | (ascii & $3F)
```

For uppercase A–Z this is a no-op (the ASCII byte is already correct). For
space: `$60`. The VDG has no lowercase — everything displays uppercase.

## Memory layout

```
$0050–$0051   VAR_CUR   cursor position (offset into video RAM, 0–511)
$0400–$05FF   video RAM (32 columns × 16 rows)
$1000–$10C0   kernel code and compiled Hello World word
$7E00–$7DFF   data stack (grows down)
$7FFE–$8000   return stack (grows down)
```

## Hello World word

The compiled `HELLO` word is pure data — a sequence of CFA addresses:

```asm
CFA_HELLO:  FDB  DOCOL
            FDB  CFA_LIT
            FDB  72           ; 'H'
            FDB  CFA_EMIT
            FDB  CFA_LIT
            FDB  69           ; 'E'
            FDB  CFA_EMIT
            ; ... and so on for each character
            FDB  CFA_EXIT
```

There is no interpreter or compiler on the device. `HELLO` is the output of
what a cross-compiler would produce from `: HELLO ." HELLO, WORLD!" ;`.

## Build

Requires [lwtools](https://www.lwtools.ca/) (`brew install lwtools`).

```sh
make          # assemble → build/kernel.bin (DECB format, ~200 bytes)
make run      # assemble and launch in XRoar
make clean    # remove build/
```

## Testing with XRoar

Requires [XRoar](https://www.6809.org.uk/xroar/) (`brew install xroar`) and
CoCo 2 ROM images in `~/.xroar/roms/`:

- `bas12.rom` — Color BASIC 1.2
- `extbas11.rom` — Extended Color BASIC 1.1

`make run` boots XRoar with a CoCo 2B (NTSC), loads the DECB binary via
BASIC's cassette loader, and executes it at `$109C`. The screen clears and
`HELLO, WORLD!` appears in the top-left corner.

## What's next

1. `CR` word — move cursor to the start of the next row
2. Python cross-compiler — takes Forth source on the Mac, produces a binary
   thread that references the kernel's fixed CFA addresses
3. Serial receiver — bit-banged via PIA U4 (`$FF20`/`$FF22`), loads a
   compiled Forth binary sent from the Mac over serial
