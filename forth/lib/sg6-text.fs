\ sg6-text.fs — SG6 mode text rendering (block-pixel font)
\
\ Provides: sg6-char, sg6-row
\ Requires: glyph-addr (from font5x7.fs),
\           cv (from rg-text.fs),
\           kernel primitives AND, IF, ELSE, THEN, DUP, +, @, !,
\           C@, C!, DO, LOOP, I, DROP
\
\ SG6 mode sets INT*/EXT=1 on the VDG, which disables the internal
\ character ROM — EMIT produces garbage.  Instead, each font pixel becomes
\ an entire SG6 cell: $BF (solid filled block) for on, $80 (empty) for off.
\ Characters are 5 cells wide × 7 rows tall.
\
\ Before calling sg6-char, set cv to the VRAM base address.
\ SG6 is always 32 bytes/row (64×48 at 512 bytes).
\
\ Usage:
\   INCLUDE lib/font5x7.fs
\   INCLUDE lib/rg-text.fs
\   INCLUDE lib/sg6-text.fs
\   init-font
\   $0400 cv !
\   CHAR A 1 1 sg6-char

VARIABLE sg-g                     \ current glyph address
VARIABLE sg-d                     \ current dest address

: sg6-row  ( font-byte -- )
  5 0 DO
    DUP $80 AND IF $BF ELSE $80 THEN
    sg-d @ I + C!
    DUP +                         \ left-shift by 1
  LOOP DROP
  sg-d @ 32 + sg-d ! ;           \ advance to next screen row

: sg6-char  ( char cx cy -- )
  32 * SWAP + cv @ + sg-d !       \ sg-d = vram + cy*32 + cx
  glyph-addr sg-g !
  7 0 DO
    sg-g @ I + C@ sg6-row
  LOOP ;
