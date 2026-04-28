\ src/typewriter/typewriter.fs — bare-metal keyboard echo test
\
\ Reads keys via the new PIA0-based KEY (no ROM dependency) and echoes
\ them to the VDG screen.  Tests CODE_KEY, EMIT, CR, and the full
\ MATRIX2ASCII keyboard scan path.
\
\ Key behaviour:
\   Printable keys  -> emit character at cursor, advance
\   ENTER  ($0D)    -> move to next row (CR)
\   CLEAR  ($0C)    -> clear screen and home cursor
\   BREAK  ($03)    -> exit to BASIC
\   LEFT   ($1E)    -> move cursor left (backspace)
\   RIGHT  ($1F)    -> move cursor right
\   UP     ($1C)    -> move cursor up one row
\   DOWN   ($1D)    -> move cursor down one row
\
\ Build:   make
\ Load:    LOADM"TYPEWRTR":EXEC

INCLUDE ../../forth/lib/bye.fs

\ ── Variables ────────────────────────────────────────────────────────────────

\ VAR_CUR is the kernel's cursor variable at KVAR-CUR (2 bytes).
\ We also need to track the character "under" the cursor so we can
\ restore it when the cursor moves.

VARIABLE saved-char

\ ── Cursor helpers ───────────────────────────────────────────────────────────

\ Read the VDG byte at the current cursor position
: cur@  ( -- vdg-byte )  KVAR-CUR @ $0400 + C@ ;

\ Write a VDG byte at the current cursor position
: cur!  ( vdg-byte -- )  KVAR-CUR @ $0400 + C! ;

\ Show cursor: save char under cursor, write cursor block ($EF = inverse block)
: cursor-on   cur@ saved-char !  $EF cur! ;

\ Hide cursor: restore the saved character
: cursor-off  saved-char @ cur! ;

\ ── Screen utilities ─────────────────────────────────────────────────────────

\ cls: fill video RAM with VDG spaces ($60) and home the cursor.
: cls
  512 0 DO  $60 $0400 I + C!  LOOP
  0 0 AT
  $60 saved-char ! ;

\ ── Cursor movement ──────────────────────────────────────────────────────────

\ Backspace: move cursor left, erase character there
: backspace
  KVAR-CUR @                    \ get cursor offset
  DUP 0 > IF                \ if > 0
    1 -  DUP KVAR-CUR !         \ decrement and store
    $0400 +  $60 SWAP C!     \ erase char at new position (VDG space)
  ELSE
    DROP
  THEN ;

\ Move cursor right by 1 (with ceiling at 511)
: cur-right
  KVAR-CUR @
  DUP 511 < IF
    1 +  KVAR-CUR !
  ELSE
    DROP
  THEN ;

\ Move cursor up one row (subtract 32, floor at 0)
: cur-up
  KVAR-CUR @
  DUP 31 > IF
    32 -  KVAR-CUR !
  ELSE
    DROP
  THEN ;

\ Move cursor down one row (add 32, ceiling at 511)
: cur-down
  KVAR-CUR @
  DUP 480 < IF
    32 +  KVAR-CUR !
  ELSE
    DROP
  THEN ;

\ ── Main typewriter loop ────────────────────────────────────────────────────

: typewriter
  cls
  cursor-on
  BEGIN
    KEY
    cursor-off
    DUP $03 = IF  DROP  bye                     THEN
    DUP $0D = IF  DROP  cr                      ELSE
    DUP $0C = IF  DROP  cls                     ELSE
    DUP $1E = IF  DROP  backspace                ELSE
    DUP $1F = IF  DROP  cur-right               ELSE
    DUP $1C = IF  DROP  cur-up                  ELSE
    DUP $1D = IF  DROP  cur-down                ELSE
    emit
    THEN THEN THEN THEN THEN THEN
    cursor-on
  AGAIN ;

typewriter
