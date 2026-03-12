\ vdg-modes.fs -- VDG Mode Demo
\
\ Cycles through all 11 MC6847 display modes.  Each mode shows a visual
\ pattern (color bars, stripes, checkerboard) plus the mode name rendered
\ in the appropriate way for that mode.  Press any key to advance.
\
\ Modes: Alpha, SG4, SG6, CG1, RG1, CG2, RG2, CG3, RG3, CG6, RG6
\
\ Requires: vdg.fs (mode switching), font5x7.fs (bitmap font)

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/font5x7.fs

\ -- Constants -----------------------------------------------------------

11 CONSTANT NMODES
$5800 CONSTANT MTAB           \ mode table base (11 x 16 = 176 bytes)
$58B0 CONSTANT EXTAB          \ CG expand table (16 bytes)
$3000 CONSTANT GVRAM          \ graphics VRAM (safe: app code < $3000)

\ -- Variables -----------------------------------------------------------

VARIABLE mi                   \ current mode index (0-10)
VARIABLE tp                   \ data write pointer
VARIABLE cv                   \ cached VRAM base
VARIABLE cb                   \ cached bytes per row
VARIABLE qt                   \ quarter-size scratch
VARIABLE ftmp                 \ font byte temp (cg-char)
VARIABLE sg-g                 \ SG6: current glyph address
VARIABLE sg-d                 \ SG6: current dest address

\ -- Data write helpers --------------------------------------------------

: tb  ( byte -- )  tp @ C!  tp @ 1 + tp ! ;
: tw  ( word -- )  tp @ !  tp @ 2 + tp ! ;

\ -- CG nibble-expand table (16 bytes at EXTAB) -------------------------
\ Maps 4 font bits to 1 CG byte.  Each input bit becomes 2 identical bits.

: init-expand
  EXTAB tp !
  $00 tb $03 tb $0C tb $0F tb
  $30 tb $33 tb $3C tb $3F tb
  $C0 tb $C3 tb $CC tb $CF tb
  $F0 tb $F3 tb $FC tb $FF tb ;

\ -- Mode table (11 entries x 16 bytes at MTAB) -------------------------
\
\ Per entry:
\   +0  VRAM base  (word)      +6  mode type (byte)
\   +2  VRAM size  (word)      +7  bytes per row (byte)
\   +4  SAM V      (byte)      +8  name (8 bytes, ASCII, space-padded)
\   +5  PIA bits   (byte)
\
\ Mode types: 0=alpha  1=sg4  2=sg6  3=cg  4=rg

: init-modes
  MTAB tp !

  \ 0: Alpha -- 32x16 text
  $0400 tw 512 tw 0 tb $00 tb 0 tb 32 tb
  CHAR A tb CHAR L tb CHAR P tb CHAR H tb CHAR A tb $20 tb $20 tb $20 tb

  \ 1: SG4 -- 64x32 semigraphics, 8 colors
  $0400 tw 512 tw 0 tb $00 tb 1 tb 32 tb
  CHAR S tb CHAR G tb CHAR 4 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 2: SG6 -- 64x48 semigraphics, 4 colors
  $0400 tw 512 tw 0 tb $10 tb 2 tb 32 tb
  CHAR S tb CHAR G tb CHAR 6 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 3: CG1 -- 64x64, 4 colors, 1024B
  GVRAM tw 1024 tw 1 tb $80 tb 3 tb 16 tb
  CHAR C tb CHAR G tb CHAR 1 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 4: RG1 -- 128x64, 2 colors, 1024B
  GVRAM tw 1024 tw 1 tb $90 tb 4 tb 16 tb
  CHAR R tb CHAR G tb CHAR 1 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 5: CG2 -- 128x64, 4 colors, 1536B
  GVRAM tw 1536 tw 2 tb $A0 tb 3 tb 32 tb
  CHAR C tb CHAR G tb CHAR 2 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 6: RG2 -- 128x96, 2 colors, 1536B
  GVRAM tw 1536 tw 3 tb $B0 tb 4 tb 16 tb
  CHAR R tb CHAR G tb CHAR 2 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 7: CG3 -- 128x96, 4 colors, 3072B
  GVRAM tw 3072 tw 4 tb $C0 tb 3 tb 32 tb
  CHAR C tb CHAR G tb CHAR 3 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 8: RG3 -- 128x192, 2 colors, 3072B
  GVRAM tw 3072 tw 5 tb $D0 tb 4 tb 16 tb
  CHAR R tb CHAR G tb CHAR 3 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 9: CG6 -- 128x192, 4 colors, 6144B
  GVRAM tw 6144 tw 6 tb $E0 tb 3 tb 32 tb
  CHAR C tb CHAR G tb CHAR 6 tb $20 tb $20 tb $20 tb $20 tb $20 tb

  \ 10: RG6 -- 256x192, 2 colors, 6144B
  GVRAM tw 6144 tw 6 tb $F0 tb 4 tb 32 tb
  CHAR R tb CHAR G tb CHAR 6 tb $20 tb $20 tb $20 tb $20 tb $20 tb
;

\ -- Table access --------------------------------------------------------

: me     ( -- addr )  mi @ 16 * MTAB + ;
: m-vram ( -- addr )  me @ ;
: m-size ( -- n )     me 2 + @ ;
: m-samv ( -- n )     me 4 + C@ ;
: m-pia  ( -- n )     me 5 + C@ ;
: m-type ( -- n )     me 6 + C@ ;
: m-bpr  ( -- n )     me 7 + C@ ;
: m-name ( -- addr )  me 8 + ;

\ -- Mode switching ------------------------------------------------------

: cache-mode  ( -- )  m-vram cv !  m-bpr cb ! ;

: switch-hw  ( -- )
  cache-mode
  m-samv set-sam-v
  cv @ 9 RSHIFT set-sam-f
  m-pia set-pia ;

\ -- Text rendering (Alpha / SG4) ---------------------------------------

: emit-name  ( -- )
  m-name 8 0 DO DUP I + C@ EMIT LOOP DROP ;

\ -- Font rendering: RG modes (1 bit/pixel) -----------------------------
\ Font byte maps 1:1 to VRAM byte (5 glyph pixels in bits 7-3).

: rg-char  ( char cx cy -- )
  8 * cb @ * SWAP + cv @ +    \ dest = vram + cy*8*bpr + cx
  SWAP glyph-addr SWAP        \ ( glyph dest )
  7 0 DO
    OVER I + C@
    OVER I cb @ * + C!
  LOOP DROP DROP ;

\ -- Font rendering: CG modes (2 bits/pixel) ----------------------------
\ Expand each font bit to 2 CG bits via lookup table.

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

\ -- Font rendering: SG6 mode -------------------------------------------
\ SG6 disables the internal character ROM (INT*/EXT=1), so EMIT won't
\ display text.  Instead we use whole SG6 cells as block pixels:
\ each font pixel becomes one filled cell ($BF) or empty cell ($80).
\ Characters are 5 cells wide x 7 rows tall.

: sg6-row  ( font-byte -- )
  \ Write 5 cells for one font row.  Scans bits 7-3 (cols 0-4).
  \ sg-d @ points to first cell of this row.
  5 0 DO
    DUP $80 AND IF $BF ELSE $80 THEN
    sg-d @ I + C!
    DUP + \ left-shift by 1 (DUP + = *2)
  LOOP DROP
  sg-d @ 32 + sg-d ! ;         \ advance to next screen row

: sg6-char  ( char cx cy -- )
  32 * SWAP + cv @ + sg-d !    \ sg-d = vram + cy*32 + cx
  glyph-addr sg-g !
  7 0 DO
    sg-g @ I + C@ sg6-row
  LOOP ;

\ -- Name rendering for graphics modes ----------------------------------

: show-name-rg  ( -- )
  m-name
  8 0 DO
    DUP I + C@ DUP $20 <> IF
      I 1 + 1 rg-char
    ELSE DROP THEN
  LOOP DROP ;

: show-name-cg  ( -- )
  m-name
  8 0 DO
    DUP I + C@ DUP $20 <> IF
      I 1 + 1 cg-char
    ELSE DROP THEN
  LOOP DROP ;

: show-name-sg6  ( -- )
  m-name
  8 0 DO
    DUP I + C@ DUP $20 <> IF
      I 6 * 1 + 1 sg6-char     \ 6 cells apart (5 wide + 1 gap), row 1
    ELSE DROP THEN
  LOOP DROP ;

\ -- Display: Alpha mode ------------------------------------------------

: show-alpha  ( -- )
  cv @ 512 $60 FILL           \ green spaces
  0 0 AT emit-name ;

\ -- Display: SG4 mode --------------------------------------------------
\ Text name at top, 8 color bars (rows 8-15) showing all SG4 colors.

: show-sg4  ( -- )
  cv @ 512 $60 FILL
  0 0 AT emit-name
  8 0 DO
    cv @ I 8 + 32 * +         \ addr = vram + (8+I)*32
    32                         \ 1 row
    I 4 LSHIFT $8F OR         \ SG4 full block: color I, all quadrants
    FILL
  LOOP ;

\ -- Display: SG6 mode --------------------------------------------------
\ Alternating color stripes below, block-pixel font name above.

: show-sg6  ( -- )
  \ Clear all 512 bytes to empty SG6 cells
  cv @ 512 $80 FILL
  \ Rows 9-15: alternating color stripes
  7 0 DO
    cv @ I 9 + 32 * +
    32
    I 1 AND IF $FF ELSE $BF THEN
    FILL
  LOOP
  show-name-sg6 ;

\ -- Display: CG modes --------------------------------------------------
\ Four horizontal color bands (one per pixel value), then font name.

: cg-fill  ( color -- byte )
  DUP 2 LSHIFT OR DUP 4 LSHIFT OR ;

: show-cg  ( -- )
  cv @ m-size 0 FILL
  m-size 4 /MOD SWAP DROP qt !
  4 0 DO
    cv @ I qt @ * +
    qt @
    I cg-fill
    FILL
  LOOP
  show-name-cg ;

\ -- Display: RG modes --------------------------------------------------
\ Checkerboard pattern ($AA/$55 alternating rows), then font name.

: show-rg  ( -- )
  cv @ m-size 0 FILL
  m-size cb @ /MOD SWAP DROP
  0 DO
    cv @ I cb @ * +
    cb @
    I 1 AND IF $55 ELSE $AA THEN
    FILL
  LOOP
  show-name-rg ;

\ -- Content dispatcher -------------------------------------------------

: show-content  ( -- )
  m-type 0 = IF show-alpha THEN
  m-type 1 = IF show-sg4   THEN
  m-type 2 = IF show-sg6   THEN
  m-type 3 = IF show-cg    THEN
  m-type 4 = IF show-rg    THEN ;

\ -- Main ----------------------------------------------------------------

: main
  init-font
  init-expand
  init-modes
  0 mi !
  BEGIN
    switch-hw
    show-content
    KEY DROP
    mi @ 1 + NMODES /MOD DROP mi !
  AGAIN ;

main HALT
