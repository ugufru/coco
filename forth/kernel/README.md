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
| `2DUP` | `( n1 n2 -- n1 n2 n1 n2 )` | Duplicate top pair |
| `2DROP` | `( n1 n2 -- )` | Discard top pair |
| `ROT` | `( n1 n2 n3 -- n2 n3 n1 )` | Rotate third item to top |

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
| `+!` | `( n addr -- )` | Add n to the cell at addr |

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

### Return stack

| Word | Stack | Description |
|---|---|---|
| `>R` | `( n -- ) R:( -- n )` | Move top of data stack to return stack |
| `R>` | `( -- n ) R:( n -- )` | Move top of return stack to data stack |
| `R@` | `( -- n ) R:( n -- n )` | Copy top of return stack to data stack |

### Spatial

| Word | Stack | Description |
|---|---|---|
| `PROX-SCAN` | `( cx cy radius array count -- bitmask )` | Proximity scan: test each (x,y) pair in array against center, return bitmask of hits within radius |

### Data

| Word | Stack | Description |
|---|---|---|
| `sprite-data` | `( -- addr )` | Address of kernel sprite FCB data (5 sprites × 12 bytes) |
| `font-data` | `( -- addr )` | Address of kernel font FCB data (59 glyphs × 8 bytes) |

### System

| Word | Stack | Description |
|---|---|---|
| `HALT` | — | Spin forever (`BRA *`) — end of application |

---

## Boot sequence

The kernel is assembled at `$E000` but BASIC's `CLOADM` can't write there
(ROM is mapped at `$8000–$FEFF`).  A bootstrap solves this:

1. `fc.py` remaps the kernel DECB record from `$E000` to `$1000` (staging).
2. `CLOADM` loads four records into low RAM (`$0000–$7FFF`).
3. The bootstrap at `$0E00` enables all-RAM mode (`STA $FFDF`) and copies
   `$1000` → `$E000`.
4. `JMP START` enters the kernel at its final location.

**[Open the interactive boot animation](boot-animation.html)** for a visual
step-by-step walkthrough.

### DECB record layout

| Record | Addr | Size | Content |
|---|---|---|---|
| 1 | `$0050` | 44 B | Kernel variables |
| 2 | `$0E00` | ~25 B | Bootstrap |
| 3 | `$1000` | ~1.9K | Staged kernel (remapped from `$E000`) |
| 4 | `$2000` | ~19.3K | Application (contiguous, varies by app) |
| Exec | `$0E00` | — | Bootstrap entry point |

All DECB records must target the lower 32K (`$0000–$7FFF`) because CLOADM
runs with ROM still mapped at `$8000–$FEFF`.

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
$0400–$05FF   VDG text VRAM (32×16, boot only)
$0600–$1FFF   RG6 VRAM (6144 bytes, set by rg-init after boot)
              ├ $0E00  bootstrap (dead after boot, overwritten by VRAM)
              └ $1000  staged kernel (dead after boot, overwritten by VRAM)
$2000–$7FFF   application code (contiguous, ~24K loadable via CLOADM)
$8000–$DDFF   runtime RAM (24K — variables, tables, buffers; NOT CLOADM-loadable)
$DE00         data stack base (U, grows downward)
$E000–$E012   DOCOL, DOVAR entry points (final location)
$E013–$E0B4   CFA table (51 entries × 2 bytes, includes DOVAR data blocks)
$E0B5–$E761   primitive machine code + font/sprite FCB data + key table
$E762–$E7A0   START: hardware init + app entry
$E7A1         KERN_END (end of bootstrap copy range)
$E7A1–$FEFF   free RAM for kernel growth / static data (~6.1K)
$FF00–$FFFF   I/O registers + hardware vectors (always mapped, never RAM)
```

All-RAM mode is enabled at boot (`STA $FFDF`), giving full 64K RAM from
`$0000–$FEFF`.  `$FF00–$FFFF` is always I/O regardless of mode.

**CLOADM constraint:** BASIC runs with ROM at `$8000–$FEFF`, so CLOADM can
only load data to the lower 32K.  The `$8000–$DDFF` region is writable at
runtime (after the bootstrap enables all-RAM) but cannot hold loaded code.

### Memory budget

| Region | Size | Contents |
|---|---|---|
| VRAM | 6K | RG6 display (`$0600–$1FFF`, set by rg-init) |
| App (loadable) | ~24K | Forth thread + CODE words (`$2000–$7FFF`) |
| Runtime RAM | 24K | variables, tables, buffers (`$8000–$DDFF`; not CLOADM-loadable) |
| Data stack | 512B | grows down from `$DE00` |
| Return stack | 512B | grows down from `$E000` (below kernel) |
| Kernel code | ~1.9K | primitives, CFA table, font/sprite data, keyboard matrix, startup (`$E000–$E7A0`) |
| Post-kernel | ~6.1K | free for kernel growth / static data (`$E7A1–$FEFF`) |

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
