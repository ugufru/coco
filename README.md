# CoCo Forth — Version 1.2

A cross-compiled Forth system for the TRS-80 Color Computer, targeting the
Motorola 6809 CPU. Write Forth on a modern machine, cross-compile with
`fc.py`, run on real hardware or under emulation.

> **New in 1.2 — asynchronous sound.** `lib/async-sound.fs` adds a
> non-blocking, cooperative DAC sound engine: a phase-accumulator wavetable
> voice driven by HSYNC-flag polling (`snd-poll` / `snd-fill`), so a game can
> keep a tone playing across frames without a blocking delay loop and with no
> kernel changes. See `src/sound-async`, `proposals/SOUND_ENGINE_PROPOSAL.md`,
> and `proposals/hsync_polling_budget_chart.html`.

> **Since 1.2 (unreleased): the cartridge sound chips.** Forth libraries
> now drive every sound chip the CoCo Zero carries: the SN76489 on the
> GameMaster cartridge (`lib/sn76489.fs`, with a song player on the 60 Hz
> interrupt), VGM files on it (`lib/vgm.fs`), the AY-3-8910 on the CoCo PSG
> and the four-chip Symphony-12 (`lib/ay8910.fs`), and the Orchestra-90's
> stereo DACs (`lib/orch90.fs`). `src/sound-tour` plays one phrase on each,
> next to the CoCo's own 1-bit and 6-bit outputs; `src/jukebox` is a disk
> radio player, built on `lib/decb.fs`, a DECB file reader that needs no Disk
> BASIC; `src/sound-sn76489` plays Bach's Badinerie. Every library now has a
> section in `reference.html`.

> **Also new: a CoCo terminal for Claude Code.** `src/terminal` is a Forth
> terminal that talks to Claude Code on the Mac over XRoar's Becker port.
> `lib/console.fs` adds a scrolling console with true lowercase for the
> CoCo 2B's MC6847T1, and `lib/becker.fs` provides the byte I/O.
> `tools/cocoterm_bridge.py` runs Claude and reflows its replies to 32
> columns, and `tools/cocoterm_mcp.py` gives Claude `peek` and `poke` tools
> for live CoCo memory, which it used to draw `src/vader`.

## Architecture

- **Threading**: Indirect Threaded Code (ITC)
- **CPU**: Motorola 6809 (X=IP, U=DSP, S=RSP, Y=scratch)
- **Kernel**: ~4K of 6809 assembly — 84 primitives + 3 inline data words (`font-data`, `sprite-data`, `sin-data`); covers graphics, sprites, beam tracing, RNG, and small-int literal compression (`LIT0`/`LIT1`/`LIT2`/`LIT3`/`LIT4`/`LITM1`)
- **Compiler**: Python cross-compiler (`fc.py`) — Forth source to DECB binary; supports CODE / KCODE inline assembly, `+FIELD` struct definers, and `DATA[PY]` compile-time data generation
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
kernel/                 6809 assembly kernel (lwasm)
├── kernel.asm          source — primitives, build profiles, variables
├── Makefile            'make' = ROM kernel; 'make allram' = all-RAM kernel
└── README.md           full primitive reference and memory maps (both profiles)
tools/
├── fc.py               Forth cross-compiler
├── cocoterm_bridge.py  Becker-port bridge from the CoCo terminal to Claude Code
├── cocoterm_mcp.py     peek/poke MCP tools for live CoCo memory
└── README.md           compiler pipeline and tool docs
lib/                    shared Forth libraries (.fs), 30 of them; index in lib/README.md
├── sound.fs            DAC and 1-bit tones and effects (async-sound.fs: non-blocking)
├── sn76489.fs          SN76489 (GameMaster) chip words and song player
├── vgm.fs              VGM playback on the SN76489
├── ay8910.fs           AY-3-8910 / YM2149: CoCo PSG and Symphony-12
├── orch90.fs           Orchestra-90CC stereo DACs
├── decb.fs             read DECB disk files without Disk BASIC
├── console.fs          scrolling console with MC6847T1 true lowercase
└── becker.fs           XRoar Becker port byte I/O
src/                    demo programs (each with its own Makefile and README)
├── hello/              hello world example
├── tetris/ bounce/ ... games and graphics (gallery: tutorials/demos)
├── sound-tour/         one phrase on every sound chip
├── jukebox/            COCO.FM, a disk radio for .SNG and .VGM songs
├── sound-sn76489/      Bach's Badinerie on the SN76489
├── terminal/           CoCo terminal for Claude Code
└── vader/              SG4 art Claude pokes into video RAM
make/demo.mk            shared per-demo build rules
tutorials/              13-chapter Getting Started tutorial, demo gallery, disk guide
reference.html          language reference: primitives, compiler, every library
build/demos.dsk         16 demos on a DECB disk (make dsk)
build/sound.dsk         the sound demos and the jukebox's songs (make sound-dsk)
ROADMAP.md              where the project is going; roadmap.jsonl is the ranked queue
issues.jsonl            the issue tracker; issues.html browses it (make issues)
PROJECT_SETUP.md        setting up your own project — Makefile, fc.py options,
                        kernel build overrides, common scenarios
LICENSE                 BSD 2-clause
README.md               this file
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

The demos come on two ready-made disks (needs Toolshed's `decb`):

```sh
make dsk          # build/demos.dsk: 16 demos
make sound-dsk    # build/sound.dsk: the sound demos and the jukebox's songs
```

The sound demos that use a cartridge chip need it: a GameMaster for the
SN76489, an Orchestra-90, or a CoCo Zero for the AY chips. Each entry in the
demo gallery (`tutorials/demos`) says what it needs.

## Hello World

```forth
\ hello.fs (src/hello)
INCLUDE ../../lib/bye.fs

: SPACE  32 EMIT ;
: DOT    CHAR . EMIT ;
: BARE   CHAR B EMIT  CHAR A EMIT  CHAR R EMIT  CHAR E EMIT ;
: NAKED  CHAR N EMIT  CHAR A EMIT  CHAR K EMIT  CHAR E EMIT  CHAR D EMIT ;
: FORTH  CHAR F EMIT  CHAR O EMIT  CHAR R EMIT  CHAR T EMIT  CHAR H EMIT ;
: digit  ( n -- )  CHAR 0 + EMIT ;
: version  ( ver -- )  DUP 8 RSHIFT digit DOT $FF AND digit ;

BARE SPACE NAKED SPACE FORTH SPACE KERN-VERSION version
BEGIN KEY 3 = UNTIL exit-basic
```

Produces `BARE NAKED FORTH 1.2` on the CoCo screen; BREAK returns to BASIC.
`KERN-VERSION` is the kernel's own version, which `fc.py` passes in as a
constant.

## License

BSD 2-clause. See [LICENSE](LICENSE).
