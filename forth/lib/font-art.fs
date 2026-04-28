\ font-art.fs — Artifact-safe font for RG6 NTSC display
\
\ Provides: init-font, glyph-addr
\ Requires: kernel primitives CMOVE, font-data
\          fc.py-injected constant: font-base
\
\ 59 glyphs covering ASCII $20-$5A (space, punctuation, 0-9, A-Z).
\ 8 bytes per glyph (7 visible + 1 blank spacer row) — 472 bytes total.
\ Each pixel is a 2-bit artifact pair (11=on, 00=off) — no NTSC fringing.
\ 4th artifact pixel always 00 (inter-character gap).
\
\ Glyph data is stored as raw bytes in the kernel.  init-font copies it
\ to font-base (set by the kernel build: $5800 in ROM mode, $9000 in
\ all-RAM mode).

: glyph-addr  ( char -- addr )
  DUP $20 < IF DROP $20 THEN
  DUP $5A > IF DROP $20 THEN
  $20 - 8 * font-base + ;

: init-font  font-data font-base 472 CMOVE ;
