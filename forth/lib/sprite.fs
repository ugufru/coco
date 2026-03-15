\ sprite.fs — Software sprite library for RG6 artifact-color mode
\
\ Provides: spr-w, spr-h (field accessors)
\           spr-draw, spr-erase-box (kernel assembly primitives)
\
\ Sprite data format (stored in memory):
\   +0  width  (1 byte) — width in artifact pixels (1-128)
\   +1  height (1 byte) — height in rows (1-192)
\   +2..  row data — 2 bits per artifact pixel, 4 pixels per byte
\         Each row is ceil(width/4) bytes, left-aligned in each byte.
\         Pixel order within byte: bits 7-6 = leftmost, bits 1-0 = rightmost.
\         Color 0 (00) = transparent, 1 = blue, 2 = red, 3 = white.

\ ── Sprite field access ──────────────────────────────────────────────────

: spr-w  ( addr -- width )   C@ ;
: spr-h  ( addr -- height )  1 + C@ ;

\ spr-draw ( addr x y -- ) and spr-erase-box ( addr x y -- ) are
\ kernel assembly primitives for performance (~10x faster than ITC).
