\ sprite.fs — Software sprite library for RG6 artifact-color mode
\
\ Provides: spr-draw, spr-erase, spr-erase-box, spr-w, spr-h
\ Requires: rg-pixel.fs (rg-pset)
\           kernel primitives C@, @, +, -, *, DUP, DROP, SWAP, OVER,
\           RSHIFT, AND, IF, THEN, DO, LOOP, I, !, @
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

\ ── Internal variables ───────────────────────────────────────────────────

VARIABLE sa                    \ sprite row data base address
VARIABLE sx                    \ screen x position (artifact pixels)
VARIABLE sy                    \ screen y position
VARIABLE sw                    \ sprite width
VARIABLE sh                    \ sprite height
VARIABLE sbpr                  \ sprite bytes per row
VARIABLE srow                  \ current row index
VARIABLE scol                  \ current column index
VARIABLE sbyte                 \ current data byte
VARIABLE sbit                  \ bit shift for current pixel
VARIABLE spx                   \ extracted pixel color

\ ── Internal: extract and draw one sprite pixel ──────────────────────────

: spr-pixel  ( -- )
  \ Compute byte address: sa + srow*sbpr + scol/4
  srow @ sbpr @ * sa @ + scol @ 2 RSHIFT + C@ sbyte !
  \ Extract 2-bit color: shift = 6 - (scol%4)*2
  6 scol @ 3 AND 2 * - sbit !
  sbyte @ sbit @ RSHIFT 3 AND spx !
  \ Draw if non-transparent
  spx @ IF
    sx @ scol @ + sy @ srow @ + spx @ rg-pset
  THEN ;

\ ── Internal: erase one sprite pixel to black ────────────────────────────

: spr-epixel  ( -- )
  srow @ sbpr @ * sa @ + scol @ 2 RSHIFT + C@ sbyte !
  6 scol @ 3 AND 2 * - sbit !
  sbyte @ sbit @ RSHIFT 3 AND spx !
  spx @ IF
    sx @ scol @ + sy @ srow @ + 0 rg-pset
  THEN ;

\ ── Internal: set up sprite variables from ( addr x y -- ) ──────────────

: spr-setup  ( addr x y -- )
  sy ! sx !
  DUP spr-w sw !
  DUP spr-h sh !
  2 + sa !
  sw @ 3 + 2 RSHIFT sbpr ! ;

\ ── spr-draw ( addr x y -- ) ─────────────────────────────────────────────
\ Draw sprite at screen position (x, y).  Color 0 pixels are transparent.

: spr-draw  ( addr x y -- )
  spr-setup
  sh @ 0 DO
    I srow !
    sw @ 0 DO
      I scol !
      spr-pixel
    LOOP
  LOOP ;

\ ── spr-erase ( addr x y -- ) ────────────────────────────────────────────
\ Erase sprite at position (x, y) by drawing black over every
\ non-transparent pixel.  Preserves background under transparent pixels.

: spr-erase  ( addr x y -- )
  spr-setup
  sh @ 0 DO
    I srow !
    sw @ 0 DO
      I scol !
      spr-epixel
    LOOP
  LOOP ;

\ ── spr-erase-box ( addr x y -- ) ────────────────────────────────────────
\ Fast erase: fill entire bounding box with black.  Destroys background
\ but is faster than per-pixel erase.  Uses variables to avoid nested J.

VARIABLE ebr                   \ erase box current row

: spr-erase-box  ( addr x y -- )
  spr-setup
  sh @ 0 DO
    I ebr !
    sw @ 0 DO
      sx @ I + sy @ ebr @ + 0 rg-pset
    LOOP
  LOOP ;
