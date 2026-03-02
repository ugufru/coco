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
    ├─ tokenize()       strip comments, split on whitespace
    │
    ├─ parse()          walk tokens → IR
    │                     ('lit', 72)       integer or CHAR literal
    │                     ('word', 'emit')  word reference
    │                   VARIABLE declarations collected separately
    │
    └─ compile_forth()  two passes
            │
            ├─ Pass 1   calculate addresses
            │             main thread starts at APP_BASE ($2000)
            │             colon definitions follow
            │             variable CFA+data cells follow those
            │
            └─ Pass 2   emit binary
                          literals    → CFA_LIT + 16-bit value
                          word refs   → 2-byte CFA address
                          definitions → DOCOL + body + CFA_EXIT
                          variables   → DOVAR + 2-byte data cell (init 0)
```

The output is wrapped in DECB block headers and written as a `.bin` file.
BASIC's `CLOADM` loads it and the kernel's `START` routine jumps to `APP_BASE`.

---

## What fc.py does not do

| Feature | Standard Forth | fc.py |
|---|---|---|
| Runs on target | yes | no (runs on host) |
| Runtime dictionary | yes | no |
| Name lookup at runtime | yes | no (compile-time only) |
| Self-hosting | yes | no (written in Python) |
| `IF` / `THEN` / `LOOP` | yes | not yet |
| Interactive REPL | yes | no |
| Source on device | yes | no |

fc.py handles: `: NAME ... ;`, `VARIABLE NAME`, `CHAR X`, integer literals,
and references to defined words and kernel primitives. That's enough to write
real programs. The control structures (`IF`, `THEN`, `DO`, `LOOP`) come next
— they require emitting `BRANCH`/`0BRANCH` addresses with forward-reference
patching, which is straightforward to add.

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
block in a single DECB binary. BASIC loads both in one `CLOADM` and jumps to
`START`.
