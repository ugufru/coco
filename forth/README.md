# CoCo Forth — Version 1.0

A cross-compiled Forth system for the TRS-80 Color Computer, targeting the
Motorola 6809 CPU. Write Forth on a modern machine, cross-compile with
`fc.py`, run on real hardware or under emulation.

## Architecture

- **Threading**: Indirect Threaded Code (ITC)
- **CPU**: Motorola 6809 (X=IP, U=DSP, S=RSP, Y=scratch)
- **Kernel**: ~4K of 6809 assembly — 80+ primitives including graphics, sprites, beam tracing, and small-int literal compression (LIT0/LIT1/LIT2)
- **Compiler**: Python cross-compiler (`fc.py`) — Forth source to DECB binary
- **Target**: TRS-80 Color Computer 1/2/3, 64K RAM

No interactive REPL or on-device compiler. The host compiles; the CoCo executes.

## Directory Layout

```
forth/
├── kernel/         6809 assembly kernel (lwasm)
│   ├── kernel.asm  source — primitives, bootstrap, variables
│   ├── Makefile    assembles kernel.bin + kernel.map
│   └── README.md   full primitive reference and memory map
├── tools/
│   ├── fc.py       Forth cross-compiler
│   └── README.md   compiler pipeline docs
├── lib/            shared Forth libraries (.fs)
├── hello/          hello world example
├── LICENSE         BSD 2-clause
└── README.md       this file
```

## Quick Start

### Prerequisites

- [lwtools](https://www.lwtools.ca/) — 6809 cross-assembler (`brew install lwtools`)
- Python 3 — for the cross-compiler
- [XRoar](https://www.6809.org.uk/xroar/) — CoCo emulator (`brew install xroar`)
- CoCo 2 ROMs in `~/.xroar/roms/`: `bas12.rom`, `extbas11.rom`

### Build and Run

```sh
cd kernel
make          # assemble kernel → build/kernel.bin + kernel.map
make run      # compile hello.fs + kernel → launch in XRoar
```

### Compile Your Own Program

```sh
python3 tools/fc.py myapp.fs \
    --kernel     kernel/build/kernel.map \
    --kernel-bin kernel/build/kernel.bin \
    --output     myapp.bin

xroar -machine coco2bus -ram 64 \
    -bas ~/.xroar/roms/bas12.rom \
    -extbas ~/.xroar/roms/extbas11.rom \
    -run myapp.bin
```

The `-ram 64` flag is required — the kernel uses all-RAM mode.

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
