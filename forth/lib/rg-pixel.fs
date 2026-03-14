\ rg-pixel.fs — RG6 artifact-color pixel primitives
\
\ Provides: rg-init, rg-pcls, rg-pset, rg-pget, rg-hline
\ Requires: kernel primitives AND, OR, C@, C!, LSHIFT, RSHIFT, FILL,
\           *, +, -, DUP, DROP, SWAP, OVER, @, !, DO, LOOP, I
\           vdg.fs (set-sam-v, set-sam-f, set-pia)
\
\ RG6 mode is 256x192, 1 bit/pixel, 6144 bytes VRAM, 32 bytes/row.
\ On NTSC composite, adjacent pixel pairs produce artifact colors:
\
\   Bit pair   Artifact color
\   --------   --------------
\     00       Black
\     11       White
\     10       Blue
\     01       Red/orange
\
\ This library addresses artifact-color pixels at 128x192 resolution.
\ x ranges 0-127, y ranges 0-191.  Each artifact pixel is a 2-bit pair
\ in VRAM.  Color parameter: 0=black, 1=blue, 2=red, 3=white.
\
\ VRAM base: $0E00 (SAM F offset 7).  Leaves $0400-$0DFF free for
\ text VRAM and variables.
\
\ Byte layout (8 real pixels = 4 artifact pixels per byte):
\   bits 7-6 = artifact pixel 0 (leftmost)
\   bits 5-4 = artifact pixel 1
\   bits 3-2 = artifact pixel 2
\   bits 1-0 = artifact pixel 3 (rightmost)
\
\ Color encoding per artifact pixel (2 bits):
\   0 (black) = 00    1 (blue)  = 10
\   2 (red)   = 01    3 (white) = 11

VARIABLE rv                       \ VRAM base address

\ ── Lookup tables ─────────────────────────────────────────────────────────
\ Three 4-byte tables in high RAM, built once by rg-init.

$7CD0 CONSTANT COLTAB            \ color index → 2-bit pattern
$7CD4 CONSTANT SHFTAB            \ sub-pixel (x%4) → left-shift amount
$7CD8 CONSTANT MSKTAB            \ sub-pixel (x%4) → AND mask to clear

: init-tables  ( -- )
  \ Color table: index 0-3 → bit pair value
  \   0=black(00) 1=blue(10) 2=red(01) 3=white(11)
  0 COLTAB     C!
  2 COLTAB 1 + C!
  1 COLTAB 2 + C!
  3 COLTAB 3 + C!
  \ Shift table: sub-pixel position → left shift count
  \   x%4=0 → 6, x%4=1 → 4, x%4=2 → 2, x%4=3 → 0
  6 SHFTAB     C!
  4 SHFTAB 1 + C!
  2 SHFTAB 2 + C!
  0 SHFTAB 3 + C!
  \ Mask table: sub-pixel position → AND mask (clears that pixel's bits)
  \   x%4=0 → $3F, x%4=1 → $CF, x%4=2 → $F3, x%4=3 → $FC
  $3F MSKTAB     C!
  $CF MSKTAB 1 + C!
  $F3 MSKTAB 2 + C!
  $FC MSKTAB 3 + C! ;

\ ── rg-init ( -- ) ────────────────────────────────────────────────────────
\ Switch to RG6 mode, build lookup tables, clear screen.

: rg-init  ( -- )
  init-tables
  $3000 rv !
  6 set-sam-v                  \ V2:V1:V0 = 110 (RG6)
  rv @ 9 RSHIFT set-sam-f     \ F offset = VRAM / 512
  $F8 set-pia                  \ A*/G=1, GM2=1, GM1=1, GM0=1, CSS=1
  rv @ 6144 0 FILL ;           \ clear to black

\ ── rg-pcls ( -- ) ────────────────────────────────────────────────────────
\ Clear entire screen to black.

: rg-pcls  ( -- )  rv @ 6144 0 FILL ;

\ ── Internal variables for pixel ops ──────────────────────────────────────

VARIABLE pa                    \ VRAM byte address
VARIABLE ps                    \ sub-pixel index (0-3)

\ ── rg-addr ( x y -- ) ───────────────────────────────────────────────────
\ Calculate VRAM byte address and sub-pixel index for artifact pixel at
\ (x, y).  Stores results in pa and ps.  x: 0-127, y: 0-191.

: rg-addr  ( x y -- )
  32 * rv @ + SWAP             \ ( row-addr x )
  DUP 3 AND ps !              \ ps = x % 4
  2 RSHIFT + pa ! ;            \ pa = row-addr + x/4

\ ── rg-pset ( x y color -- ) ─────────────────────────────────────────────
\ Plot an artifact-color pixel.  color: 0=black, 1=blue, 2=red, 3=white.

VARIABLE pc                    \ pixel color

: rg-pset  ( x y color -- )
  pc !                         \ save color
  rg-addr                      \ sets pa, ps from x y
  pc @ COLTAB + C@             \ ( colbits )
  ps @ SHFTAB + C@ LSHIFT     \ ( shifted-color )
  pa @ C@                      \ ( shifted-color byte )
  ps @ MSKTAB + C@ AND        \ ( shifted-color masked-byte )
  OR pa @ C! ;                 \ write new byte

\ ── rg-pget ( x y -- color ) ─────────────────────────────────────────────
\ Read the raw 2-bit value at artifact pixel (x, y).
\ Returns: 0=black(00), 1=red(01), 2=blue(10), 3=white(11).
\ Note: raw bit values, not the color indices used by rg-pset.
\ To compare with rg-pset colors, use the inverse mapping:
\   raw 0→color 0, raw 1→color 2, raw 2→color 1, raw 3→color 3.

: rg-pget  ( x y -- raw )
  rg-addr                      \ sets pa, ps
  pa @ C@                      \ ( byte )
  ps @ SHFTAB + C@ RSHIFT     \ shift pixel to bits 1-0
  3 AND ;                      \ mask to 2 bits

\ ── rg-hline ( x1 x2 y color -- ) ────────────────────────────────────────
\ Horizontal line from x1 to x2 (inclusive) at row y.  x1 <= x2.
\ Simple per-pixel loop.  TODO: optimize with full-byte fills for aligned
\ interior spans.

VARIABLE hl-c  VARIABLE hl-y

: rg-hline  ( x1 x2 y color -- )
  hl-c ! hl-y !               \ save color and y
  1 + SWAP                     \ ( x2+1 x1 )
  DO
    I hl-y @ hl-c @ rg-pset
  LOOP ;
