# CoCo Forth Executor Kernel — Version 1.0

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
| `LIT0` / `LIT1` / `LIT2` | `( -- n )` | Push 0 / 1 / 2.  Compiler-emitted only — `0`, `1`, `2` in source compile to a single 2-byte CFA cell instead of the generic 4-byte `LIT` + value, shaving ~50–300 bytes off every non-trivial app. |

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
| `PICK` | `( xu ... x0 u -- xu ... x0 xu )` | Copy u-th stack item to top (0 = DUP) |

### Arithmetic

| Word | Stack | Description |
|---|---|---|
| `+` | `( n1 n2 -- sum )` | Add |
| `-` | `( n1 n2 -- diff )` | Subtract (n1 − n2) |
| `*` | `( n1 n2 -- product )` | Multiply (16-bit signed, via 6809 MUL) |
| `/MOD` | `( n1 n2 -- rem quot )` | Divide: remainder and quotient |
| `NEGATE` | `( n -- -n )` | Two's complement negate |
| `ABS` | `( n -- |n| )` | Absolute value |
| `MIN` | `( n1 n2 -- n )` | Smaller of two signed values |
| `MAX` | `( n1 n2 -- n )` | Larger of two signed values |
| `0MAX` | `( n -- n' )` | Clamp to non-negative: max(n, 0) |
| `0MIN` | `( n -- n' )` | Clamp to non-positive: min(n, 0) |
| `2*` | `( n -- n*2 )` | Arithmetic shift left by 1 |
| `2/` | `( n -- n/2 )` | Arithmetic shift right by 1 (sign-preserving) |

> **Tip:** `/MOD` gives both remainder and quotient. For just division use `/MOD SWAP DROP`.
> For just modulus use `/MOD DROP`. No separate `/` or `MOD` primitives are needed.

### Logic and bitwise

| Word | Stack | Description |
|---|---|---|
| `AND` | `( n1 n2 -- n3 )` | Bitwise AND |
| `OR` | `( n1 n2 -- n3 )` | Bitwise OR |
| `LSHIFT` | `( n u -- n' )` | Logical shift left by u bits |
| `RSHIFT` | `( n u -- n' )` | Logical shift right by u bits |
| `INVERT` | `( n -- ~n )` | Bitwise complement (one's complement) |
| `XOR` | `( n1 n2 -- n3 )` | Bitwise exclusive OR |

### Comparison

| Word | Stack | Description |
|---|---|---|
| `=` | `( n1 n2 -- flag )` | True ($FFFF) if equal |
| `<>` | `( n1 n2 -- flag )` | True if not equal |
| `<` | `( n1 n2 -- flag )` | True if n1 < n2 (signed) |
| `>` | `( n1 n2 -- flag )` | True if n1 > n2 (signed) |
| `0=` | `( n -- flag )` | True if zero |
| `U<` | `( u1 u2 -- flag )` | True if u1 < u2 (unsigned) |
| `WITHIN` | `( n lo hi -- flag )` | True if lo ≤ n < hi |

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
| `TYPE` | `( addr len -- )` | Output len characters starting at addr |
| `COUNT` | `( c-addr -- addr+1 len )` | Convert counted string to addr+len |

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
| `J` | `( -- n )` | Push outer loop index (in nested DO...LOOP) |
| `+LOOP` | `( n -- )` | Add n to index; branch back if limit not crossed |
| `UNLOOP` | `( -- ) R:( limit index -- )` | Discard loop parameters from return stack |

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
| `MDIST` | `( addr1 addr2 -- d )` | Manhattan distance between two (x,y) coordinate pairs |

### Data

| Word | Stack | Description |
|---|---|---|
| `sprite-data` | `( -- addr )` | Address of kernel sprite FCB data (5 sprites × 12 bytes) |
| `font-data` | `( -- addr )` | Address of kernel font FCB data (59 glyphs × 8 bytes) |

### Screen sync

| Word | Stack | Description |
|---|---|---|
| `VSYNC` | `( -- )` | Wait for vertical sync (60 Hz NTSC) |
| `WAIT-PAST-ROW` | `( row -- )` | After VSYNC, wait for beam past display row (0-191) |
| `COUNT-BLANKING` | `( -- n )` | Diagnostic: count 100 HSYNC pulses after VSYNC |

### RG6 graphics

| Word | Stack | Description |
|---|---|---|
| `RG-PSET` | `( x y color -- )` | Plot one artifact pixel (color 0-3) |
| `RG-LINE` | `( x1 y1 x2 y2 color -- )` | Bresenham line, all 8 octants |
| `RG-CHAR` | `( char cx cy -- )` | Render font glyph at text position |
| `SPR-DRAW` | `( addr x y -- )` | Draw sprite with transparency |
| `SPR-ERASE-BOX` | `( addr x y -- )` | Clear sprite bounding box to black |

### Beam system

| Word | Stack | Description |
|---|---|---|
| `BEAM-TRACE` | `( x1 y1 x2 y2 buf -- count )` | Bresenham trace with pixel save to buffer |
| `BEAM-DRAW-SLICE` | `( buf start count color -- )` | Draw buffer slice in given color |
| `BEAM-RESTORE-SLICE` | `( buf start count -- )` | Restore saved colors from buffer |
| `BEAM-FIND-OBSTACLE` | `( buf count -- index )` | Find first non-black saved pixel |
| `BEAM-SCRUB-POS` | `( buf count cx cy -- )` | Zero saved colors within ±4x, ±3y of position |

### System

| Word | Stack | Description |
|---|---|---|
| `HALT` | — | Spin forever (`BRA *`) — end of application |

---

## Boot sequence

The kernel is assembled at `$E000` but BASIC's `CLOADM` can't write there
(ROM is mapped at `$8000–$FEFF`).  A bootstrap solves this:

1. `fc.py` remaps the kernel DECB record from `$E000` to the staging area
   (immediately after the bootstrap, typically `$0E19`).
2. `CLOADM`/`LOADM` loads three records into low RAM (`$0000–$7FFF`).
3. The bootstrap at `$0E00` enables all-RAM mode (`STA $FFDF`) and copies
   the staged kernel to `$E000`.
4. `JMP START` enters the kernel at its final location.

**[Open the interactive boot animation](boot-animation.html)** for a visual
step-by-step walkthrough.

### DECB record layout

| Record | Addr | Size | Content |
|---|---|---|---|
| 1 | `$0E00` | ~25 B | Bootstrap |
| 2 | `$0E19` | ~3.9K | Staged kernel + variables (remapped from `$E000`) |
| 3 | `$2000` | varies | Application code |
| Exec | `$0E00` | — | Bootstrap entry point |

All DECB records target the lower 32K (`$0000–$7FFF`). Kernel variables live
inside the kernel (at `$EF03+`), not in low RAM, so binaries are LOADM-safe
under Disk BASIC.

**Staging area:** The kernel is staged at `$0E19` (right after the bootstrap)
and extends to just below `APP_BASE`. `fc.py` dynamically computes the staging
address and patches the bootstrap accordingly.

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

### Kernel variables ($EF03+)

Scratch variables in kernel space, accessed via extended addressing. Copied to
all-RAM with the kernel by the bootstrap. Forth source accesses them via
`KVAR-*` constants injected by `fc.py` from the kernel map.

```
$EF03  VAR_CUR          cursor offset into video RAM (0–511)
$EF05  VAR_KEY_PREV     last accepted key ASCII (debounce)
       ...              (keyboard, line, sprite, text, beam scratch — 50 bytes total)
$EF34  VAR_BEAM_CNT     last variable
$EF35  KERN_END         end of kernel (bootstrap copy range)
```

Addresses are resolved from `kernel.map` — do not hardcode them in Forth
source. Use `KVAR-CUR`, `KVAR-RGVRAM`, `KVAR-RGFONT`, etc.

### Full address space

```
$0400–$05FF   VDG text VRAM (32×16, boot only)
$0600–$1FFF   RG6 VRAM (6144 bytes, set by rg-init after boot)
              ├ $0E00  bootstrap (dead after boot, overwritten by VRAM)
              └ $0E19  staged kernel (dead after boot, overwritten by VRAM)
$1F00–$7FFF   application code (APP_BASE configurable via --base)
$8000–$DDFF   runtime RAM (24K — variables, tables, buffers)
$DE00         data stack base (U, grows downward)
$E000–$E012   DOCOL, DOVAR entry points (final location)
$E013–$E0xx   CFA table (83 entries × 2 bytes, includes DOVAR data blocks)
$E0xx–$E829   primitive machine code + font/sprite FCB data + key table
$E82A–$E869   START: hardware init + app entry
$E86A–$EF02   promoted library primitives (graphics, sprites, beams)
$EF03–$EF34   kernel variables (50 bytes, see above)
$EF35         KERN_END (end of bootstrap copy range)
$EF35–$FEFF   free RAM for kernel growth (~4.3K)
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
| App (loadable) | ~23K | Forth thread + CODE words (`$2000–$7FFF`) |
| Runtime RAM | 24K | variables, tables, buffers (`$8000–$DDFF`; not CLOADM-loadable) |
| Data stack | 512B | grows down from `$DE00` |
| Return stack | 512B | grows down from `$E000` (below kernel) |
| Kernel code | ~3.7K | 81 primitives (incl. LIT0/LIT1/LIT2 small-int compression) + graphics/beam/sprite + font/sprite data + startup (`$E000–$EE30`) |
| Post-kernel | ~4.3K | free at final location (`$EE30–$FEFF`), but **staging limit is 4K** (see below) |

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

## 6809 / lwasm Gotchas for CODE Words

These have caused real bugs in this project:

- **Pre-decrement by 1 is illegal for stores**: `STA ,-S` and `CLR ,-S` are undefined on the 6809. Use `PSHS A` instead.
- **D register byte order**: For 8-bit values used with 16-bit ops (ADDD, STD), the value goes in B (low byte). Always `CLRA` + `LDB value` before `ADDD`.
- **No direct register-to-register compare**: `CMPA B` is not a valid instruction. Push one register first: `PSHS B` + `CMPA ,S+`.
- **Don't load D before a conditional branch**: `LDD #0` clobbers the N/Z/V flags set by the preceding CMPD/SUBD. Branch on flags first, then load the result.
- **Pre-decrement size**: `,-U` (postbyte $C2) is a 1-byte pre-decrement — wrong for 16-bit stack push. Use `,--U` (postbyte $C3) for 2-byte pre-decrement: `STD ,--U`.
- **Readable stack pop**: Use `LDD ,U` + `LEAU 2,U` instead of `LDD ,U++` for explicit, readable data stack pops.

## Testing with XRoar

Requires [XRoar](https://www.6809.org.uk/xroar/) (`brew install xroar`) and
CoCo 2 ROM images in `~/.xroar/roms/`:

- `bas12.rom` — Color BASIC 1.2
- `extbas11.rom` — Extended Color BASIC 1.1
