\ src/rain/rain.fs — Digital Rain (Matrix effect) for the TRS-80 Color Computer
\
\ Green-on-black VDG text mode creates the classic Matrix aesthetic
\ naturally.  32 independent columns of falling characters, each at
\ its own speed, with bright inverse-video heads and dimmer normal
\ trails.
\
\ Press any key on title screen to start.  Close XRoar to exit.
\
\ Build:   make
\ Load:    LOADM"RAIN":EXEC

\ ── Shared libraries ──────────────────────────────────────────────

INCLUDE ../../forth/lib/rng.fs
INCLUDE ../../forth/lib/screen.fs

\ ── Normal-video text output ──────────────────────────────────────
\ vemit: green-on-black text (normal video).  EMIT writes inverse.

: vemit  ( char -- )
  $3F AND
  KVAR-CUR @ $0400 + C!
  KVAR-CUR @ 1 + KVAR-CUR ! ;

\ ── Constants ─────────────────────────────────────────────────────

\ Per-column state arrays (32 entries x 2 bytes each)
\ heads:  row of bright head character ($FF = inactive)
\ tails:  row of erasing tail
\ speeds: frames between advances (2-5)
\ timers: countdown to next advance

\ $4000 = heads   (64 bytes)
\ $4040 = tails   (64 bytes)
\ $4080 = speeds  (64 bytes)
\ $40C0 = timers  (64 bytes)

\ ── Array access ──────────────────────────────────────────────────

: heads   ( col -- addr )  2 * $4000 + ;
: tails   ( col -- addr )  2 * $4040 + ;
: speeds  ( col -- addr )  2 * $4080 + ;
: timers  ( col -- addr )  2 * $40C0 + ;

\ ── Helpers ───────────────────────────────────────────────────────

\ Screen address from column and row
: scr-addr  ( col row -- addr )  32 * + $0400 + ;

\ Random letter A-Z: 32 rnd gives 0..31, reject > 25
: rnd-alpha  ( -- ascii )  BEGIN  32 rnd  DUP 26 < UNTIL  CHAR A + ;

\ Inverse video character (bright head)
: inv-char  ( ascii -- vdg )  $3F AND $40 OR ;

\ Normal video character (dim trail)
: nrm-char  ( ascii -- vdg )  $3F AND ;

\ ── Column operations ─────────────────────────────────────────────

VARIABLE col      \ current column being processed

\ Activate a column with random speed and trail offset
: activate  ( col -- )
  DUP heads 0 SWAP !             \ head starts at row 0
  DUP tails                      \ random trail length 4-7
    4 rnd 4 + 0 SWAP - SWAP !    \ tail = -(4..7), negative = off-screen
  DUP speeds
    4 rnd 2 + SWAP !             \ speed = 2..5
  DUP timers
    OVER speeds @ SWAP !         \ timer = speed
  DROP ;

\ Deactivate a column
: deactivate  ( col -- )  heads $FF SWAP ! ;

\ Is column active?
: active?  ( col -- flag )  heads @ $FF = 0= ;

\ ── Update one column ────────────────────────────────────────────

: update-col  ( col -- )
  col !

  \ Decrement timer
  col @ timers DUP @ 1 - SWAP !

  \ Check if timer hit zero
  col @ timers @ 0= IF

    \ Reset timer
    col @ speeds @ col @ timers !

    \ Draw head if on-screen (0-15)
    col @ heads @ DUP 0 < 0= SWAP 16 < AND IF
      \ Write inverse random char at head position
      rnd-alpha inv-char
      col @ col @ heads @ scr-addr C!

      \ Demote previous row to trail (normal video)
      col @ heads @ 0 > IF
        col @ col @ heads @ 1 - scr-addr DUP C@
        \ Only demote if it's an inverse char (has bit 6 set)
        DUP $40 AND IF
          $3F AND SWAP C!
        ELSE
          DROP DROP
        THEN
      THEN
    THEN

    \ Erase tail if on-screen (0-15)
    col @ tails @ DUP 0 < 0= SWAP 16 < AND IF
      $80 col @ col @ tails @ scr-addr C!
    THEN

    \ Advance head and tail
    col @ heads @ 1 + col @ heads !
    col @ tails @ 1 + col @ tails !

    \ Deactivate if tail has left screen
    col @ tails @ 15 > IF
      col @ deactivate
    THEN
  THEN ;

\ ── Trail mutation ────────────────────────────────────────────────
\ Small chance each frame to change a random trail character

: maybe-mutate  ( -- )
  \ ~1/8 chance per frame
  8 rnd 0= IF
    32 rnd                        \ random column
    16 rnd                        \ random row
    scr-addr DUP C@               \ ( addr byte )
    \ Only mutate if it's a normal-video letter (bit 6 clear, not $80)
    DUP $80 = IF  DROP DROP EXIT  THEN
    DUP $40 AND IF  DROP DROP EXIT  THEN    \ skip inverse (head)
    DROP  rnd-alpha nrm-char SWAP C!
  THEN ;

\ ── Spawn check ───────────────────────────────────────────────────
\ Each frame, try to spawn a column or two

: maybe-spawn  ( -- )
  \ Try 2 random columns per frame
  32 rnd DUP active? 0= IF  activate  ELSE  DROP  THEN
  32 rnd DUP active? 0= IF  activate  ELSE  DROP  THEN ;

\ ── Initialize all columns as inactive ────────────────────────────

: init-cols  ( -- )
  32 0 DO  I deactivate  LOOP ;

\ ── Title screen ──────────────────────────────────────────────────

: .title  ( -- )
  10 5 AT
  68 vemit 73 vemit 71 vemit 73 vemit 84 vemit 65 vemit 76 vemit   \ DIGITAL
  32 vemit
  82 vemit 65 vemit 73 vemit 78 vemit                               \ RAIN
  9 9 AT
  80 vemit 82 vemit 69 vemit 83 vemit 83 vemit 32 vemit            \ PRESS_
  65 vemit 78 vemit 89 vemit 32 vemit 75 vemit 69 vemit            \ ANY KE
  89 vemit ;                                                         \ Y

\ ── Main ──────────────────────────────────────────────────────────

: rain  ( -- )
  \ Title screen — seed RNG by counting vsync frames until keypress
  cls-black  .title
  0 seed !
  BEGIN  vsync  seed @ 1 + seed !  KEY?  UNTIL
  BEGIN  vsync  KEY? 0=  UNTIL

  \ Start rain
  cls-black
  init-cols

  \ Main loop
  BEGIN
    $FF $FFD7 C!                  \ SAM double speed (~1.78 MHz)

    \ Update all 32 columns
    32 0 DO  I update-col  LOOP

    \ Trail mutations and spawning
    maybe-mutate
    maybe-mutate
    maybe-spawn

    $FF $FFD6 C!                  \ SAM normal speed (display refresh)
    vsync
  0 UNTIL ;

rain
