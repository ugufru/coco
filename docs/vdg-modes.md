# MC6847 VDG Display Modes

The MC6847 Video Display Generator is the CoCo's graphics chip. It takes bytes from video RAM, generates composite NTSC video, and sends it to the TV. The SAM chip (MC6883) feeds it data at the right rate; PIA1 ($FF22) tells it which mode to use.

Everything we've built so far — text, semigraphics-4, the kaleidoscope, digital rain — uses the default alphanumeric/SG4 mode: 512 bytes at $0400, no register changes needed. The VDG can do much more.

## Architecture

Three chips cooperate to produce video:

| Chip | Role | Key Registers |
|------|------|---------------|
| **MC6847 (VDG)** | Generates video signal from RAM data | Mode pins controlled by PIA1 |
| **MC6883 (SAM)** | Feeds RAM to VDG at correct rate, sets display offset | $FFC0–$FFD3 |
| **MC6821 (PIA1)** | Directly drives VDG mode pins from $FF22 bits | $FF22 bits 3–7 |

The VDG has no writable registers of its own. Its mode pins are directly wired to PIA1 port B ($FF22). You change display modes by writing to $FF22 and the SAM registers.

## PIA1 Port B ($FF22) — VDG Control Bits

```
Bit 7  A*/G     0 = alpha/semigraphics, 1 = full graphics
Bit 6  GM2      \
Bit 5  GM1       > graphics mode select (3 bits)
Bit 4  GM0      /  (also doubles as INT*/EXT for SG6)
Bit 3  CSS      color set select (0 or 1)
────────────────────────────────────────────────────
Bit 2  RAM size input (read-only, not VDG-related)
Bit 1  Single-bit sound output
Bit 0  RS-232 data input
```

**Important**: $FF22 is shared with sound and serial I/O. Read-modify-write (read, mask, OR, write) to avoid clobbering non-VDG bits.

## SAM Video Registers

The SAM uses paired clear/set addresses — write any value to the address to flip the bit:

### Display Mode (V0–V2): $FFC0–$FFC5

| Bit | Clear | Set | Purpose |
|-----|-------|-----|---------|
| V0  | $FFC0 | $FFC1 | Display mode bit 0 |
| V1  | $FFC2 | $FFC3 | Display mode bit 1 |
| V2  | $FFC4 | $FFC5 | Display mode bit 2 |

The SAM mode bits tell it how many bytes per row to feed the VDG. They must agree with the PIA mode bits or you get garbage.

### Display Offset (F0–F6): $FFC6–$FFD3

| Bit | Clear | Set | Purpose |
|-----|-------|-----|---------|
| F0  | $FFC6 | $FFC7 | Offset bit 0 |
| F1  | $FFC8 | $FFC9 | Offset bit 1 |
| F2  | $FFCA | $FFCB | Offset bit 2 |
| F3  | $FFCC | $FFCD | Offset bit 3 |
| F4  | $FFCE | $FFCF | Offset bit 4 |
| F5  | $FFD0 | $FFD1 | Offset bit 5 |
| F6  | $FFD2 | $FFD3 | Offset bit 6 |

**Display start address = offset × 512**. Default offset = 2, giving $0400.

For graphics modes needing more than 512 bytes of VRAM, you typically relocate the display to a contiguous block. For example, setting offset to 4 starts display at $0800.

## Display Modes — Complete Table

### Semigraphics Modes (A\*/G = 0)

These share the alphanumeric character cell grid. Bit 7 of each data byte selects between text (0) and semigraphics (1) on a per-cell basis — you can freely mix text and graphics.

| Mode | Resolution | Colors | VRAM | Byte Layout | SAM V2:V1:V0 | $FF22 Bits 7–4 |
|------|-----------|--------|------|-------------|---------------|-----------------|
| **Alpha** | 32×16 chars | 2 (CSS selects green or orange) | 512 | bit 7=0, bit 6=INV, bits 5–0=char | 000 | 0XX0 |
| **SG4** | 64×32 | 8 + black | 512 | bit 7=1, bits 6–4=color, bits 3–0=quadrants | 000 | 0XXX |
| **SG6** | 64×48 | 4 + black | 512 | bit 7=1, bit 6=C1, bits 5–0=six elements | 000 | 0XX1 |
| **SG8** | 64×64 | 8 + black | 2048 | Like SG4 but 4 bytes per cell position | 010 | 0XX0 |
| **SG12** | 64×96 | 8 + black | 3072 | 6 bytes per cell position | 100 | 0XX0 |
| **SG24** | 64×192 | 8 + black | 6144 | 12 bytes per cell position | 110 | 0XX0 |

SG4 and SG6 are the sweet spot — full 8-color graphics in just 512 bytes, mixed with text. SG8/12/24 trade RAM for vertical resolution while keeping the same 64-pixel horizontal density.

### Full Graphics Modes (A\*/G = 1)

True bitmap modes. No text mixing — every byte is pixel data.

| Mode | Resolution | Colors | VRAM | Bits/Pixel | SAM V2:V1:V0 | $FF22 Bits 7–4 |
|------|-----------|--------|------|-----------|---------------|-----------------|
| **CG1** | 64×64 | 4 | 1024 | 2 | 001 | 1000 |
| **RG1** | 128×64 | 2 | 1024 | 1 | 001 | 1001 |
| **CG2** | 128×64 | 4 | 1536 | 2 | 010 | 1010 |
| **RG2** | 128×96 | 2 | 1536 | 1 | 011 | 1011 |
| **CG3** | 128×96 | 4 | 3072 | 2 | 011 | 1100 |
| **RG3** | 128×192 | 2 | 3072 | 1 | 100 | 1101 |
| **CG6** | 128×192 | 4 | 6144 | 2 | 101 | 1110 |
| **RG6** | 256×192 | 2 | 6144 | 1 | 110 | 1111 |

**CG** (Color Graphics): 2 bits per pixel → 4 colors, 4 pixels per byte.
**RG** (Resolution Graphics): 1 bit per pixel → 2 colors, 8 pixels per byte.

### Pixel Packing

**CG modes** (2 bits/pixel, MSB first):
```
Byte: [P0 P0 P1 P1 P2 P2 P3 P3]
       7  6  5  4  3  2  1  0
```
Four pixels per byte. Pixel 0 is leftmost.

**RG modes** (1 bit/pixel, MSB first):
```
Byte: [P0 P1 P2 P3 P4 P5 P6 P7]
       7  6  5  4  3  2  1  0
```
Eight pixels per byte. Pixel 0 is leftmost.

### VRAM Address Calculation (Full Graphics)

```
byte_addr = vram_base + (y × bytes_per_row) + (x / pixels_per_byte)
bit_position = x MOD pixels_per_byte
```

| Mode | Bytes/Row | Pixels/Byte |
|------|----------|-------------|
| CG1  | 16       | 4           |
| RG1  | 16       | 8           |
| CG2  | 32       | 4           |
| RG2  | 16       | 8           |
| CG3  | 32       | 4           |
| RG3  | 16       | 8           |
| CG6  | 32       | 4           |
| RG6  | 32       | 8           |

## Color Sets

The CSS bit ($FF22 bit 3) selects between two palettes:

### Full Graphics Modes

| Pixel Value | CSS=0 | CSS=1 |
|-------------|-------|-------|
| 00 | Green | Buff |
| 01 | Yellow | Cyan |
| 10 | Blue | Magenta |
| 11 | Red | Orange |

In RG (2-color) modes, 0 = black background, 1 = foreground (Green if CSS=0, Buff if CSS=1). The border color matches the foreground.

### Alphanumeric Mode

| CSS | Characters | Background |
|-----|-----------|------------|
| 0 | Green on dark green | Black border |
| 1 | Orange on dark orange | Black border |

### Semigraphics-4

SG4 ignores CSS — it has its own 3-bit color field per cell, selecting from all 8 colors: Green(0), Yellow(1), Blue(2), Red(3), Buff(4), Cyan(5), Magenta(6), Orange(7).

### Artifact Colors

On real NTSC hardware (and accurate emulators), high-resolution modes (RG1, RG2, RG3, RG6) produce artifact colors when alternating pixel patterns interact with the NTSC chroma subcarrier. This gives an effective 4-color palette in what is technically a 2-color mode. Many CoCo games exploit this. The artifact palette depends on pixel phase alignment and varies slightly between CoCo models.

## VDG Character Set

The VDG's internal ROM has 64 characters — uppercase ASCII only:

```
$00–$1F:  @ A B C D E F G H I J K L M N O P Q R S T U V W X Y Z [ \ ] ^ ←
$20–$3F:  (space) ! " # $ % & ' ( ) * + , - . / 0 1 2 3 4 5 6 7 8 9 : ; < = > ?
```

Bit 6 inverts the character (green-on-black becomes black-on-green). No lowercase.

## Timing

- VDG clock: 3.579545 MHz (NTSC colorburst frequency)
- Horizontal scan: 63.5 µs (15,734 Hz)
- Vertical field: 16.667 ms (60 Hz)
- FS\* (field sync) → PIA0 CB1 → available as vsync interrupt
- HS\* (horizontal sync) → PIA0 CA1 → available as hsync interrupt

## Mode Switching — What It Takes

To switch from the default alpha/SG4 mode to a full graphics mode:

1. **Set SAM display mode** (V0–V2) to match the target mode's data rate
2. **Set SAM display offset** (F0–F6) to point at your VRAM buffer
3. **Write PIA1 $FF22** with the correct A\*/G, GM2, GM1, GM0, CSS bits (read-modify-write to preserve bits 0–2)
4. **Clear the VRAM buffer** (or fill with initial data)

To switch back to text mode:

1. Clear SAM V0–V2 (write to $FFC0, $FFC2, $FFC4)
2. Set SAM offset back to 2 (clear all F bits, then set F1)
3. Write $FF22 with A\*/G=0, GM bits as needed

### Example: Switch to RG6 (256×192, 2-color)

```forth
\ SAM: V2:V1:V0 = 110
$FF $FFC5 C!    \ set V2
$FF $FFC3 C!    \ set V1
$FF $FFC0 C!    \ clear V0

\ SAM: display offset = $0E00 (offset 7 → 7×512 = $0E00)
\ Set F0, F1, F2 (value 7)
$FF $FFC7 C!    \ set F0
$FF $FFC9 C!    \ set F1
$FF $FFCB C!    \ set F2
$FF $FFCC C!    \ clear F3
$FF $FFCE C!    \ clear F4
$FF $FFD0 C!    \ clear F5
$FF $FFD2 C!    \ clear F6

\ PIA: A*/G=1, GM2=1, GM1=1, GM0=1, CSS=0 → bits 7-3 = 11110
\ Read-modify-write $FF22
$FF22 C@ $07 AND $F0 OR $FF22 C!

\ Clear 6K of VRAM at $0E00
6144 0 DO  0 $0E00 I + C!  LOOP
```

## Kernel Primitive Requirements

The current kernel primitives are sufficient for basic graphics mode switching and pixel operations. Everything can be done with `C@`, `C!`, `@`, `!`, `AND`, `OR`, and arithmetic.

However, filling or copying large VRAM buffers in Forth (via `DO`/`LOOP` with `C!`) is painfully slow. Two new primitives would make graphics modes practical:

### FILL ( addr count byte -- )

Fill `count` bytes starting at `addr` with `byte`. Essential for clearing graphics buffers (up to 6144 bytes for RG6/CG6). A 6K fill in Forth takes ~36,000 Forth instruction dispatches. A native FILL primitive does it in ~18K CPU cycles — roughly 20× faster.

```asm
CODE_FILL
    LDD   ,U        ; byte (only B used)
    LDY   2,U       ; count
    LDX   4,U       ; addr
    LEAU  6,U       ; drop 3 cells
FILL_LOOP
    STB   ,X+
    LEAY  -1,Y
    BNE   FILL_LOOP
    ; NEXT
```

### CMOVE ( src dest count -- )

Copy `count` bytes from `src` to `dest`. Needed for scrolling, double-buffering, and sprite blitting. Same performance argument as FILL.

```asm
CODE_CMOVE
    LDY   ,U        ; count
    LDX   2,U       ; dest
    LDD   4,U       ; src (use as pointer via ,D — actually need different approach)
    ; ... (needs temp for src pointer)
    LEAU  6,U       ; drop 3 cells
```

### Nice-to-Have: Bit manipulation helpers

For pixel-level operations in RG/CG modes, Forth code needs to shift bits and create masks. The kernel has `AND` and `OR` but no shift operations. These could help:

- **LSHIFT** ( n count -- n' ) — logical left shift
- **RSHIFT** ( n count -- n' ) — logical right shift

Without these, pixel plotting requires lookup tables or repeated `DUP +` (which works for left-shift-by-1 but is clumsy for arbitrary shifts).

### Summary: What's Needed vs. What's Nice

| Primitive | Priority | Why |
|-----------|----------|-----|
| **FILL** | High | 6K buffer clears are unusable without it |
| **CMOVE** | High | Scrolling, double-buffering, sprite copy |
| LSHIFT | Medium | Pixel mask generation |
| RSHIFT | Medium | Pixel extraction |

Everything else — mode switching, pixel plotting, color set selection — works fine with existing primitives composed into Forth words.
