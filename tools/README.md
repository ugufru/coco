# `tools/` — Build & Development Scripts

| Script | Role |
|--------|------|
| [`fc.py`](#fcpy--forth-cross-compiler) | Forth cross-compiler (`.fs` source → DECB binary) — the main toolchain piece |
| [`gen-lib-readme.py`](#gen-lib-readmepy--lib-word-index-generator) | Regenerates `lib/README.md` from `lib/*.fs` headers |
| [`cocoterm_bridge.py`](#cocoterm_bridgepy-coco-terminal-bridge-to-claude-code) | Bridges the CoCo terminal (`src/terminal`) to Claude Code over XRoar's Becker port |
| [`cocoterm_mcp.py`](#cocoterm_mcppy-peek-and-poke-tools-for-live-coco-memory) | MCP server giving Claude `peek` and `poke` tools for live CoCo memory |

The bulk of this README documents `fc.py`. `gen-lib-readme.py` and the
two terminal tools are covered in shorter sections at the end.

These scripts use only the Python 3 standard library, with no third-party
dependencies. The project as a whole depends only on Python 3 (host)
and `lwasm` from [lwtools](https://www.lwtools.ca/) (assembler).

---

# `fc.py` — Forth Cross-Compiler

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
| `CODE NAME ... ;CODE` (app-space inline assembly) | done |
| `KCODE NAME ... ;KCODE` (kernel-space inline assembly) | done |
| `VARIABLE NAME` | done |
| `N CONSTANT NAME` | done |
| `+FIELD`, `CFIELD:`, `FIELD:` (struct definers) | done |
| `DATA[PY name … ]DATA` (compile-time Python data) | done |
| `CHAR X` | done |
| Integer literals (decimal, `0x` hex, `$` hex) | done |
| `DO … LOOP` / `DO … +LOOP` with `I` and `J` | done |
| `IF … THEN`, `IF … ELSE … THEN` | done |
| `BEGIN … AGAIN`, `BEGIN … UNTIL` | done |
| `EXIT` (auto-emits one `UNLOOP` per active `DO` before exiting) | done |
| `INCLUDE filename` | done |
| String literals (`S"`, `."`) | done |
| Runtime dictionary | not applicable (cross-compiler) |
| Interactive REPL | not applicable (cross-compiler) |
| `BEGIN … WHILE … REPEAT` | not implemented |

### Names: one word per name, for the whole program

Names are case-insensitive, and every reference is resolved against the
whole program once parsing ends, not in definition order. So a name
defined twice gives one word for every reference, earlier ones and
library ones included, and a definition named like a kernel primitive
replaces the primitive everywhere (the jukebox's `pick` once broke
`lib/decb.fs`'s `2 PICK` that way, #588). fc.py warns about both and
names the sites (#592), for example:

```
fc.py: warning: game.fs:40: colon definition 'pick' shadows the kernel primitive PICK everywhere it is used
fc.py: warning: game.fs:12: VARIABLE 'score' is already defined at game.fs:3; only one of them is used, for every reference
```

They are warnings, not errors, because some shadows are on purpose:
`lib/rg-text.fs` and `src/bounce` replace `rg-char`, and tutorial 9
builds its own `NEGATE` and `?DUP`. A file INCLUDEd twice does not warn.

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

### KCODE — kernel-space CODE words

`KCODE NAME ... ;KCODE` is the same syntax as `CODE`, but the resulting
machine code is assembled into **kernel space** (alongside the kernel's
own primitives) rather than into the app binary. The CFA reference inside
the application's threaded code is the same — calling a KCODE word is
indistinguishable from calling a CODE word — but the body lives at a
kernel-area address.

Use KCODE when:

- App-binary size is the binding constraint and there's slack in the
  kernel address space (the kernel's `--stage-base` pin can lock the
  staging address so kernel growth doesn't move the app's records).
- A routine is shared across multiple apps and lives more naturally in
  kernel-resident memory than copied into every app.

KCODE is otherwise identical to CODE — same register conventions,
same `;NEXT` requirement.

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
| `--base` | from kernel map (`APP_BASE` symbol) | application load address |
| `--hole` | *(none)* | reserved region (`addr,size`) — app binary skips this range |
| `--stage-base` | auto | pin all-RAM kernel staging address |

With `--kernel-bin`, the output contains the kernel and the app in a single
DECB binary. fc.py auto-detects which kernel profile is in use:

- **ROM mode** (no kernel records ≥ `$E000`): kernel loads at its assembled
  address (`$2000`), app loads at `APP_BASE` (`$3000`), DECB exec is `START`
  directly. No staging copy. BASIC ROMs stay alive on a 32K machine.
- **All-RAM mode** (kernel records at `$E000+`): kernel records get remapped
  to a staging area (default just past the bootstrap at `$0E00`), app at
  `$2000`, DECB exec is the bootstrap. Bootstrap enables all-RAM and
  copies the kernel to its final `$E000` location. Requires 64K.

fc.py exposes several kernel build constants as Forth constants in source:
`font-base`, `vram-base`, `app-base`, `trig-base`. These vary per profile
(see `kernel/README.md`) so apps can be profile-agnostic.

---

# `gen-lib-readme.py` — Lib Word Index Generator

Walks every `lib/*.fs` file and regenerates `lib/README.md` — a single
combined index with one section per library, listing description,
`Provides:` / `Requires:` lines, and a table of public words with stack
effects. Run it manually with `python3 tools/gen-lib-readme.py`, or as
part of `make lib-readme` / `make all` (the root Makefile wires it in
so the index stays current as `lib/*.fs` changes).

**What it parses:**

- Leading `\` comment block — the short description (with any `filename.fs —`
  prefix stripped), and `Provides:` / `Requires:` lines (multi-line continuations
  are joined with spaces).
- Word definitions — `:`, `CODE`, `KCODE`, `VARIABLE` at start of line, plus
  Forth's postfix `<value> CONSTANT <name>` form anywhere on a line.
- Stack effects — same-line `( ... -- ... )` for colon defs, plus a fallback
  search that handles:
  - preceding `\` comments naming the word, including divider-decorated
    forms like `\ ── name ( a -- b ) ───`
  - `;;;`-style stack comments inside the body of a CODE/KCODE word
  - same-line `\ ( a -- b )` trailing comments after `CODE` / `KCODE`
- Forth-style word-name boundaries — names ending in `?`, `!`, `/` etc. work
  (Python's `\b` doesn't, so the script uses explicit name-char lookarounds).

**What it filters:** any word whose name starts with `_` is treated as
private and omitted. The `_`-prefix convention is the project's signal
for "internal helper, not part of the public API." Use it for scratch
variables, intra-file utility words, internal lookup-table addresses.

**What it does *not* do:**

- It doesn't reformat or modify any `.fs` source. Source-side cleanup
  (adding stack effects, fixing `Provides:` lists) happens by hand and
  the generator just reads what's there.
- It doesn't validate that `Provides:` matches the actual word list.
  If they drift, you'll see the table and the `Provides:` line side by
  side and notice the mismatch — but the generator won't yell about it.
- It doesn't handle `IMMEDIATE`, vocabularies, or runtime dictionary
  features (none of which fc.py supports anyway).

**Output is deterministic.** Repeated runs over unchanged sources
produce byte-identical `lib/README.md`. CI / `make` can rely on this.

---

# `cocoterm_bridge.py`: CoCo Terminal Bridge to Claude Code

Connects the CoCo terminal (`src/terminal/terminal.fs`) running in XRoar to
Claude Code on the host. The CoCo side is a dumb terminal; this script owns
the line, runs Claude, and sends back text the 32x16 VDG screen can show.
User-facing setup is in `src/terminal/README.md`.

**Connection.** XRoar's Becker port (`-cart rsdos -cart-becker`) is a TCP
*client* that connects once, when the cartridge is created, to
`127.0.0.1:65504`. The bridge is the server, so start it first. It serves one
client at a time and goes back to listening when XRoar disconnects.

**Line discipline.** Bytes from the CoCo are keystrokes:

| Byte | Action |
|---|---|
| `$20`-`$7E` | Append to the line and echo |
| `$08` | Remove the last character and echo `$08` |
| `$0D` | Submit the line as one Claude turn |
| `$03` (BREAK) | Kill the running turn, or clear the line; send `^C` and a prompt |
| `$0C` (CLEAR) | Clear the screen and reprompt |

Typing is ignored while a turn is running, except BREAK.

**Turns.** Each line runs
`claude -p <line> --output-format stream-json --verbose --include-partial-messages`
in `--cwd`, with `--permission-mode` (default `plan`), an appended system
prompt asking for short plain-ASCII answers, and `--resume <session_id>` after
the first turn. The id comes from the stream, not `--continue`, which would
pick up whatever conversation was most recent in that directory. Events are
handled as follows:

- `text_delta` events stream through the reflow as they arrive.
- `tool_use` blocks print a one-line `* Tool arg` note, except the `peek` and
  `poke` memory tools, which print nothing so drawings are not scrolled away.
- The `result` event ends the turn. On an error, the reason comes from the
  result's `result` text. Its `subtype` can still say `success` when the API
  rejected the request.

**Reflow and sanitizer (`Reflow`).** A streaming word-wrapper whose output is
the same however the input is chunked. It maps common Unicode punctuation to
ASCII (dashes, smart quotes, ellipsis, arrows), folds accents, drops markdown
`*`, backticks and leading `#`, replaces anything else outside `$20`-`$7E`
with `?`, wraps words at `--cols` (32), and hard-breaks words that are too
long. Line breaks are sent as CR. A line that exactly fills 32 columns gets no
CR, because the CoCo console has already moved to the next row.
`--selftest` checks all of this.

**Pacing.** A single writer thread sends output at `--cps` (default 400)
characters per second, because XRoar drops Becker bytes when its receive
buffer is full.

**Authentication.** If `ANTHROPIC_API_KEY` is set, it takes precedence over the
claude.ai login, and turns are billed to that key. `--no-api-key` removes it
from the environment of the `claude` processes only.

**Testing without XRoar.** `--nc` sends CRLF, turns off echo and autowrap, and
maps backspace and clear screen to terminal equivalents, so
`nc 127.0.0.1 65504` works from a normal terminal.

---

# `cocoterm_mcp.py`: Peek and Poke Tools for Live CoCo Memory

A stdio MCP server that gives Claude two tools, `peek` (read up to 1024 bytes)
and `poke` (write up to 1024 bytes). The bridge passes it to every turn with
`--mcp-config` and allows `mcp__coco__peek` and `mcp__coco__poke`.

**Path of a request.**

1. Claude calls the tool. The MCP server sends one JSON line to the bridge's
   control port, `127.0.0.1:65505` (`--mem-port`):
   `{"op": "peek", "addr": 1024, "len": 16}` or
   `{"op": "poke", "addr": 1024, "data": "<hex>"}`.
2. The bridge splits the request into frames of at most 64 bytes and sends
   them to the CoCo inline with terminal output:
   `ESC P addrH addrL n` to read, `ESC W addrH addrL n data...` to write.
3. `terminal.fs` handles the frame and answers `ESC` plus the bytes read, or a
   bare `ESC` after a write. The bridge separates these replies from
   keystrokes and returns `{"ok": true, "data": "<hex>"}` or
   `{"ok": false, "error": "..."}`.

A request fails with an error if no CoCo is connected or no reply arrives
within 5 seconds. The tool descriptions include the ROM-mode memory map and
warn Claude away from `$FF40`-`$FF4F`, since reading the Becker port itself
breaks the link.

---

# `fc.py` note: string literals

Two `S"` or `."` literals on one source line used to hang the tokenizer (#563);
that is fixed. One limitation remains: comment stripping runs before literal
extraction, so `(`, `)` and `\` inside a string are still removed. Keep them
out of string literals.
