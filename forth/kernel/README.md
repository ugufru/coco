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
| `*` | `( n1 n2 -- product )` | Multiply (16-bit signed, via 6809 MUL) |
| `/MOD` | `( n1 n2 -- rem quot )` | Divide: remainder and quotient |
| `NEGATE` | `( n -- -n )` | Two's complement negate |

### Logic and bitwise

| Word | Stack | Description |
|---|---|---|
| `AND` | `( n1 n2 -- n3 )` | Bitwise AND |
| `OR` | `( n1 n2 -- n3 )` | Bitwise OR |
| `LSHIFT` | `( n u -- n' )` | Logical shift left by u bits |
| `RSHIFT` | `( n u -- n' )` | Logical shift right by u bits |

### Comparison

| Word | Stack | Description |
|---|---|---|
| `=` | `( n1 n2 -- flag )` | True ($FFFF) if equal |
| `<>` | `( n1 n2 -- flag )` | True if not equal |
| `<` | `( n1 n2 -- flag )` | True if n1 < n2 (signed) |
| `>` | `( n1 n2 -- flag )` | True if n1 > n2 (signed) |
| `0=` | `( n -- flag )` | True if zero |

### Memory

| Word | Stack | Description |
|---|---|---|
| `@` | `( addr -- n )` | Fetch 16-bit value from address |
| `!` | `( n addr -- )` | Store 16-bit value to address |
| `C@` | `( addr -- c )` | Fetch byte from address (zero-extended to 16 bits) |
| `C!` | `( c addr -- )` | Store low byte to address |
| `FILL` | `( addr u c -- )` | Fill u bytes at addr with byte c |
| `CMOVE` | `( src dst u -- )` | Copy u bytes from src to dst |

### I/O and keyboard

| Word | Stack | Description |
|---|---|---|
| `EMIT` | `( c -- )` | Write character to video RAM at cursor, advance cursor |
| `CR` | `( -- )` | Advance cursor to start of next row |
| `KEY` | `( -- c )` | Wait for a keypress, push ASCII code (blocking) |
| `KEY?` | `( -- c\|0 )` | Non-blocking key scan: ASCII code or 0 if no key held |
| `KBD-SCAN` | `( col -- row )` | Raw PIA0 keyboard matrix scan |

### Screen

| Word | Stack | Description |
|---|---|---|
| `AT` | `( row col -- )` | Set cursor to row (0–15) and column (0–31) |

### Control flow

| Word | Stack | Description |
|---|---|---|
| `0BRANCH` | `( flag -- )` | Branch by offset if flag is zero |
| `BRANCH` | `( -- )` | Unconditional branch by offset |
| `DO` | `( limit index -- )` | Begin a counted loop; push limit and index to return stack |
| `LOOP` | — | Increment index; branch back if index < limit |
| `I` | `( -- n )` | Push current loop index |

### System

| Word | Stack | Description |
|---|---|---|
| `HALT` | — | Spin forever (`BRA *`) — end of application |

---

## Boot sequence

The kernel is assembled at `$8000` but BASIC's `CLOADM` can't write there
(ROM is mapped at `$8000–$FEFF`).  A bootstrap solves this:

1. `fc.py` remaps the kernel DECB record from `$8000` to `$1000` (staging).
2. `CLOADM` loads five records into low RAM.
3. The bootstrap at `$0E00` enables all-RAM mode (`STA $FFDF`) and copies
   `$1000` → `$8000`.
4. `JMP START` enters the kernel at its final location.

**[Open the interactive boot animation](boot-animation.html)** for a visual
step-by-step walkthrough.

### DECB record layout

| Record | Addr | Size | Content |
|---|---|---|---|
| 1 | `$0050` | 44 B | Kernel variables |
| 2 | `$0E00` | ~25 B | Bootstrap |
| 3 | `$1000` | ~1.1K | Staged kernel (remapped from `$8000`) |
| 4 | `$2000` | 8K | App part 1 (before VRAM hole) |
| 5 | `$5800` | ~6K | App part 2 (after VRAM hole) |
| Exec | `$0E00` | — | Bootstrap entry point |

### SAM all-RAM mode

The MC6883 SAM uses address-decoded write-only register pairs
(even = clear, odd = set):

| Write to | Effect |
|---|---|
| `$FFDE` | TY=0 — normal (ROM at `$8000–$FEFF`) |
| `$FFDF` | TY=1 — all-RAM (`$8000–$FEFF` = writable RAM) |

The data written is irrelevant — only the address matters.
Requires 64K×1 DRAM (4164 chips).  XRoar: use `-ram 64`.

---

## Memory map

### Kernel variables ($0050–$007B)

Scratch variables in low RAM, accessed via extended addressing. Zero ROM cost.

```
$0050  VAR_CUR          cursor offset into video RAM (0–511)
$0052  VAR_KEY_PREV     last accepted key ASCII (debounce)
$0053  VAR_KEY_SHIFT    SHIFT flag
$0054  VAR_KEY_RELCNT   release debounce counter
$0055  VAR_KEY_REPDLY   auto-repeat countdown (16-bit)
$0057  VAR_RGVRAM       RG6 VRAM base address (set by rg-init)

$0059  VAR_LINE_*       Bresenham line scratch (13 bytes, used by rg-line CODE word)
$0066  VAR_SPR_*        sprite drawing scratch (15 bytes, used by spr-draw/spr-erase-box CODE words)
$0075  VAR_RG*          text rendering config (7 bytes, used by rg-char CODE word)
```

### Full address space

```
$0000–$004F   direct page (reserved)
$0050–$007B   kernel scratch variables (see above)
$0400–$05FF   VDG text VRAM (32×16, Alpha mode only)
$0E00–$0E18   bootstrap (copies staged kernel, enables all-RAM)
$1000–$1471   staged kernel (DECB load addr; copied to $8000 at boot)
$2000–$3FFF   application code part 1 (8K, before VRAM hole)
$4000–$57FF   RG6 VRAM (6144 bytes, hole in app binary)
$5800–$73FF   application code part 2 (continues after hole)
$7400–$75D8   font data (font-art.fs, ~472 bytes)
$7600–$7774   game data: galaxy, sprites, AI (Space Warp)
$7C00–$7CEF   sine table + pixel lookup tables
$7E00         data stack base (U, grows downward)
$8000–$8012   DOCOL, DOVAR entry points (final location)
$8013–$8060   CFA table (39 entries × 2 bytes)
$8061–$8432   primitive machine code + MATRIX2ASCII table
$8433–$8471   START: hardware init + app entry
$8472         KERN_END (end of bootstrap copy range)
$8472–$FEFF   free RAM (available for future kernel growth / static data)
$FF00–$FFFF   I/O registers + hardware vectors (always mapped, never RAM)
```

All-RAM mode is enabled at boot (`STA $FFDF`), giving full 64K RAM from
`$0000–$FEFF`.  `$FF00–$FFFF` is always I/O regardless of mode.

### Memory budget

| Region | Size | Contents |
|---|---|---|
| Kernel code | ~1.1K | primitives, CFA table, keyboard matrix, startup (`$8000–$8471`) |
| Kernel growth | ~31K | free RAM above kernel (`$8472–$FEFF`) |
| App space | ~15K | Forth thread + CODE words (`$2000–$3FFF` + `$5800–$73FF`) |
| VRAM | 6K | RG6 display (`$4000–$57FF`, hole in app binary) |
| Font data | ~472B | artifact-safe glyphs (`$7400–$75D8`) |
| Game data | ~384B | galaxy, sprites, AI (`$7600–$7774`) |
| Tables | ~240B | sine, pixel LUTs (`$7C00–$7CEF`) |
| Data stack | 512B | grows down from `$7E00` |
| Return stack | 512B | grows down from `$8000` (below kernel) |

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
xroar -machine coco2bus -ram 64 \
    -bas ~/.xroar/roms/bas12.rom \
    -extbas ~/.xroar/roms/extbas11.rom \
    -run myapp.bin
```

The `-ram 64` flag is required — the kernel uses all-RAM mode to run from
`$8000`.

## Testing with XRoar

Requires [XRoar](https://www.6809.org.uk/xroar/) (`brew install xroar`) and
CoCo 2 ROM images in `~/.xroar/roms/`:

- `bas12.rom` — Color BASIC 1.2
- `extbas11.rom` — Extended Color BASIC 1.1
