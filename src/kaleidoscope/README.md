# CoCo Forth Kaleidoscope

A four-way symmetric pattern generator for the TRS-80 Color Computer, built on the CoCo Forth kernel. Draws random colored pixels in semigraphics-4 mode, mirrored across both axes to create evolving kaleidoscope patterns.

## How It Works

The 64×32 SG4 pixel grid is divided into four quadrants around the center point (32, 16). Each step picks a random position (dx, dy) and plots it in all four quadrants simultaneously:

```
(31−dx, 15−dy)  |  (32+dx, 15−dy)
----------------+----------------
(31−dx, 16+dy)  |  (32+dx, 16+dy)
```

With a 75% plot / 25% erase ratio, the pattern builds up quickly while slowly recycling old pixels. All 8 CoCo colors are used.

### SAM Double Speed

The computation phase runs at ~1.78 MHz by toggling the SAM speed bit (`$FFD7`), dropping back to normal speed (`$FFD6`) before vsync so the VDG can refresh the display cleanly. This allows 16 symmetric pixel operations per frame.

### RNG

Uses a 16-bit LCG (`seed × 25173 + 13849`, full period 65536) with high-byte extraction for power-of-2 modulo via AND — avoids the kernel's repeated-subtraction `/MOD` which is too slow for large dividends.

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile the kaleidoscope
cd src/kaleidoscope && make

# Run in XRoar
make run
```

## Controls

Press any key on the title screen to start. Close XRoar to exit.
