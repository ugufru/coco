\ cg-text.fs — CG mode text rendering (2 bits/pixel)
\
\ Provides: init-expand, expand-hi, expand-lo, cg-char
\ Requires: glyph-addr (from font5x7.fs),
\           cv, cb (from rg-text.fs),
\           tp, tb (from datawrite.fs),
\           kernel primitives RSHIFT, AND, IF, ELSE, THEN,
\           *, +, @, !, C@, C!, SWAP, OVER, DROP, DO, LOOP, I
\
\ Renders 5x7 font glyphs into CG-mode VRAM by expanding each font bit
\ to a 2-bit CG pixel.  Uses a 16-entry lookup table (4 font bits → 1 CG
\ byte) built at runtime by init-expand.
\
\ Before calling cg-char, set cv to the VRAM base, cb to bytes-per-row,
\ and call init-expand once to build the lookup table.
\
\ Usage:
\   INCLUDE lib/font5x7.fs
\   INCLUDE lib/datawrite.fs
\   INCLUDE lib/rg-text.fs
\   INCLUDE lib/cg-text.fs
\   init-font  init-expand
\   $3000 cv !  16 cb !
\   CHAR A 2 1 cg-char

$7CE0 CONSTANT EXTAB              \ CG expand table (16 bytes, high RAM)

VARIABLE ftmp                     \ font byte temp

: init-expand
  EXTAB tp !
  $00 tb $03 tb $0C tb $0F tb
  $30 tb $33 tb $3C tb $3F tb
  $C0 tb $C3 tb $CC tb $CF tb
  $F0 tb $F3 tb $FC tb $FF tb ;

: expand-hi  ( byte -- cg-byte )  4 RSHIFT EXTAB + C@ ;
: expand-lo  ( byte -- cg-byte )  $08 AND IF $C0 ELSE 0 THEN ;

: cg-char  ( char cx cy -- )
  8 * cb @ * SWAP 2 * + cv @ +   \ dest = vram + cy*8*bpr + cx*2
  SWAP glyph-addr SWAP            \ ( glyph dest )
  7 0 DO
    OVER I + C@ ftmp !            \ save font byte
    ftmp @ expand-hi
    OVER I cb @ * + C!             \ write CG byte 1
    ftmp @ expand-lo
    OVER I cb @ * + 1 + C!        \ write CG byte 2
  LOOP DROP DROP ;
