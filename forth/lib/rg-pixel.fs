\ rg-pixel.fs — RG6 artifact-color pixel primitives
\
\ Provides: rg-init, rg-pcls, rg-pset (kernel), rg-pget, rg-hline,
\           rg-line (kernel)
\ Requires: kernel primitives, vdg.fs (set-sam-v, set-sam-f, set-pia)
\
\ rg-pset and rg-line are kernel CODE words.
\ rg-pget, rg-hline, rg-addr are Forth words defined here.

VARIABLE rv                       \ VRAM base address

\ ── Lookup tables ─────────────────────────────────────────────────────────
$8728 CONSTANT COLTAB
$872C CONSTANT SHFTAB
$8730 CONSTANT MSKTAB

: init-tables  ( -- )
  0 COLTAB     C!  1 COLTAB 1 + C!  2 COLTAB 2 + C!  3 COLTAB 3 + C!
  6 SHFTAB     C!  4 SHFTAB 1 + C!  2 SHFTAB 2 + C!  0 SHFTAB 3 + C!
  $3F MSKTAB     C!  $CF MSKTAB 1 + C!  $F3 MSKTAB 2 + C!  $FC MSKTAB 3 + C! ;

\ rg-init-at points the VDG at an arbitrary $0200-aligned VRAM base
\ and clears 6K of pixel memory.  All-RAM builds use $0600 (rg-init);
\ ROM-mode builds need a high base (e.g. $6000 on 32K) so the kernel
\ at $1000 isn't smashed.
: rg-init-at  ( base -- )
  init-tables
  DUP rv !  KVAR-RGVRAM !
  6 set-sam-v
  rv @ 9 RSHIFT set-sam-f
  $F8 set-pia
  rv @ 6144 0 FILL ;

: rg-init  ( -- )  $0600 rg-init-at ;

: rg-pcls  ( -- )  rv @ 6144 0 FILL ;

VARIABLE pa
VARIABLE ps

: rg-addr  ( x y -- )
  32 * rv @ + SWAP
  DUP 3 AND ps !
  2 RSHIFT + pa ! ;

\ rg-pset ( x y color -- )  — kernel primitive

: rg-pget  ( x y -- raw )
  rg-addr
  pa @ C@
  ps @ SHFTAB + C@ RSHIFT
  3 AND ;

VARIABLE hl-c  VARIABLE hl-y

: rg-hline  ( x1 x2 y color -- )
  hl-c ! hl-y !
  1 + SWAP
  DO  I hl-y @ hl-c @ rg-pset  LOOP ;

\ Temp variables used by rg-line (kernel) — kept for other uses
VARIABLE tp

\ rg-line ( x1 y1 x2 y2 color -- )  — kernel primitive
