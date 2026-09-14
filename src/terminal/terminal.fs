\ src/terminal/terminal.fs - CoCo terminal for Claude Code (#24)
\
\ A dumb terminal: every key goes straight out the Becker port, and every
\ byte that comes back is drawn with the T1 lowercase console.  The Mac-side
\ bridge (tools/cocoterm_bridge.py) owns the line: it echoes, handles
\ backspace, submits on ENTER and runs Claude Code.
\
\ The bridge can also read and write CoCo memory through ESC commands
\ (see "Memory commands" below), which Claude uses as peek/poke tools.
\
\ Keys:
\   letters        sent lowercase; SHIFT+letter sends uppercase
\   LEFT arrow     backspace ($08)
\   ENTER          submit ($0D)
\   BREAK          cancel the current reply ($03)
\   CLEAR          clear screen ($0C)
\
\ Run (bridge first: XRoar connects to it once, at start-up):
\   make bridge        (in one terminal)
\   make run           (in another)
\
\ Needs a T1 VDG machine (coco2bus) for real lowercase.
\ Known limits: no key auto-repeat, fast rollover can drop a key (#562).

INCLUDE ../../lib/console.fs
INCLUDE ../../lib/becker.fs

VARIABLE prev        \ last raw key? value, for press detection
VARIABLE under       \ screen byte hidden under the cursor
VARIABLE shown       \ nonzero while the cursor block is drawn
VARIABLE blink       \ VSYNC frame counter for the blink

\ ── Cursor ────────────────────────────────────────────────────────────────

: cur-addr  ( -- addr )  KVAR-CUR @ $0400 + ;

\ In T1 lowercase mode screen code $20 is a solid block.
: cursor-on
  shown @ 0= IF  cur-addr C@ under !  $20 cur-addr C!  -1 shown !  THEN ;

: cursor-off
  shown @ IF  under @ cur-addr C!  0 shown !  THEN ;

\ Count frames without blocking: PIA0 CRB bit 7 is the VSYNC flag,
\ cleared by reading $FF02 (same trick as the kernel KEY repeat timer).
: tick
  $FF03 C@ $80 AND IF
    $FF02 C@ DROP  1 blink +!
  THEN
  blink @ 16 AND IF cursor-on ELSE cursor-off THEN ;

\ ── Memory commands (#564) ───────────────────────────────────────────────
\
\ ESC starts a command from the bridge; the console never draws ESC.
\   ESC P ah al n        read n bytes   reply: ESC + the n bytes
\   ESC W ah al n data   write n bytes  reply: ESC
\ The bridge sends n = 1..64 and paces the frame.

: bk-wait  ( -- c )  BEGIN bk? UNTIL bk@ ;
: bk-addr  ( -- addr )  bk-wait 8 LSHIFT bk-wait OR ;

: cmd-peek  ( -- )
  bk-addr bk-wait  $1B bk!  bk-type ;

: cmd-poke  ( -- )
  bk-addr bk-wait
  DUP IF
    OVER + SWAP DO  bk-wait I C!  LOOP
  ELSE
    2DROP
  THEN
  $1B bk! ;

: command  ( -- )
  bk-wait
  DUP CHAR P = IF  DROP cmd-peek  ELSE
  CHAR W = IF  cmd-poke  THEN  THEN ;

\ ── Incoming bytes ───────────────────────────────────────────────────────

: receive  ( c -- )
  cursor-off
  DUP $1B = IF  DROP command  ELSE  con-emit  THEN ;

: drain
  BEGIN
    bk? IF  bk@ receive 0  ELSE  -1  THEN
  UNTIL ;

\ ── Keyboard ─────────────────────────────────────────────────────────────

\ KEY_TABLE is uppercase-only and SHIFT leaves letters alone, so make
\ unshifted letters lowercase, the way a modern keyboard feels.
: map-key  ( c -- c' )
  DUP CHAR A  CHAR Z 1 +  WITHIN IF
    KVAR-KEY-SHIFT C@ 0= IF  $20 OR  THEN
  THEN
  DUP $1E = IF  DROP $08  THEN ;

\ key? does not debounce, so send only on a change to a new nonzero key.
: keys
  key? DUP prev @ <> IF
    DUP prev !
    ?DUP IF  map-key bk!  THEN
  ELSE
    DROP
  THEN ;

\ ── Main ─────────────────────────────────────────────────────────────────

: main
  lower-on
  con-cls
  S" cocoterm / bare naked forth" con-type $0D con-emit
  S" waiting for the bridge..." con-type $0D con-emit
  0 prev !  0 shown !  0 blink !
  BEGIN
    drain
    keys
    tick
  AGAIN ;

main
