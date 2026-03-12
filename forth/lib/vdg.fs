\ vdg.fs — VDG/SAM display mode switching library
\
\ Provides: set-sam-v, set-sam-f, set-pia, reset-text
\ Requires: kernel primitives C@, C!, AND, OR, RSHIFT, DUP, DROP, IF/ELSE/THEN
\
\ The MC6847 VDG has no writable registers — its mode pins are wired to
\ PIA1 port B ($FF22 bits 7-3). The SAM ($FFC0-$FFD3) controls data rate
\ and display offset via paired clear/set addresses.
\
\ Usage example — switch to RG6 (256x192, 2-color, 6144 bytes):
\   $0E00 vram !              \ set VRAM base variable
\   6 set-sam-v               \ V2:V1:V0 = 110
\   vram @ 512 / set-sam-f    \ F bits point at VRAM
\   $F0 set-pia               \ A*/G=1, GM2=1, GM1=1, GM0=1, CSS=0
\   vram @ 6144 0 FILL        \ clear the buffer
\
\ To return to text mode:
\   reset-text

\ ── set-sam-v ( v -- ) ──────────────────────────────────────────────────────
\ Set SAM display mode bits V0-V2. v is 0-7.
\ Each bit has a clear/set address pair starting at $FFC0.

: set-sam-v  ( v -- )
  3 0 DO
    DUP 1 AND
    IF $FFC1 ELSE $FFC0 THEN
    I 2 * + $FF SWAP C!
    1 RSHIFT
  LOOP DROP ;

\ ── set-sam-f ( offset -- ) ─────────────────────────────────────────────────
\ Set SAM display offset bits F0-F6. offset is 0-127.
\ Display start address = offset * 512. Default is 2 ($0400).
\ Each bit has a clear/set address pair starting at $FFC6.

: set-sam-f  ( offset -- )
  7 0 DO
    DUP 1 AND
    IF $FFC7 ELSE $FFC6 THEN
    I 2 * + $FF SWAP C!
    1 RSHIFT
  LOOP DROP ;

\ ── set-pia ( bits -- ) ─────────────────────────────────────────────────────
\ Set PIA1 $FF22 bits 7-3 (A*/G, GM2, GM1, GM0, CSS).
\ Preserves bits 2-0 (RAM size, sound, serial).
\ bits should have the desired value pre-shifted (e.g. $F0 for RG6).

: set-pia  ( bits -- )
  $FF22 C@ $07 AND OR $FF22 C! ;

\ ── reset-text ( -- ) ───────────────────────────────────────────────────────
\ Restore default alpha/SG4 text mode: SAM V=000, offset=2 ($0400),
\ PIA bits 7-3 cleared.

: reset-text  ( -- )
  0 set-sam-v
  2 set-sam-f
  0 set-pia ;
