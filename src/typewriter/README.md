# CoCo Forth Typewriter

A bare-metal keyboard echo test for the TRS-80 Color Computer, built on the CoCo Forth kernel. Types characters to the VDG screen with a visible block cursor and full cursor movement — no BASIC ROM dependency.

## What It Demonstrates

This demo exercises the kernel's **keyboard and display primitives** end-to-end:

- **KEY** — blocking keyboard read via direct PIA0 matrix scan (8 columns × 7 rows), with debounce and SHIFT support. No ROM calls.
- **EMIT** — character output with ASCII-to-VDG encoding (`$40 | (ascii & $3F)`), cursor advance, and automatic scrolling.
- **CR** — carriage return (cursor to next row).
- **AT** — absolute cursor positioning (col row).
- **C@/C!** — direct video RAM reads and writes ($0400–$05FF) for the cursor block and screen clearing.
- **IF/ELSE/THEN** — nested conditionals for key dispatch.
- **DO/LOOP, I** — screen clearing loop.
- **HALT** — clean program exit.

The cursor implementation shows how to do read-modify-restore on video RAM: save the character under the cursor, draw a block (`$EF`), and restore the original character when the cursor moves.

## Controls

| Key | Action |
|-----|--------|
| Printable keys | Echo character at cursor |
| ENTER | Move to next row |
| CLEAR | Clear screen and home cursor |
| LEFT | Backspace (move left + erase) |
| RIGHT | Move cursor right |
| UP / DOWN | Move cursor up/down one row |
| BREAK | Halt (exit program) |

## Also Included: kbdtest

`kbdtest.fs` is a minimal keyboard diagnostic — press any key and it echoes the character followed by its hex ASCII code (e.g., `A=41 B=42`). Useful for verifying that all keys in the matrix scan map correctly, especially after kernel changes to KEY or MATRIX2ASCII.

## Build

```sh
# Build the kernel (if not already built)
cd forth/kernel && make

# Compile and run the typewriter
cd src/typewriter && make run
```
