\ rg-text.fs — RG mode text rendering (1 bit/pixel)
\
\ Provides: cv (variable), cb (variable), rg-char
\ Requires: glyph-addr (from font5x7.fs),
\           kernel primitives *, +, @, C@, C!, SWAP, OVER, DROP, DO, LOOP, I
\
\ Renders 5x7 font glyphs into RG-mode VRAM.  Each font byte maps directly
\ to one VRAM byte (5 glyph pixels in bits 7-3, 3 blank pixels in bits 2-0).
\
\ Before calling rg-char, set cv to the VRAM base address and cb to the
\ bytes-per-row for the current mode.
\
\ Usage:
\   INCLUDE lib/font5x7.fs
\   INCLUDE lib/rg-text.fs
\   init-font
\   $3000 cv !  16 cb !           \ RG1: VRAM at $3000, 16 bytes/row
\   CHAR A 2 1 rg-char            \ draw 'A' at column 2, row 1

VARIABLE cv                       \ cached VRAM base address
VARIABLE cb                       \ cached bytes per row

: rg-char  ( char cx cy -- )
  8 * cb @ * SWAP + cv @ +        \ dest = vram + cy*8*bpr + cx
  SWAP glyph-addr SWAP            \ ( glyph dest )
  7 0 DO
    OVER I + C@
    OVER I cb @ * + C!
  LOOP DROP DROP ;
