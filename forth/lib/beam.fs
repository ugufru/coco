\ beam.fs — Beam pixel-save/restore system for artifact-free rendering
\
\ Provides: beam-trace, beam-draw-slice, beam-restore-slice (kernel primitives)
\ Requires: kernel (VAR_BEAM_BUF, VAR_BEAM_VRAM, VAR_BEAM_CNT,
\           VAR_LINE_*, VAR_RGVRAM), rg-pixel.fs
\
\ Path buffer format: 3 bytes per pixel
\   Offset 0: x  (1 byte, artifact pixel x 0-127)
\   Offset 1: y  (1 byte, screen row y 0-191)
\   Offset 2: c  (1 byte, original 2-bit color in low bits)
\
\ beam-trace ( x1 y1 x2 y2 buf -- count )  — kernel primitive
\ beam-draw-slice ( buf start count color -- )  — kernel primitive
\ beam-restore-slice ( buf start count -- )  — kernel primitive
