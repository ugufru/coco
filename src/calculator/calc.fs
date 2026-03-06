\ src/calculator/calc.fs — Graphical Infix Calculator
\
\ Screen layout (32x16):
\   Row  0     : red SG4 border bar
\   Rows 1-5   : big digit display (5-row yellow SG4 pixel font)
\   Row  6     : red SG4 border bar
\   Rows  7-8  : [7][8][9][/][M+]  — 5-cell buttons, 1-cell gaps, cols 1/7/13/19/25
\   Rows  9-10 : [4][5][6][*][MR]
\   Rows 11-12 : [1][2][3][-]      — 4-button rows, cols 1/7/13/19
\   Rows 13-14 : [0][C][=][+]
\   Row  15    : OP: / MEM: status line

\ ── Text output helpers ───────────────────────────────────────────────────────

: NEGATE   0 SWAP - ;
: ?DUP     DUP IF DUP THEN ;
: U.       10 /MOD ?DUP IF U. THEN  CHAR 0 + EMIT ;
: .        DUP 0 < IF NEGATE CHAR - EMIT THEN  U. ;

\ ── Video RAM primitives ──────────────────────────────────────────────────────

\ VRAW! ( byte col row -- )
: VRAW!   32 * + $0400 + C! ;

\ CLEAR-SCREEN  fill all 16 rows with black ($60 = VDG space)
: CLEAR-SCREEN
  512 0 DO  $20  $0400 I + C!  LOOP ;

\ ── Button renderer ───────────────────────────────────────────────────────────
\
\ BUTTON5  ( color label col row -- )
\ Writes just the label char (inverse-video) at the button centre.
\ color is accepted for call-site compatibility but ignored.

\ INV-BYTE ( char -- vdg_byte )  green char on black background
\ Chars $40+ (A-Z etc): subtract $40 → $00-$3F inverse range
\ Chars $00-$3F (digits, operators): already in range, unchanged
: INV-BYTE  DUP $40 < IF EXIT THEN  $40 - ;

VARIABLE BTN-C
VARIABLE BTN-R

: BUTTON5  ( color label col row -- )
  BTN-R !  BTN-C !      \ save col, row; stack: color label
  SWAP DROP             \ drop color; stack: label
  INV-BYTE
  BTN-C @ 2 +           \ centre of old 5-wide slot
  BTN-R @
  VRAW! ;

\ ── Calculator state ──────────────────────────────────────────────────────────

VARIABLE ACCUM
VARIABLE PREV
VARIABLE OP
VARIABLE MEM
VARIABLE ENTERING

\ ── Big pixel-font digit display ──────────────────────────────────────────────
\
\ Each digit = 3 cols wide x 5 rows tall, in yellow SG4 ($9F).
\ DC/DR = top-left corner of current digit in text-cell coordinates.
\ DPIXT writes one lit pixel; only "on" pixels are drawn.
\ CLEAR-DISPLAY erases rows 1-5 before each redraw.

VARIABLE DC
VARIABLE DR

\ DPIXT ( dc dr -- )  write $BF (red SG4) at text cell (DC+dc, DR+dr)
: DPIXT
  DR @ + 32 *
  SWAP DC @ + +
  $0400 + $BF SWAP C! ;

\ CLEAR-DISPLAY  fill rows 1-5 with black
: CLEAR-DISPLAY
  160 0 DO  $20  $0420 I + C!  LOOP ;

\ ── Digit pixel patterns (only "on" cells listed) ────────────────────────────

: DRAW-0
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
  0 1 DPIXT             2 1 DPIXT
  0 2 DPIXT             2 2 DPIXT
  0 3 DPIXT             2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-1
  1 0 DPIXT
  1 1 DPIXT
  1 2 DPIXT
  1 3 DPIXT
  1 4 DPIXT ;

: DRAW-2
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
                         2 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
  0 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-3
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
                         2 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
                         2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-4
  0 0 DPIXT             2 0 DPIXT
  0 1 DPIXT             2 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
                         2 3 DPIXT
                         2 4 DPIXT ;

: DRAW-5
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
  0 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
                         2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-6
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
  0 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
  0 3 DPIXT             2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-7
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
                         2 1 DPIXT
                         2 2 DPIXT
                         2 3 DPIXT
                         2 4 DPIXT ;

: DRAW-8
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
  0 1 DPIXT             2 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
  0 3 DPIXT             2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-9
  0 0 DPIXT  1 0 DPIXT  2 0 DPIXT
  0 1 DPIXT             2 1 DPIXT
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT
                         2 3 DPIXT
  0 4 DPIXT  1 4 DPIXT  2 4 DPIXT ;

: DRAW-MINUS
  0 2 DPIXT  1 2 DPIXT  2 2 DPIXT ;

: DRAW-DIGIT  ( d -- )
  DUP 0 = IF DROP DRAW-0 EXIT THEN
  DUP 1 = IF DROP DRAW-1 EXIT THEN
  DUP 2 = IF DROP DRAW-2 EXIT THEN
  DUP 3 = IF DROP DRAW-3 EXIT THEN
  DUP 4 = IF DROP DRAW-4 EXIT THEN
  DUP 5 = IF DROP DRAW-5 EXIT THEN
  DUP 6 = IF DROP DRAW-6 EXIT THEN
  DUP 7 = IF DROP DRAW-7 EXIT THEN
  DUP 8 = IF DROP DRAW-8 EXIT THEN
  9 = IF DRAW-9 THEN ;

\ U.GFX ( u -- )  draw unsigned integer using pixel font; advances DC
: U.GFX
  10 /MOD ?DUP IF U.GFX THEN
  DRAW-DIGIT
  DC @ 4 + DC ! ;

\ .GFX ( n -- )  draw signed integer using pixel font
: .GFX
  DUP 0 < IF
    NEGATE
    DRAW-MINUS
    DC @ 4 + DC !
  THEN
  U.GFX ;

\ ── Display update ────────────────────────────────────────────────────────────

: SHOW-NUM
  CLEAR-DISPLAY
  1 DC !  1 DR !
  ACCUM @ .GFX ;


\ ── Calculator logic ──────────────────────────────────────────────────────────

: DO-OP
  OP @ DUP 0 = IF DROP EXIT THEN
  DUP CHAR + = IF DROP  PREV @ ACCUM @ +  ACCUM ! EXIT THEN
  DUP CHAR - = IF DROP  PREV @ ACCUM @ -  ACCUM ! EXIT THEN
  DUP CHAR * = IF DROP  PREV @ ACCUM @ *  ACCUM ! EXIT THEN
  CHAR / = IF
    ACCUM @ 0 <> IF  PREV @ ACCUM @ /MOD SWAP DROP ACCUM !  THEN
  THEN ;

: HANDLE-DIGIT  ( char -- )
  CHAR 0 -
  ENTERING @ IF
    ACCUM @ 10 * + ACCUM !
  ELSE
    ACCUM !  -1 ENTERING !
  THEN ;

: HANDLE-OP  ( char -- )
  ENTERING @ IF  DO-OP  0 ENTERING !  THEN
  ACCUM @ PREV !  OP ! ;

: HANDLE-EQ
  ENTERING @ IF  DO-OP  0 OP !  0 ENTERING !  THEN ;

: HANDLE-CLEAR
  0 ACCUM !  0 PREV !  0 OP !  -1 ENTERING ! ;

: HANDLE-MPLUS   ACCUM @ MEM @ + MEM ! ;
: HANDLE-MR      MEM @ ACCUM !  -1 ENTERING ! ;

: DIGIT?  ( c -- c flag )
  DUP CHAR 0 < IF 0 EXIT THEN
  DUP CHAR 9 > IF 0 EXIT THEN
  -1 ;

: DISPATCH  ( key -- )
  DIGIT? IF    HANDLE-DIGIT  SHOW-NUM EXIT THEN
  DUP CHAR + = IF HANDLE-OP  SHOW-NUM EXIT THEN
  DUP CHAR - = IF HANDLE-OP  SHOW-NUM EXIT THEN
  DUP CHAR * = IF HANDLE-OP  SHOW-NUM EXIT THEN
  DUP CHAR / = IF HANDLE-OP  SHOW-NUM EXIT THEN
  DUP CHAR = = IF DROP HANDLE-EQ   SHOW-NUM EXIT THEN
  DUP CHAR C = IF DROP HANDLE-CLEAR SHOW-NUM EXIT THEN
  DUP CHAR M = IF DROP HANDLE-MPLUS          EXIT THEN
  DUP CHAR R = IF DROP HANDLE-MR    SHOW-NUM EXIT THEN
  DROP ;

\ FLASH-AT ( char col row -- )  briefly show char as black-on-green, then restore
: FLASH-AT
  BTN-R !  BTN-C !
  DUP INV-BYTE $40 +  BTN-C @  BTN-R @  VRAW!
  2000 0 DO LOOP
  INV-BYTE            BTN-C @  BTN-R @  VRAW! ;

\ FLASH-KEY ( char -- )  flash the on-screen label matching the key
: FLASH-KEY
  DUP CHAR 7 = IF DROP CHAR 7 3  8  FLASH-AT EXIT THEN
  DUP CHAR 8 = IF DROP CHAR 8 9  8  FLASH-AT EXIT THEN
  DUP CHAR 9 = IF DROP CHAR 9 15 8  FLASH-AT EXIT THEN
  DUP CHAR / = IF DROP CHAR / 21 8  FLASH-AT EXIT THEN
  DUP CHAR M = IF DROP CHAR M 27 8  FLASH-AT EXIT THEN
  DUP CHAR 4 = IF DROP CHAR 4 3  10 FLASH-AT EXIT THEN
  DUP CHAR 5 = IF DROP CHAR 5 9  10 FLASH-AT EXIT THEN
  DUP CHAR 6 = IF DROP CHAR 6 15 10 FLASH-AT EXIT THEN
  DUP CHAR * = IF DROP CHAR * 21 10 FLASH-AT EXIT THEN
  DUP CHAR R = IF DROP CHAR R 27 10 FLASH-AT EXIT THEN
  DUP CHAR 1 = IF DROP CHAR 1 3  12 FLASH-AT EXIT THEN
  DUP CHAR 2 = IF DROP CHAR 2 9  12 FLASH-AT EXIT THEN
  DUP CHAR 3 = IF DROP CHAR 3 15 12 FLASH-AT EXIT THEN
  DUP CHAR - = IF DROP CHAR - 21 12 FLASH-AT EXIT THEN
  DUP CHAR 0 = IF DROP CHAR 0 3  14 FLASH-AT EXIT THEN
  DUP CHAR C = IF DROP CHAR C 9  14 FLASH-AT EXIT THEN
  DUP CHAR = = IF DROP CHAR = 15 14 FLASH-AT EXIT THEN
  DUP CHAR + = IF DROP CHAR + 21 14 FLASH-AT EXIT THEN
  DROP ;

: CALC   BEGIN  KEY DUP FLASH-KEY DISPATCH  AGAIN ;

\ ── Static screen ─────────────────────────────────────────────────────────────

: DRAW-BUTTONS
  \ Row 8:  7 8 9 / M   (cols 1 7 13 19 25)
  $8F CHAR 7 1  8 BUTTON5
  $8F CHAR 8 7  8 BUTTON5
  $8F CHAR 9 13 8 BUTTON5
  $8F CHAR / 19 8 BUTTON5
  $8F CHAR M 25 8 BUTTON5
  \ Row 10: 4 5 6 * R
  $8F CHAR 4 1  10 BUTTON5
  $8F CHAR 5 7  10 BUTTON5
  $8F CHAR 6 13 10 BUTTON5
  $8F CHAR * 19 10 BUTTON5
  $8F CHAR R 25 10 BUTTON5
  \ Row 12: 1 2 3 -
  $8F CHAR 1 1  12 BUTTON5
  $8F CHAR 2 7  12 BUTTON5
  $8F CHAR 3 13 12 BUTTON5
  $8F CHAR - 19 12 BUTTON5
  \ Row 14: 0 C = +
  $8F CHAR 0 1  14 BUTTON5
  $8F CHAR C 7  14 BUTTON5
  $8F CHAR = 13 14 BUTTON5
  $8F CHAR + 19 14 BUTTON5 ;

: DRAW-SCREEN
  CLEAR-SCREEN
  DRAW-BUTTONS ;

\ ── Entry point ───────────────────────────────────────────────────────────────

: MAIN
  HANDLE-CLEAR
  DRAW-SCREEN
  SHOW-NUM
  CALC ;

MAIN HALT
