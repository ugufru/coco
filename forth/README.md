# CoCo Forth — Version 1.0

A cross-compiled Forth system for the TRS-80 Color Computer, targeting the
Motorola 6809 CPU. Write Forth on a modern machine, cross-compile with
`fc.py`, run on real hardware or under emulation.

## Architecture

- **Threading**: Indirect Threaded Code (ITC)
- **CPU**: Motorola 6809 (X=IP, U=DSP, S=RSP, Y=scratch)
- **Kernel**: ~4K of 6809 assembly — 80+ primitives including graphics, sprites, beam tracing, and small-int literal compression (LIT0/1/2/3/4/-1)
- **Compiler**: Python cross-compiler (`fc.py`) — Forth source to DECB binary
- **Target**: TRS-80 Color Computer 1/2/3, 32K minimum (64K for all-RAM apps)

The kernel ships in two build profiles:

| Profile | Build flag | Kernel ORG | App base | RAM | BASIC ROMs |
|---|---|---|---|---|---|
| ROM mode (default) | (none) | `$2000` | `$3000` | 32K | live at `$A000+` |
| all-RAM | `-DALL_RAM=1` / `KERNEL_VARIANT=allram` | `$E000` | `$2000` | 64K | paged out |

ROM mode is the default — apps run on a stock 32K CoCo, BREAK exits cleanly to
the BASIC `OK` prompt, and there's no staging copy. All-RAM mode is opt-in for
apps that need >18K of contiguous code.

No interactive REPL or on-device compiler. The host compiles; the CoCo executes.

## Directory Layout

```
forth/
├── kernel/             6809 assembly kernel (lwasm)
│   ├── kernel.asm      source — primitives, build profiles, variables
│   ├── Makefile        'make' = ROM kernel; 'make allram' = all-RAM kernel
│   └── README.md       full primitive reference and memory maps (both profiles)
├── tools/
│   ├── fc.py           Forth cross-compiler
│   └── README.md       compiler pipeline docs
├── lib/                shared Forth libraries (.fs)
├── hello/              hello world example
├── PROJECT_SETUP.md    setting up your own project — Makefile, fc.py options,
│                       kernel build overrides, common scenarios
├── LICENSE             BSD 2-clause
└── README.md           this file
```

For starting a new project of your own, see [`PROJECT_SETUP.md`](PROJECT_SETUP.md).

## Quick Start

### Prerequisites

- [lwtools](https://www.lwtools.ca/) — 6809 cross-assembler (`brew install lwtools`)
- Python 3 — for the cross-compiler
- [XRoar](https://www.6809.org.uk/xroar/) — CoCo emulator (`brew install xroar`)
- CoCo 2 ROMs in `~/.xroar/roms/`: `bas12.rom`, `extbas11.rom`

### Build and Run

```sh
cd kernel
make          # assemble ROM-mode kernel → build/kernel.bin + kernel.map
make run      # compile hello.fs + kernel → launch in XRoar (32K)

make allram   # also build the all-RAM kernel → build/kernel-allram.{bin,map}
```

### Compile Your Own Program

```sh
python3 tools/fc.py myapp.fs \
    --kernel     kernel/build/kernel.map \
    --kernel-bin kernel/build/kernel.bin \
    --output     myapp.bin

xroar -machine coco2bus -ram 32 \
    -bas ~/.xroar/roms/bas12.rom \
    -extbas ~/.xroar/roms/extbas11.rom \
    -run myapp.bin
```

For all-RAM apps (e.g. clock with FujiNet, or anything needing >18K of code),
add `KERNEL_VARIANT=allram` to your demo Makefile and use `-ram 64` in XRoar.

### Load from Disk (DECB)

Binaries are LOADM-safe by default. With a Disk BASIC system:

```
LOADM"MYAPP":EXEC
```

## Hello World

```forth
\ hello.fs
: space  $20 EMIT ;
: bare   CHAR B EMIT CHAR A EMIT CHAR R EMIT CHAR E EMIT ;
: naked  CHAR N EMIT CHAR A EMIT CHAR K EMIT CHAR E EMIT CHAR D EMIT ;
: forth  CHAR F EMIT CHAR O EMIT CHAR R EMIT CHAR T EMIT CHAR H EMIT ;
: main   bare space naked space forth HALT ;
```

Produces: `BARE NAKED FORTH` on the CoCo screen.

## License

BSD 2-clause. See [LICENSE](LICENSE).
