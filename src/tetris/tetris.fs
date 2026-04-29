\ src/tetris/tetris.fs — Bare Naked Tetris for the TRS-80 Color Computer
\
\ SG4 semigraphics Tetris.  Board: 10 wide x 16 tall, one character
\ cell per block.  Info panel at columns 13-31.
\
\ Controls:  LEFT/RIGHT = move, UP = rotate, DOWN = soft drop,
\            SPACE = hard drop.
\
\ Board state array at $5000 (16-byte stride x 16 rows = 256 bytes).
\ Piece rotation table at $5100 (7 pieces x 4 rotations x 4 bytes).
\ High score table at $5200 (5 entries x 2 bytes = 10 bytes).
\
\ Rendering: zero-flicker "draw first, clean stale" approach.
\ Text uses normal video (green on black) via vemit instead of EMIT.
\
\ Build:   make
\ Load:    LOADM"TETRIS":EXEC

\ ── Shared library ──────────────────────────────────────────────────────────

INCLUDE ../../forth/lib/rng.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/print.fs
INCLUDE ../../forth/lib/datawrite.fs

\ ── Normal-video text output ────────────────────────────────────────────────
\ EMIT writes inverse ($40|char) = dark on green.  vemit writes normal
\ ($3F AND char) = green on black.  All in-game text uses vemit so the
\ background stays black.

: vemit  ( char -- )
  $3F AND
  KVAR-CUR @ $0400 + C!
  KVAR-CUR @ 1 + KVAR-CUR ! ;

: vu.  ( u -- )  10 /MOD ?dup IF vu. THEN  CHAR 0 + vemit ;

\ ── Game variables ──────────────────────────────────────────────────────────

VARIABLE px        \ current piece column
VARIABLE py        \ current piece row
VARIABLE pr        \ current rotation (0-3)
VARIABLE pp        \ current piece type (0-6)
VARIABLE np        \ next piece type (0-6)
VARIABLE score     \ score
VARIABLE lns       \ lines cleared total
VARIABLE grav      \ frames per gravity drop
VARIABLE gcnt      \ gravity counter (counts down)
VARIABLE locked    \ piece-just-locked flag
VARIABLE go-flag   \ game-over flag

VARIABLE ck-ok     \ collision check result

VARIABLE dc-tmp    \ color for draw-cell
VARIABLE sv-r      \ saved row index for nested loops
VARIABLE full-f    \ row-full? flag
VARIABLE clr-n     \ lines cleared this lock
VARIABLE src-a     \ copy-row-down source address
VARIABLE dst-a     \ copy-row-down dest address
VARIABLE p-a       \ piece address temp
VARIABLE key-v     \ current key value
VARIABLE prev-k    \ previous key for repeat handling
VARIABLE key-t     \ key repeat timer

\ Flicker-free rendering: old ("dirty") and new VRAM addresses
VARIABLE d0  VARIABLE d1  VARIABLE d2  VARIABLE d3
VARIABLE n0  VARIABLE n1  VARIABLE n2  VARIABLE n3
VARIABLE sg-byte   \ precomputed SG4 byte for current piece

\ ── Piece color table ─────────────────────────────────────────────────────
\ 7 bytes at $5210.  Maps piece index (0-6) to SG4 color, skipping
\ buff (4) which looks white.  Uses green (0) for J instead.

: init-colors  ( -- )
  1 $5210 C!     \ I = yellow
  2 $5211 C!     \ O = blue
  3 $5212 C!     \ T = red
  5 $5213 C!     \ S = cyan
  6 $5214 C!     \ Z = magenta
  7 $5215 C!     \ L = orange
  3 $5216 C! ;   \ J = red

: piece-color  ( idx -- color )  $5210 + C@ ;

\ ── Piece table ─────────────────────────────────────────────────────────────
\ 7 pieces x 4 rotations x 4 bytes = 112 bytes at $5100.
\ Each byte encodes a block offset: byte = dx*4 + dy.
\ Decode: byte 4 /MOD  ( -- dy dx )  — dx on top, ready for px @ +
\ Piece colors: piece_index + 1 (1=yellow .. 7=orange).

: init-pieces
  $5100 tp !
  \ Piece 0: I
  0 tb 4 tb 8 tb 12 tb     \ R0: ####
  0 tb 1 tb 2 tb 3 tb      \ R1: vertical
  0 tb 4 tb 8 tb 12 tb     \ R2
  0 tb 1 tb 2 tb 3 tb      \ R3
  \ Piece 1: O
  0 tb 4 tb 1 tb 5 tb      \ R0: ##
  0 tb 4 tb 1 tb 5 tb      \ R1: ##
  0 tb 4 tb 1 tb 5 tb      \ R2
  0 tb 4 tb 1 tb 5 tb      \ R3
  \ Piece 2: T
  0 tb 4 tb 8 tb 5 tb      \ R0: ### with nub down
  0 tb 1 tb 5 tb 2 tb      \ R1: nub right
  4 tb 1 tb 5 tb 9 tb      \ R2: nub up
  4 tb 1 tb 5 tb 6 tb      \ R3: nub left
  \ Piece 3: S
  4 tb 8 tb 1 tb 5 tb      \ R0: .## / ##.
  0 tb 1 tb 5 tb 6 tb      \ R1: vertical
  4 tb 8 tb 1 tb 5 tb      \ R2
  0 tb 1 tb 5 tb 6 tb      \ R3
  \ Piece 4: Z
  0 tb 4 tb 5 tb 9 tb      \ R0: ##. / .##
  4 tb 1 tb 5 tb 2 tb      \ R1: vertical
  0 tb 4 tb 5 tb 9 tb      \ R2
  4 tb 1 tb 5 tb 2 tb      \ R3
  \ Piece 5: L
  0 tb 1 tb 2 tb 6 tb      \ R0: #. / #. / ##
  0 tb 4 tb 8 tb 1 tb      \ R1: ### / #..
  0 tb 4 tb 5 tb 6 tb      \ R2: ## / .# / .#
  8 tb 1 tb 5 tb 9 tb      \ R3: ..# / ###
  \ Piece 6: J
  4 tb 5 tb 2 tb 6 tb      \ R0: .# / .# / ##
  0 tb 1 tb 5 tb 9 tb      \ R1: #.. / ###
  0 tb 4 tb 1 tb 2 tb      \ R2: ## / #. / #.
  0 tb 4 tb 8 tb 9 tb ;    \ R3: ### / ..#

\ ── Board operations ────────────────────────────────────────────────────────

: board-addr  ( col row -- addr )  16 * + $5000 + ;
: clear-board  ( -- )  256 0 DO  0 $5000 I + C!  LOOP ;

\ ── Piece data access ───────────────────────────────────────────────────────

: piece-addr  ( -- addr )  pp @ 16 * pr @ 4 * + $5100 + ;

\ ── Random piece (0-6) ──────────────────────────────────────────────────────

: rnd7  ( -- 0..6 )  BEGIN  8 rnd  DUP 7 <  UNTIL ;

\ ── VRAM cell operations ────────────────────────────────────────────────────
\ Board is drawn at VRAM columns 1-10 (offset +1 from board coords).
\ Each SG4 cell: $80 | (color<<4) | $0F (all 4 sub-pixels lit).
\ Empty cell: $80 (black, no sub-pixels).

: draw-cell  ( col row -- )
  32 * + $0400 +
  dc-tmp @ 16 * $8F OR
  SWAP C! ;

: erase-cell  ( col row -- )
  32 * + $0400 + $80 SWAP C! ;

\ ── Collision detection ─────────────────────────────────────────────────────

: check1  ( byte -- )
  4 /MOD                  \ ( dy dx )
  px @ +                  \ ( dy col )
  SWAP py @ +             \ ( col row )
  OVER 0 < IF  0 ck-ok !  DROP DROP EXIT  THEN
  OVER 9 > IF  0 ck-ok !  DROP DROP EXIT  THEN
  DUP 0  < IF  0 ck-ok !  DROP DROP EXIT  THEN
  DUP 15 > IF  0 ck-ok !  DROP DROP EXIT  THEN
  board-addr C@ IF  0 ck-ok !  THEN ;

: can-place?  ( -- flag )
  1 ck-ok !
  piece-addr
  DUP     C@ check1
  DUP 1 + C@ check1
  DUP 2 + C@ check1
  3 +     C@ check1
  ck-ok @ ;

\ ── Piece VRAM address calculation ─────────────────────────────────────────

: block-vram  ( byte -- addr )
  4 /MOD                  \ ( dy dx )
  px @ + 1 +             \ ( dy  vram-col )
  SWAP py @ +             \ ( vram-col  row )
  32 * + $0400 + ;

\ ── Zero-flicker rendering ─────────────────────────────────────────────────

: save-dirty  ( -- )
  piece-addr p-a !
  p-a @     C@ block-vram d0 !
  p-a @ 1 + C@ block-vram d1 !
  p-a @ 2 + C@ block-vram d2 !
  p-a @ 3 + C@ block-vram d3 ! ;

: save-new  ( -- )
  piece-addr p-a !
  p-a @     C@ block-vram n0 !
  p-a @ 1 + C@ block-vram n1 !
  p-a @ 2 + C@ block-vram n2 !
  p-a @ 3 + C@ block-vram n3 !
  pp @ piece-color 16 * $8F OR sg-byte ! ;

: draw-new  ( -- )
  sg-byte @ n0 @ C!
  sg-byte @ n1 @ C!
  sg-byte @ n2 @ C!
  sg-byte @ n3 @ C! ;

: is-new?  ( addr -- flag )
  DUP n0 @ = IF DROP -1 EXIT THEN
  DUP n1 @ = IF DROP -1 EXIT THEN
  DUP n2 @ = IF DROP -1 EXIT THEN
      n3 @ = ;

: clean-dirty  ( -- )
  d0 @ is-new? 0= IF $80 d0 @ C! THEN
  d1 @ is-new? 0= IF $80 d1 @ C! THEN
  d2 @ is-new? 0= IF $80 d2 @ C! THEN
  d3 @ is-new? 0= IF $80 d3 @ C! THEN ;

: snap-new-to-dirty  ( -- )
  n0 @ d0 !  n1 @ d1 !  n2 @ d2 !  n3 @ d3 ! ;

\ ── draw-piece: used for initial draw and after spawn ──────────────────────

: draw-piece  ( -- )
  pp @ piece-color dc-tmp !
  piece-addr p-a !
  4 0 DO
    p-a @ I + C@ 4 /MOD
    px @ + 1 + SWAP py @ +
    draw-cell
  LOOP ;

\ ── Board rendering (full redraw from array) ───────────────────────────────

: redraw-board  ( -- )
  16 0 DO
    I sv-r !
    10 0 DO
      I sv-r @ board-addr C@
      DUP 0= IF
        DROP  I 1 + sv-r @ erase-cell
      ELSE
        dc-tmp !  I 1 + sv-r @ draw-cell
      THEN
    LOOP
  LOOP ;

\ ── Lock piece into board array ─────────────────────────────────────────────

: lock-piece  ( -- )
  piece-addr p-a !
  4 0 DO
    p-a @ I + C@ 4 /MOD
    px @ + SWAP py @ +
    board-addr  pp @ piece-color SWAP C!
  LOOP ;

\ ── Line clearing ──────────────────────────────────────────────────────────

: row-full?  ( row -- flag )
  1 full-f !
  16 * $5000 +
  10 0 DO
    DUP I + C@ 0= IF  0 full-f !  THEN
  LOOP
  DROP full-f @ ;

: copy-row-down  ( row -- )
  DUP 16 * $5000 + dst-a !
  1 - 16 * $5000 + src-a !
  10 0 DO
    src-a @ I + C@  dst-a @ I + C!
  LOOP ;

: clear-top  ( -- )  10 0 DO  0 $5000 I + C!  LOOP ;

: remove-row  ( row -- )
  BEGIN
    DUP 0 > IF
      DUP copy-row-down  1 -  0
    ELSE
      DROP clear-top  1
    THEN
  UNTIL ;

: clear-lines  ( -- )
  0 clr-n !
  BEGIN
    0
    16 0 DO
      I row-full? IF
        I remove-row
        clr-n @ 1 + clr-n !
        DROP 1
      THEN
    LOOP
    0=
  UNTIL ;

\ ── Scoring ─────────────────────────────────────────────────────────────────

: calc-grav  ( -- )
  30 lns @ 5 /MOD SWAP DROP -
  DUP 3 < IF DROP 3 THEN
  grav ! ;

: update-score  ( -- )
  clr-n @ 0= IF EXIT THEN
  100 clr-n @ * score @ + score !
  clr-n @ lns @ + lns !
  calc-grav ;

\ ── Text display ────────────────────────────────────────────────────────────

: .6sp  ( -- )
  32 vemit 32 vemit 32 vemit 32 vemit 32 vemit 32 vemit ;

: show-score  ( -- )
  13 3 AT  83 vemit 67 vemit 79 vemit 82 vemit 69 vemit      \ SCORE
  13 4 AT  .6sp
  13 4 AT  score @ vu. ;

: show-lines  ( -- )
  13 6 AT  76 vemit 73 vemit 78 vemit 69 vemit 83 vemit      \ LINES
  13 7 AT  .6sp
  13 7 AT  lns @ vu. ;

\ ── High scores ─────────────────────────────────────────────────────────────
\ 5 entries at $5200 (16-bit each).  Persists across restarts.

: init-high  ( -- )
  0 $5200 !  0 $5202 !  0 $5204 !  0 $5206 !  0 $5208 ! ;

: insert-high  ( -- )
  score @ $5200 @ > IF
    $5206 @ $5208 !  $5204 @ $5206 !  $5202 @ $5204 !  $5200 @ $5202 !
    score @ $5200 !  EXIT
  THEN
  score @ $5202 @ > IF
    $5206 @ $5208 !  $5204 @ $5206 !  $5202 @ $5204 !
    score @ $5202 !  EXIT
  THEN
  score @ $5204 @ > IF
    $5206 @ $5208 !  $5204 @ $5206 !
    score @ $5204 !  EXIT
  THEN
  score @ $5206 @ > IF
    $5206 @ $5208 !
    score @ $5206 !  EXIT
  THEN
  score @ $5208 @ > IF
    score @ $5208 !
  THEN ;

: show-high  ( -- )
  24 9 AT   72 vemit 73 vemit 71 vemit 72 vemit                       \ HIGH
  23 10 AT  83 vemit 67 vemit 79 vemit 82 vemit 69 vemit 83 vemit    \ SCORES
  23 11 AT  CHAR 1 vemit CHAR ) vemit .6sp  25 11 AT  $5200 @ vu.
  23 12 AT  CHAR 2 vemit CHAR ) vemit .6sp  25 12 AT  $5202 @ vu.
  23 13 AT  CHAR 3 vemit CHAR ) vemit .6sp  25 13 AT  $5204 @ vu.
  23 14 AT  CHAR 4 vemit CHAR ) vemit .6sp  25 14 AT  $5206 @ vu.
  23 15 AT  CHAR 5 vemit CHAR ) vemit .6sp  25 15 AT  $5208 @ vu. ;

\ ── Border ──────────────────────────────────────────────────────────────────

: draw-border  ( -- )
  16 0 DO
    $CF $0400      I 32 * + C!
    $CF $0400 11 + I 32 * + C!
  LOOP ;

\ ── Next piece preview ──────────────────────────────────────────────────────
\ 4x4 preview area at VRAM (14, 10).

: clear-next  ( -- )
  4 0 DO
    $80 $054E I + C!
    $80 $056E I + C!
    $80 $058E I + C!
    $80 $05AE I + C!
  LOOP ;

: draw-next  ( -- )
  clear-next
  np @ piece-color dc-tmp !
  np @ 16 * $5100 + p-a !
  4 0 DO
    p-a @ I + C@ 4 /MOD
    14 + SWAP 10 +
    draw-cell
  LOOP ;

\ ── Info panel ──────────────────────────────────────────────────────────────

: draw-panel  ( -- )
  13 0 AT
  66 vemit 65 vemit 82 vemit 69 vemit 32 vemit                       \ BARE_
  78 vemit 65 vemit 75 vemit 69 vemit 68 vemit                       \ NAKED
  13 1 AT  84 vemit 69 vemit 84 vemit 82 vemit 73 vemit 83 vemit    \ TETRIS
  show-score  show-lines
  13 9 AT  78 vemit 69 vemit 88 vemit 84 vemit                       \ NEXT
  draw-next
  draw-border
  show-high ;

\ ── Spawn piece ─────────────────────────────────────────────────────────────

: spawn  ( -- )
  np @ pp !
  rnd7 np !
  3 px !  0 py !  0 pr !
  can-place? 0= IF  1 go-flag !  THEN ;

\ ── Movement ────────────────────────────────────────────────────────────────

: do-left  ( -- )
  px @ 1 - px !
  can-place? 0= IF  px @ 1 + px !  THEN ;

: do-right  ( -- )
  px @ 1 + px !
  can-place? 0= IF  px @ 1 - px !  THEN ;

: do-down  ( -- )
  py @ 1 + py !
  can-place? 0= IF  py @ 1 - py !  THEN
  grav @ gcnt ! ;

: do-rotate  ( -- )
  pr @ 1 + 3 AND pr !
  can-place? 0= IF  pr @ 1 - 3 AND pr !  THEN ;

: do-hdrop  ( -- )
  BEGIN
    py @ 1 + py !
    can-place? 0=
  UNTIL
  py @ 1 - py !
  1 locked ! ;

\ ── Gravity ─────────────────────────────────────────────────────────────────

: do-gravity  ( -- )
  gcnt @ 1 - DUP gcnt !
  IF EXIT THEN
  grav @ gcnt !
  py @ 1 + py !
  can-place? 0= IF
    py @ 1 - py !
    1 locked !
  THEN ;

\ ── Input ───────────────────────────────────────────────────────────────────

: do-dispatch  ( key -- )
  DUP $1E = IF DROP do-left    EXIT THEN
  DUP $1F = IF DROP do-right   EXIT THEN
  DUP $1D = IF DROP do-down    EXIT THEN
  DUP $1C = IF DROP do-rotate  EXIT THEN
      $20 = IF      do-hdrop        THEN ;

: poll-input  ( -- )
  KEY? key-v !
  key-v @ 0= IF  0 prev-k !  EXIT  THEN
  key-v @ prev-k @ = IF
    key-t @ 1 - key-t !
    key-t @ 0= 0= IF EXIT THEN
    4 key-t !
  ELSE
    key-v @ prev-k !
    12 key-t !
  THEN
  key-v @ do-dispatch ;

\ ── Title screen ────────────────────────────────────────────────────────────

: .title  ( -- )
  11 5 AT
  66 vemit 65 vemit 82 vemit 69 vemit 32 vemit                       \ BARE_
  78 vemit 65 vemit 75 vemit 69 vemit 68 vemit                       \ NAKED
  13 7 AT
  84 vemit 69 vemit 84 vemit 82 vemit 73 vemit 83 vemit             \ TETRIS
  9 10 AT
  80 vemit 82 vemit 69 vemit 83 vemit 83 vemit 32 vemit             \ PRESS_
  65 vemit 78 vemit 89 vemit 32 vemit 75 vemit 69 vemit             \ ANY KE
  89 vemit ;                                                          \ Y

\ ── Game over screen ────────────────────────────────────────────────────────

: .game-over  ( -- )
  13 11 AT  71 vemit 65 vemit 77 vemit 69 vemit                      \ GAME
  13 12 AT  79 vemit 86 vemit 69 vemit 82 vemit                      \ OVER
  13 14 AT
  80 vemit 82 vemit 69 vemit 83 vemit 83 vemit                      \ PRESS
  13 15 AT
  65 vemit 78 vemit 89 vemit 32 vemit 75 vemit 69 vemit 89 vemit ; \ ANY KEY

\ ── Main ────────────────────────────────────────────────────────────────────

: tetris  ( -- )
  init-pieces
  init-colors
  init-high

  \ Title screen — seed RNG by counting vsync frames until keypress
  cls-black  .title
  0 seed !
  BEGIN  vsync  seed @ 1 + seed !  KEY?  UNTIL
  BEGIN  vsync  KEY? 0=  UNTIL

  BEGIN  \ ── restart loop ──

  \ Initialize game
  clear-board  cls-black
  0 score !  0 lns !  30 grav !  grav @ gcnt !
  0 locked !  0 go-flag !  0 prev-k !
  rnd7 np !  spawn
  draw-panel  draw-piece  save-dirty

  \ Game loop
  BEGIN
    poll-input
    do-gravity
    locked @ IF
      lock-piece  clear-lines  update-score
      vsync
      redraw-board  draw-border
      show-score  show-lines
      0 locked !
      spawn
      draw-next
      go-flag @ 0= IF
        draw-piece  save-dirty
      THEN
    ELSE
      save-new
      vsync
      go-flag @ 0= IF
        draw-new  clean-dirty  snap-new-to-dirty
      THEN
    THEN
    go-flag @
  UNTIL

  \ Game over
  insert-high
  .game-over
  show-high
  BEGIN  vsync  KEY? 0=  UNTIL
  BEGIN  vsync  KEY?     UNTIL
  BEGIN  vsync  KEY? 0=  UNTIL

  AGAIN ;  \ restart

tetris HALT
