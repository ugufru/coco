# CoCo Forth Executor Kernel

A minimal ITC Forth executor kernel for the TRS-80 Color Computer 2, written
in 6809 assembly. Assembled with `lwasm`, tested under XRoar.

---

## What this is

The kernel is the fixed firmware layer that executes cross-compiled Forth
programs. It implements the inner interpreter and a set of primitives — the
words that must be in machine code because they directly manipulate registers
or hardware. Everything else is written in Forth and cross-compiled by
[`fc.py`](../tools/README.md).

Performance-critical routines that don't need to live in the kernel can be
written as `CODE` words directly in `.fs` library files using inline 6809
assembly. See [`fc.py` documentation](../tools/README.md#inline-assembly-code-words)
for syntax and details.

There is no interactive REPL, no on-device compiler, no dictionary on the
CoCo. The host machine compiles; the CoCo executes.

---

## Threading model: Indirect Threaded Code (ITC)

Every primitive has a **Code Field Address (CFA)** — a 2-byte memory cell
containing a pointer to the word's machine code. A compiled Forth program is
a sequence of CFA addresses called a *thread*. The inner interpreter (`NEXT`)
steps through it:

```asm
LDY ,X++    ; fetch next CFA address from thread, advance IP by 2
JMP [,Y]    ; jump through CFA to machine code
```

### Register assignments

| Register | Role |
|---|---|
| `X` | IP — instruction pointer into the thread |
| `U` | DSP — data stack pointer, grows downward |
| `S` | RSP — return stack pointer (hardware stack), grows downward |
| `Y` | scratch — used by NEXT and all primitives |
| `D` | scratch accumulator (A = high byte, B = low byte) |

The 6809's dual stack pointers (`S` and `U`) map directly onto Forth's return
stack and data stack. This is not a coincidence — the 6809 is one of the few
processors where Forth fits naturally without compromise.

---

## Primitives

### Threading

| Word | Stack | Description |
|---|---|---|
| `DOCOL` | — | Enter a colon definition: push IP to return stack, set IP to word body |
| `DOVAR` | `( -- addr )` | Enter a variable: push address of data cell, NEXT |
| `EXIT` | — | Return from colon definition: pop IP from return stack |
| `LIT` | `( -- n )` | Push the next cell in the thread as a 16-bit literal |

### Stack

| Word | Stack | Description |
|---|---|---|
| `DUP` | `( n -- n n )` | Duplicate top of stack |
| `DROP` | `( n -- )` | Discard top of stack |
| `SWAP` | `( n1 n2 -- n2 n1 )` | Exchange top two items |
| `OVER` | `( n1 n2 -- n1 n2 n1 )` | Copy second item to top |
| `?DUP` | `( x -- x x \| 0 )` | Duplicate only if non-zero |

### Arithmetic

| Word | Stack | Description |
|---|---|---|
| `+` | `( n1 n2 -- sum )` | Add |
| `-` | `( n1 n2 -- diff )` | Subtract (n1 − n2) |
| `*` | `( n1 n2 -- product )` | Multiply |
| `/MOD` | `( n1 n2 -- rem quot )` | Divide: remainder and quotient |
| `NEGATE` | `( n -- -n )` | Two's complement negate |

### Memory

| Word | Stack | Description |
|---|---|---|
| `@` | `( addr -- n )` | Fetch 16-bit value from address |
| `!` | `( n addr -- )` | Store 16-bit value to address |

### I/O

| Word | Stack | Description |
|---|---|---|
| `EMIT` | `( c -- )` | Write character to video RAM at cursor, advance cursor |
| `CR` | `( -- )` | Advance cursor to start of next row |
| `KEY` | `( -- c )` | Wait for a keypress, push ASCII code |

### Control flow

| Word | Stack | Description |
|---|---|---|
| `0BRANCH` | `( flag -- )` | Branch forward by offset if flag is zero; offset follows in thread |
| `BRANCH` | `( -- )` | Unconditional branch; offset follows in thread |
| `DO` | `( limit index -- )` | Begin a counted loop; push limit and index to return stack |
| `LOOP` | — | Increment index; branch back if index < limit |
| `I` | `( -- n )` | Push current loop index |

### Comparison

| Word | Stack | Description |
|---|---|---|
| `=` | `( n1 n2 -- flag )` | True ($FFFF) if equal |
| `<>` | `( n1 n2 -- flag )` | True if not equal |
| `<` | `( n1 n2 -- flag )` | True if n1 < n2 (signed) |
| `>` | `( n1 n2 -- flag )` | True if n1 > n2 (signed) |
| `0=` | `( n -- flag )` | True if zero |

### Screen

| Word | Stack | Description |
|---|---|---|
| `AT` | `( row col -- )` | Set cursor to row (0–15) and column (0–31) |

### System

| Word | Stack | Description |
|---|---|---|
| `HALT` | — | Spin forever (`BRA *`) — end of application |

---

## Memory layout

```
$0050–$0051   VAR_CUR     cursor offset into video RAM (0–511)
$0400–$05FF   video RAM   (32 columns × 16 rows, VDG alphanumeric)
$1000         DOCOL
$1009         DOVAR
$1013–$1060   CFA table   (31 entries × 2 bytes)
$1061–$1432   machine code for all primitives
$1431         CODE_HALT
$1433         START       (DECB exec address)
$1453–$1FFF   unused      (~2.9K available for new primitives)
$2000–$xxxx   application (cross-compiled Forth thread)
$7E00         data stack base  (U, grows down)
$7FFE         return stack init (S, grows down from $7FFE)
```

---

## ASCII → VDG encoding

The CoCo's MC6847 VDG uses a 6-bit character code:

```
screen_byte = $40 | (ascii & $3F)
```

For uppercase A–Z this is a no-op. For space: `$60`. The VDG has no lowercase.

---

## Build

Requires [lwtools](https://www.lwtools.ca/) (`brew install lwtools`).

```sh
make          # assemble kernel → build/kernel.bin + kernel.map
make run      # assemble and launch combined binary in XRoar
make clean    # remove build/
```

The Makefile also cross-compiles `../hello/hello.fs` into a combined binary
for quick testing. To run a different Forth program:

```sh
python3 ../tools/fc.py myapp.fs \
    --kernel     build/kernel.map \
    --kernel-bin build/kernel.bin \
    --output     myapp.bin
xroar -machine coco2bus \
    -bas ~/.xroar/roms/bas12.rom \
    -extbas ~/.xroar/roms/extbas11.rom \
    -run myapp.bin
```

## Testing with XRoar

Requires [XRoar](https://www.6809.org.uk/xroar/) (`brew install xroar`) and
CoCo 2 ROM images in `~/.xroar/roms/`:

- `bas12.rom` — Color BASIC 1.2
- `extbas11.rom` — Extended Color BASIC 1.1
