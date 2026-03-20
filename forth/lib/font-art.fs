\ font-art.fs — Artifact-safe font for RG6 NTSC display
\
\ Provides: init-font, glyph-addr
\ Requires: kernel primitives CMOVE, font-data
\
\ 59 glyphs covering ASCII $20-$5A (space, punctuation, 0-9, A-Z).
\ 8 bytes per glyph (7 visible + 1 blank spacer row).
\ Each pixel is a 2-bit artifact pair (11=on, 00=off) — no NTSC fringing.
\ 4th artifact pixel always 00 (inter-character gap).
\
\ Glyph data is stored as raw bytes in the kernel (472 bytes).
\ init-font copies it to $9000 in all-RAM space.

: glyph-addr  ( char -- addr )
  DUP $20 < IF DROP $20 THEN
  DUP $5A > IF DROP $20 THEN
  $20 - 8 * $9000 + ;

: init-font  font-data $9000 472 CMOVE ;
