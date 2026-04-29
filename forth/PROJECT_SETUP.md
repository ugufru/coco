# Setting Up a Bare Naked Forth Project

How to build a CoCo Forth program from scratch — the minimal case, the
defaults each option falls back to, and the knobs available when defaults
don't fit.

---

## TL;DR — minimal project

```sh
mkdir src/myapp
cat > src/myapp/Makefile <<'EOF'
NAME = myapp

include ../../make/demo.mk
EOF

cat > src/myapp/myapp.fs <<'EOF'
INCLUDE ../../forth/lib/bye.fs

: main
  CHAR H EMIT  CHAR I EMIT  CR
  exit-basic ;

main
EOF

cd src/myapp && make run
```

Three lines of Makefile and a `.fs` file. Builds the ROM-mode kernel
automatically, links your app against it, and launches XRoar with a 32K CoCo.
`BREAK` returns to the BASIC `OK` prompt.

---

## Project layout

A "project" is one directory under `src/` with two files:

```
src/<name>/
├── Makefile      → 3 lines: NAME = ..., include ../../make/demo.mk
└── <name>.fs     → your Forth source (or whatever you set SRC to)
```

The shared rules in [`make/demo.mk`](../make/demo.mk) handle:

- building the kernel (if not already built)
- running `fc.py` to combine kernel + app into a DECB binary
- launching XRoar with the right RAM size and ROM set

Most projects only need to set `NAME`. Everything else has a default.

---

## Build profiles — ROM vs all-RAM

The kernel ships in two flavours. Pick the one that matches your needs:

| | ROM mode (default) | all-RAM |
|---|---|---|
| Build flag | (none) | `lwasm -DALL_RAM=1` |
| Demo Makefile | `KERNEL_VARIANT` (unset) | `KERNEL_VARIANT = allram` |
| Kernel ORG | `$2000` | `$E000` |
| App base | `$3000` | `$2000` |
| Min RAM | 32K | 64K |
| BASIC ROMs | live (`$A000+`) | paged out |
| `BREAK` exits | cleanly to `OK` prompt | only halts (no clean exit) |
| App code budget | ~10K (between kernel and font) | ~24K contiguous |
| Bootstrap | none — DECB exec → `START` | yes — `$0E00` stage+copy |

**Use ROM mode** unless you specifically need >10K of contiguous code or
the upper 32K of RAM. Most demos fit easily in ROM mode.

**Use all-RAM** when:
- Your app is bigger than ~10K (e.g. tetris, vdg-modes-cycling-everything)
- You need RAM at `$8000+` for buffers/data (e.g. clock's double-buffering)
- You're calling FujiNet/DriveWire from inside an all-RAM session and need
  `fujinet.fs`'s SAM TY toggling (see Edge Cases below)

See [`kernel/README.md`](kernel/README.md#memory-map) for full memory maps.

---

## Configuration knobs

Knobs live at three levels. Reach for the lowest level that solves your
problem.

### Level 1 — demo Makefile (per-project)

These go above `include ../../make/demo.mk`:

| Variable | Default | What it does | When to set |
|---|---|---|---|
| `NAME` | (required) | Binary stem (`NAME.bin`) | Always |
| `SRC` | `$(NAME).fs` | Top-level Forth source | Source file has a different name |
| `EXTRA_DEPS` | empty | Extra prerequisites for `$(BIN)` | Source uses `INCLUDE lib/foo.fs`; list those libs so `make` rebuilds when they change |
| `KERNEL_VARIANT` | (unset = ROM) | `allram` selects the all-RAM kernel | App needs >10K code or hi-RAM data |
| `XROAR_RAM` | `32` (ROM) / `64` (allram) | RAM kB for XRoar `-ram` flag | Want to test on a 16K layout |
| `XROAR_EXTRA` | `-kbd-translate` | Extra args appended to XRoar | NTSC artifacts (`-tv-input cmp-br`); cart ROM (`-cart-rom ...`) |

Example — RG6 demo with NTSC artifact colour:
```make
NAME        = mydemo
EXTRA_DEPS  = ../../forth/lib/rg-pixel.fs
XROAR_EXTRA = -tv-input cmp-br -kbd-translate

include ../../make/demo.mk
```

### Level 2 — fc.py flags (compile-time)

Set these by appending to the `$(FC) ...` line in your Makefile, or running
`fc.py` directly. Most projects don't need any of these.

| Flag | Default | What it does | When to set |
|---|---|---|---|
| `--kernel <path>` | `build/kernel.map` | lwasm map for symbol resolution | Custom kernel build |
| `--kernel-bin <path>` | (none) | kernel `.bin` to bundle into output | Always (the demo.mk rule sets it) |
| `--output <path>` | `<src>.bin` | output filename | Renaming the binary |
| `--base <addr>` | `APP_BASE` from kernel.map | Override app load address | Reserving address space below the app for buffers (e.g. extra VRAM bank) |
| `--hole <addr>,<size>` | (none) | Reserve a region inside the app's address space — fc.py emits two records skipping the hole | Want a fixed-address buffer mid-app (sprite tables, lookup tables) without growing the binary |
| `--stage-base <addr>` | auto-pack after bootstrap | Pin all-RAM staging area | Want KCODE words at a stable address across kernel growth (used by spacewarp project) |
| `--cycles` | off | Print per-word 6809 cycle cost estimates | Performance tuning |

Common scenarios:

- **"I have a 6K back buffer that needs a fixed address."** Use `--hole`
  to keep that range out of the app binary, set a `CONSTANT` in your `.fs`
  at the same address. Or use `--base` to push the entire app past it.
- **"My binary keeps shifting around as the kernel grows and breaks
  external tools."** Use `--stage-base` to pin staging.
- **"I want to know which Forth word burns the most cycles."** Use
  `--cycles`.

### Level 3 — kernel build overrides (lwasm `-D` flags)

These rebuild the kernel itself. Edit `forth/kernel/Makefile` or invoke
lwasm directly. Almost no project needs this — only relevant when you're
targeting an unusual machine (16K), porting to a different memory map, or
building a custom kernel variant.

| Define | Default (ROM) | Default (all-RAM) | Effect |
|---|---|---|---|
| `ALL_RAM` | undefined | `1` | Selects all-RAM mode (changes everything below) |
| `KERNEL_ORG` | `$2000` | `$E000` | Where the kernel ORGs |
| `APP_BASE` | `$3000` | `$2000` | Where the app loads |
| `VRAM_BASE` | `$0600` | `$0600` | RG6 VRAM location |
| `FONT_BASE` | `$5800` | `$9000` | Font glyph table location |
| `TRIG_BASE` | `$7800` | `$86CC` | Sine lookup table location |
| `RSP_INIT` | `$8000` | `$E000` | Return stack top |
| `DSP_INIT` | `$7E00` | `$DE00` | Data stack top |

Example — build a 16K-target ROM kernel:
```sh
cd forth/kernel
lwasm --format=decb --output=build/kernel-16k.bin --map=build/kernel-16k.map \
      -DRSP_INIT=$4000 -DDSP_INIT=$3E00 \
      kernel.asm
```

Then point your demo at it via `KERNEL_STEM = kernel-16k`.

---

## Forth-side build constants

`fc.py` injects these as Forth literals in every compile. **Use them
instead of hardcoded addresses** — your source then builds against either
profile without changes.

| Forth name | What it is | ROM mode | all-RAM |
|---|---|---|---|
| `app-base` | Where this app's code starts | `$3000` | `$2000` |
| `vram-base` | RG6 VRAM base (kernel-reserved) | `$0600` | `$0600` |
| `font-base` | Where `init-font` writes glyphs | `$5800` | `$9000` |
| `trig-base` | Where `init-sin` writes the sine table | `$7800` | `$86CC` |

Example — using them:
```forth
INCLUDE ../../forth/lib/rg-pixel.fs

: my-init
  rg-init                         \ uses vram-base internally
  init-font                       \ writes font-base
  font-base 472 0 FILL            \ explicit reference to font-base
  ;
```

The same source builds and runs in both profiles. The numbers shift; the
program doesn't.

---

## Common scenarios

### "I want a small SG4 (text) app on 32K"
Just `NAME = foo`. ROM mode is the default. Done.

### "I want RG6 graphics"
Add `INCLUDE ../../forth/lib/rg-pixel.fs`. Call `rg-init`. The kernel
reserves `$0600–$1DFF` for VRAM in both modes; `rg-init` configures the
VDG to display from `vram-base`. Set `XROAR_EXTRA = -tv-input cmp-br` for
NTSC artifact colour.

### "I want to call BASIC ROM routines (DSKCON, RTC, etc.)"
ROM mode (default) keeps `$A000–$DFFF` mapped. Direct `JSR` works:
```forth
CODE call-dskcon
        JSR     $D75F
        ;NEXT
;CODE
```

### "I need a clean exit to BASIC"
ROM mode + `INCLUDE ../../forth/lib/bye.fs` + call `exit-basic` — JMPs to
BASIC's cold start at `$A027`. Doesn't work in all-RAM mode (ROMs paged out).

### "My app code is over ~10K"
Switch to all-RAM:
```make
NAME           = bigapp
KERNEL_VARIANT = allram
```
You now get 24K contiguous code space at `$2000–$8FFF`, but lose the BASIC
ROM and the clean BREAK exit.

### "I need a fixed-address buffer in the middle of my app"
Use `--hole`. Add to your Makefile:
```make
$(BIN): $(SRC) $(EXTRA_DEPS) $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) $(SRC) \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --hole      0x5000,1536 \
	    --output    $(BIN)
```
fc.py emits app records that skip `$5000–$55FF`; you can write a
`$5000 CONSTANT MYBUF` and use that 1536-byte area without growing the
binary.

### "FujiNet calls hang or crash"
The DriveWire vectors at `$D93F`/`$D941` only exist when the HDB-DOS-CC
cart ROM is loaded. Add to the demo Makefile:
```make
XROAR_EXTRA = -cart-rom ~/.xroar/roms/hdbdw3cc2.rom -kbd-translate
```
And test with a real FujiNet or `xroar -becker` for the DriveWire endpoint.

### "I'm targeting a 16K CoCo"
Rebuild the kernel with reduced stack defaults (see Level 3 above). Stick
to SG4 modes — RG6 doesn't fit (kernel + 6K VRAM = no app room).

---

## Edge cases

- **fujinet.fs SAM toggling** — `dw-write`/`dw-read` toggle SAM TY around
  the cart-ROM JSR for all-RAM kernels (where TY=1 hides the cart ROM).
  In ROM mode the toggle compiles out via `IFEQ KERNEL_ORG-$E000`, since
  the cart is always visible. No app-side change needed.

- **`CONSTANT` requires a literal** — fc.py's `CONSTANT` parser needs a
  numeric literal, not another constant. So `vram-base CONSTANT GVRAM`
  fails; use `vram-base` directly instead, or hardcode `$0600 CONSTANT
  GVRAM` if you really want a per-app symbol.

- **`EXIT` inside `IF`** — known fc.py compilation quirk. The
  `IF ... EXIT THEN` pattern can mis-emit branch offsets in some cases.
  Restructure as `IF ... THEN` with the body inside the `IF`, or
  `IF ... ELSE THEN`.

- **High-RAM data in ROM mode** — addresses `$8000–$DFFF` are BASIC
  ROM in ROM mode (writes silently dropped). Anything you previously had
  at `$8000`/`$9000`/`$A000` needs relocation. Use `font-base` /
  `trig-base` for those tables, or pick a free spot in the heap area
  (`$5A00–$7DFF` on 32K).

---

## See also

- [`kernel/README.md`](kernel/README.md) — full memory maps, primitive
  reference, boot sequence
- [`tools/README.md`](tools/README.md) — fc.py compilation pipeline, ITC
  threading, inline `CODE` word syntax
- [`../make/demo.mk`](../make/demo.mk) — the shared Makefile (canonical
  source for variable defaults)
