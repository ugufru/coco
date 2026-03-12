# Digital Rain

A Matrix-style falling character effect for the TRS-80 Color Computer. The VDG's green-on-black text mode is a natural fit for the aesthetic — no tricks needed to get the right colors.

32 independent streams of characters fall at different speeds across the screen. Each stream has a bright head, a dimmer trail of mutating characters, and a black erasing tail. Close XRoar to exit.

## How It Looks

The VDG text mode has a natural 3-level brightness hierarchy that maps perfectly to the Matrix effect:

```
Column states (conceptual):

  col 3   col 7   col 15  col 22
  ·····   ·····   ·····   ·····
  ·····   ·····   T trail ·····
  ·····   T trail T trail ·····
  ·····   T trail T trail ·····
  H head  T trail H head  ·····
  ·····   H head  ·····   T trail
  ·····   ·····   ·····   T trail
  ·····   ·····   ·····   H head

  H = inverse video (bright green block with dark letter)
  T = normal video (green letter on black)
  · = $80 (solid black)
```

- **Head** (brightest): inverse video character — `$40 OR char`
- **Trail** (medium): normal video character — `$3F AND char`
- **Background**: `$80` (SG4 black block, fully dark)

Trail characters mutate randomly, swapping to new letters for the "flickering code" look.

## Data Structure

Four parallel arrays at `$4000`, one entry per column (32 columns × 2 bytes = 64 bytes each):

| Array | Address | Purpose |
|-------|---------|---------|
| `heads` | `$4000` | Head row position (0–15 on-screen; `$FF` = inactive) |
| `tails` | `$4040` | Tail row position (erasing end, trails behind head) |
| `speeds` | `$4080` | Frames between advances (2–5) |
| `timers` | `$40C0` | Countdown to next advance |

Trail length is implicit: the distance between head and tail. When a column spawns, the tail starts at a negative value (−4 to −7), so the trail "grows in" from the top before erasing begins.

## Algorithm

Each frame (synced to 60 Hz vsync):

1. **Update all 32 columns**: decrement timer; when it hits zero, advance head and tail by one row, drawing/erasing as they move
2. **Mutate**: ~2 random trail characters per frame get swapped to new letters
3. **Spawn**: try 2 random columns; activate any that are inactive

When a column's tail passes row 15 (bottom of screen), it deactivates. Spawn checks gradually reactivate columns, keeping a steady rain density.

## Performance

Computation runs at SAM double speed (~1.78 MHz), dropping back to normal speed before vsync for clean display — same technique used by the kaleidoscope demo.

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run
cd src/rain && make run
```

## Shared Libraries

- `forth/lib/rng.fs` — 16-bit LCG random number generator
- `forth/lib/screen.fs` — vsync synchronization, screen clearing
