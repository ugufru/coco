# fc.py — Forth Cross-Compiler

`fc.py` compiles Forth source files into CoCo DECB binaries that the kernel
executes. This document explains what it does, how it works, and — crucially
— what it is *not*.

---

## What a traditional Forth compiler is

In a standard Forth system, the compiler is part of the running Forth
environment. You type `: DOUBLE DUP + ;` at a prompt on the target machine,
and Forth:

1. Recognises `:` as an *immediate* word that switches to compile mode
2. Looks up each subsequent word in the **dictionary** (a linked list in RAM)
3. Writes the CFA address of each found word into a new definition
4. Writes a name header so the new word can be looked up later
5. Returns to interpret mode when it sees `;`

The dictionary lives on the target. The compiler lives on the target. The
source text is parsed on the target. The system compiles itself.

This is self-hosted Forth. `fc.py` is not that.

---

## What fc.py is instead

`fc.py` is a **cross-compiler**: it runs on a modern host (Mac, Linux) and
produces a binary for a different target (CoCo 6809). The CoCo never sees
source text. It only ever sees the compiled binary.

```
Forth source (.fs)          running on: Mac / Linux
        │
        │  fc.py
        ▼
DECB binary (.bin)          running on: CoCo 6809 via kernel
```

There is no dictionary on the CoCo. There is no `FIND`. There is no outer
interpreter. Name resolution happens entirely at cross-compile time, on the
host, and the results are baked into the binary as hard addresses.

---

## What the binary actually contains

The output is not 6809 machine code. It is **ITC threaded code**: a sequence
of 16-bit CFA addresses.

A CFA (Code Field Address) is a 2-byte memory location that holds a pointer
to a word's machine code. Every primitive in the kernel has one:

```
$1017 → CFA_EMIT → points to CODE_EMIT (6809 machine code)
$1015 → CFA_LIT  → points to CODE_LIT  (6809 machine code)
$1013 → CFA_EXIT → points to CODE_EXIT (6809 machine code)
```

A compiled word is just a list of these addresses:

```
: HELLO  72 EMIT  69 EMIT  ... ;

compiles to:

DOCOL       ← enters the word
CFA_LIT     ← push literal...
72          ← ...72 ('H')
CFA_EMIT    ← emit it
CFA_LIT
69          ← 'E'
CFA_EMIT
...
CFA_EXIT    ← return
```

The kernel's inner interpreter (NEXT) steps through this list two bytes at a
time, jumping through each CFA to its machine code, then back for the next.

---

## How the kernel CFA addresses get into the binary

The kernel is assembled by `lwasm`, which writes a `.map` file listing every
symbol and its address. `fc.py` reads that file:

```python
symbols = load_symbols('build/kernel.map')
# {'CFA_EMIT': 0x1017, 'CFA_LIT': 0x1015, 'DOCOL': 0x1000, ...}
```

When compiling a reference to `EMIT` in the Forth source, the compiler looks
up `CFA_EMIT` in this dict and writes `$1017` into the binary. The addresses
are never hardcoded in `fc.py` — they come from the map file. If the kernel
changes and symbols shift, recompiling the app picks up the new addresses
automatically.

---

## The compilation pipeline

```
source text
    │
    ├─ tokenize()       line-by-line processing:
    │                     strip comments (\ and parentheses)
    │                     capture CODE...;CODE blocks as raw asm text
    │                     resolve INCLUDE directives recursively
    │
    ├─ parse()          walk tokens → IR
    │                     ('lit', 72)            integer or CHAR literal
    │                     ('word', 'emit')        word reference
    │                     ('do',)                 DO
    │                     ('label', name)          loop label (for DO back-ref)
    │                     ('loop_back', name)      LOOP + branch offset
    │                     ('0branch', offset)      IF / UNTIL branch
    │                     ('branch', offset)       ELSE / AGAIN branch
    │                   VARIABLE declarations collected separately
    │                   CODE definitions collected as raw asm text
    │
    ├─ assemble_code_words()   (only if CODE words exist)
    │                     preprocess: expand ;NEXT, add global labels
    │                     assemble all CODE words via lwasm
    │                     extract each word's machine code bytes
    │
    └─ compile_forth()  two passes
            │
            ├─ Pass 1   calculate addresses
            │             main thread starts at APP_BASE ($2000)
            │             colon definitions follow
            │             CODE definitions follow (CFA + machine code each)
            │             variable CFA+data cells follow those
            │
            └─ Pass 2   emit binary
                          literals    → CFA_LIT + 16-bit value
                          word refs   → 2-byte CFA address
                          definitions → DOCOL + body + CFA_EXIT
                          CODE words  → FDB self+2 + raw machine code
                          variables   → DOVAR + 2-byte data cell (init 0)
                          DO/LOOP     → CFA_DO + label / CFA_LOOP + back-offset
                          IF/ELSE/THEN → CFA_0BRANCH/CFA_BRANCH + forward offset
                          BEGIN/AGAIN → CFA_BRANCH + back-offset
                          BEGIN/UNTIL → CFA_0BRANCH + back-offset
```

The output is wrapped in DECB block headers and written as a `.bin` file.
BASIC's `LOADM` loads it and the kernel's `START` routine jumps to `APP_BASE`.

---

## What fc.py supports

| Feature | Status |
|---|---|
| Colon definitions (`: NAME ... ;`) | done |
| `CODE NAME ... ;CODE` (inline assembly) | done |
| `VARIABLE NAME` | done |
| `N CONSTANT NAME` | done |
| `CHAR X` | done |
| Integer literals (decimal, `0x` hex, `$` hex) | done |
| `DO … LOOP` with `I` | done |
| `IF … THEN`, `IF … ELSE … THEN` | done |
| `BEGIN … AGAIN`, `BEGIN … UNTIL` | done |
| `INCLUDE filename` | done |
| Runtime dictionary | not applicable (cross-compiler) |
| String literals (`S"`, `."`) | not yet |
| Interactive REPL | not applicable (cross-compiler) |

---

## Inline assembly: CODE words

CODE words let `.fs` library files define native 6809 routines inline. The
caller can't tell whether a word is threaded Forth or machine code — it's just
a CFA address in the thread. This keeps the kernel lean and lets apps/libraries
optimize their own hot paths.

### Syntax

```forth
CODE fast-fill  ( addr count byte -- )
    PSHS X
    LDB  1,U
    LDY  2,U
    BEQ  @done
    LDX  4,U
@loop
    STB  ,X+
    LEAY -1,Y
    BNE  @loop
@done
    LEAU 6,U
    PULS X
    ;NEXT
;CODE
```

- `CODE name` opens an assembly block (must be the first token on its line)
- `;CODE` on its own line closes the block
- `;NEXT` expands to `LDY ,X++ / JMP [,Y]` (the kernel's NEXT sequence)
- Local labels use lwasm's `@` prefix — they scope correctly per word
- Assembly follows standard lwasm 6809 syntax
- All kernel symbols from the `.map` file are available as EQUs

### How it works (ITC mechanics)

A CODE word in the binary looks like:

```
addr+0:  FDB  addr+2       (CFA cell — points to machine code)
addr+2:  <machine code>    (ends with NEXT sequence)
```

When the threaded interpreter hits this word, NEXT does `LDY ,X++ / JMP [,Y]`
— loads the CFA address, jumps through it to the machine code. Identical to
how kernel primitives work, just located in the app binary instead of the
kernel.

### Register conventions

CODE words must preserve `X` (IP) and `U` (DSP) across their execution, or
update them deliberately (e.g., `LEAU 2,U` to pop the data stack). `S` is
the return stack, `Y` and `D` are scratch. End every CODE word with `;NEXT`
to return control to the threaded interpreter.

---

## Usage

```sh
python3 fc.py source.fs \
    --kernel    build/kernel.map \
    --kernel-bin build/kernel.bin \
    --output    app.bin
```

| Option | Default | Description |
|---|---|---|
| `--kernel` | `build/kernel.map` | lwasm map file for CFA address lookup |
| `--kernel-bin` | *(none)* | kernel DECB binary to prepend (produces combined binary) |
| `--output` | `<source>.bin` | output binary path |
| `--base` | `0x2000` | application load address |

With `--kernel-bin`, the output contains both the kernel block and the app
block in a single DECB binary. BASIC loads both in one `LOADM` and jumps to
`START`.
