# VDG Mode Demo

Cycles through all 11 MC6847 display modes on the TRS-80 Color Computer. Each mode shows a visual pattern plus the mode name, rendered using whichever technique that mode supports. Press any key to advance to the next mode.

## Modes

| Mode | Resolution | Colors | VRAM | Pattern | Text Method |
|------|-----------|--------|------|---------|-------------|
| Alpha | 32x16 chars | 2 | 512 | Green screen | VDG character ROM |
| SG4 | 64x32 | 8 | 512 | 8 color bars | VDG character ROM |
| SG6 | 64x48 | 4 | 512 | Color stripes | Block-pixel font |
| CG1 | 64x64 | 4 | 1024 | 4 color bands | Nibble-expanded font |
| RG1 | 128x64 | 2 | 1024 | Checkerboard | Direct bitmap font |
| CG2 | 128x64 | 4 | 1536 | 4 color bands | Nibble-expanded font |
| RG2 | 128x96 | 2 | 1536 | Checkerboard | Direct bitmap font |
| CG3 | 128x96 | 4 | 3072 | 4 color bands | Nibble-expanded font |
| RG3 | 128x192 | 2 | 3072 | Checkerboard | Direct bitmap font |
| CG6 | 128x192 | 4 | 6144 | 4 color bands | Nibble-expanded font |
| RG6 | 256x192 | 2 | 6144 | Checkerboard | Direct bitmap font |

## Text Rendering — Four Strategies

The VDG has no unified text API across modes, so the demo uses a different rendering strategy for each mode category:

**Alpha / SG4** — The VDG's internal character ROM does the work. `EMIT` writes ASCII to VRAM and the hardware renders it. Simple and free.

**RG modes** (1 bit/pixel) — Each 5x7 font byte maps directly to one VRAM byte. The font stores pixels in bits 7-3, which happen to be the leftmost 5 of 8 screen pixels. One byte per font row, one row per `bytes_per_row` stride.

**CG modes** (2 bits/pixel) — Each font bit must expand to 2 CG bits. A 16-entry lookup table at `$58B0` maps 4 font bits to 1 CG byte (each input bit becomes 2 identical output bits). Two CG bytes per font row.

**SG6** — The trickiest case. SG6 sets INT\*/EXT=1 (PIA bit 4), which disables the internal character ROM entirely — `EMIT` produces garbage. Instead, each font pixel becomes an entire SG6 cell: `$BF` (solid filled block) for pixels that are on, `$80` (empty) for off. Characters are 5 cells wide by 7 rows tall.

## Architecture

### Mode Table

11 entries x 16 bytes at `$5800`. Each entry packs everything needed to switch to and display a mode:

```
Offset  Size  Field
+0      2     VRAM base address
+2      2     VRAM size (bytes)
+4      1     SAM V bits (0-6)
+5      1     PIA $FF22 bits (A*/G, GM2, GM1, GM0, CSS)
+6      1     Mode type (0=alpha, 1=sg4, 2=sg6, 3=cg, 4=rg)
+7      1     Bytes per row
+8      8     Mode name (ASCII, space-padded)
```

### Mode Switching

`switch-hw` reads the mode table entry and programs the hardware:

1. Set SAM V0-V2 display mode bits (data rate)
2. Set SAM F0-F6 display offset (VRAM location)
3. Set PIA $FF22 bits 7-3 (VDG mode pins), preserving bits 0-2

### Memory Map

| Address | Contents |
|---------|----------|
| `$0400` | Text/SG VRAM (512 bytes, shared by Alpha, SG4, SG6) |
| `$3000` | Graphics VRAM (up to 6144 bytes for CG6/RG6) |
| `$5800` | Mode table (176 bytes) |
| `$58B0` | CG nibble-expand table (16 bytes) |
| `$6000` | Font data (37 glyphs x 7 bytes = 259 bytes) |

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run
cd src/vdg-modes
mkdir -p build
python3 ../../forth/tools/fc.py vdg-modes.fs \
    --kernel ../../forth/kernel/build/kernel.map \
    --kernel-bin ../../forth/kernel/build/kernel.bin \
    --output build/vdg-modes.bin
xroar -machine coco2bus -run build/vdg-modes.bin
```

## Shared Libraries

- `forth/lib/vdg.fs` — VDG/SAM mode switching (set-sam-v, set-sam-f, set-pia, reset-text)
- `forth/lib/font5x7.fs` — 5x7 bitmap font (37 glyphs: space, A-Z, 0-9)
