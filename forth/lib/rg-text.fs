\ rg-text.fs — RG mode text rendering (1 bit/pixel)
\
\ Provides: cv (variable), cb (variable), rg-char, rg-type
\ Requires: glyph-addr (from font5x7.fs)
\
\ Renders 5x7 font glyphs into RG-mode VRAM.  Each font byte maps directly
\ to one VRAM byte (5 glyph pixels in bits 7-3, 3 blank pixels in bits 2-0).
\
\ Before calling rg-char/rg-type, set cv to the VRAM base address and cb
\ to the bytes-per-row for the current mode.
\
\ This is the cursor+font5x7 form.  It SHADOWS the kernel's rg-char
\ primitive for callers that INCLUDE rg-text.fs.  Apps that want the
\ fast kernel rg-char (with explicit KVAR-RG* setup) should NOT include
\ rg-text.fs and should call the kernel primitive directly (see clock.fs
\ for the pattern).
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

\ rg-type ( addr len cx cy -- )  type a counted string starting at (cx,cy)
\ and advancing column-by-column.  No cursor wrap or scroll; caller is
\ responsible for keeping (cx+len) inside the screen.

VARIABLE rt-x                     \ start column captured at entry
VARIABLE rt-y                     \ start row captured at entry

: rg-type  ( addr len cx cy -- )
  rt-y !  rt-x !
  0 DO
    DUP I + C@                    \ ( addr char )
    rt-x @ I +                    \ ( addr char cx+I )
    rt-y @                        \ ( addr char cx+I cy )
    rg-char                       \ ( addr )
  LOOP
  DROP ;
