# Bounce — HSYNC Sprite Demo

A testbed for flicker-free sprite rendering on the TRS-80 Color Computer using beam-chasing techniques.

## What it does

Four 7x7 artifact-color balls bounce around a 128x192 RG6 screen. The demo lets you switch between three rendering modes in real time to see the visual difference that VSYNC and HSYNC synchronization make.

## Why it exists

On the CoCo, writing to VRAM while the VDG beam is scanning the same row produces visible tearing. This demo explores the standard mitigation: poll the PIA0 HSYNC flag ($FF01 bit 7) to count scanlines after VSYNC, then draw only after the beam has passed the sprite area.

Key findings from this demo:

- **HSYNC beam-chasing works.** Waiting for the beam to pass the draw area before modifying VRAM eliminates most visible flicker.
- **Per-frame budget matters.** Drawing all 4 sprites in one frame (~20ms) exceeds the 16.7ms frame time, making VSYNC irrelevant (the flag is already set). Splitting work across frames (2 balls per frame) keeps draw time under budget and makes sync effective.
- **The blanking offset is ~70 lines.** On NTSC, approximately 70 HSYNC pulses occur between VSYNC and visible row 0. This is a hardware constant, not a tunable.
- **HUD rendering cost.** Clearing large VRAM regions with Forth FILL is expensive (~9ms for 256 bytes). Overwriting in place (rg-char) avoids this.

## Controls

| Key | Action |
|-----|--------|
| Up arrow | Increase blanking offset |
| Down arrow | Decrease blanking offset |
| 0 | Mode 0: free-running (no sync) |
| 1 | Mode 1: VSYNC only |
| 2 | Mode 2: VSYNC + HSYNC beam-chasing |

The HUD at the bottom displays the current blanking offset (3 digits) and mode (1 digit).

## Modes

- **Mode 0** — No synchronization. The main loop runs as fast as possible. Balls move quickly but tearing is visible, especially on real hardware.
- **Mode 1** — VSYNC only. The loop waits for vertical blank before drawing, locking to 60fps. Tearing can still occur if the beam is scanning the sprite area during the draw.
- **Mode 2** — VSYNC + HSYNC. After VSYNC, the loop counts HSYNC pulses to wait until the beam has passed the sprite's vertical position, then draws behind the beam. This eliminates most visible flicker.

## Architecture

- **Ball table** at $4020: 4 balls x 12 bytes (x, y, dx, dy, old-x, old-y).
- **Time-sliced rendering**: 2 balls are moved and drawn per frame, alternating pairs. Each ball updates at 30fps; the loop runs at 60fps.
- **Sprite**: 7x7 artifact-color ball at $4000 (white circle, 2bpp encoding).
- **Font**: 5x7 bitmap font (font5x7.fs) for HUD digit display, written to `font-base` ($5800 in ROM mode).

## Building

```
make          # build bounce.bin
make run      # build and launch in XRoar
```

Requires the kernel to be built first (`make -C ../../forth/kernel`).

## CoCo Keyboard Matrix Reference

The CoCo keyboard has each key in its own column with shared row bits — not one column per group as some references suggest. Arrow keys:

| Key | Column strobe | Row bit |
|-----|---------------|---------|
| UP | $F7 | $08 |
| DN | $EF | $08 |
| LT | $DF | $08 |
| RT | $BF | $08 |

Number keys 0-9 span columns $FE-$DF, all at row bit $10.
