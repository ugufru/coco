\ fujinet-time.fs — Display the FujiNet device's wall-clock time
\
\ Calls fn-time to fetch a 6-byte date/time block from the FujiNet
\ over the cartridge's bit-banger DriveWire driver, then formats it
\ as YYYY.MM.DD HH:MM:SS on the VDG text screen.
\
\ Build:   make
\ Load:    LOADM"FNTIME":EXEC

INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/fujinet.fs


\ ── Inline 6-byte buffer that holds the FujiNet time response ────
\ ( -- addr )  push the address of the buffer
CODE time-buf
        LEAY    @buf,PCR
        STY     ,--U
        ;NEXT
@buf    RMB     6
;CODE


\ ── 2-digit decimal print with leading zero ──────────────────────
\ ( n -- )  emits two ASCII digits.  n must be 0..99.
: 2dig
  10 /MOD                       \ ( ones tens )
  CHAR 0 + EMIT
  CHAR 0 + EMIT ;


\ ── Print a 4-digit year derived from year-1900 ──────────────────
\ ( yr-1900 -- )
\ Years 1900..1999 print as "19xx", years 2000..2099 as "20xx".
\ Out-of-range years (>= 2100) won't render correctly.
: print-year
  DUP 100 < IF
    CHAR 1 EMIT CHAR 9 EMIT
  ELSE
    CHAR 2 EMIT CHAR 0 EMIT
    100 -
  THEN
  2dig ;


\ ── Format the buffer as "YYYY.MM.DD HH:MM:SS" ───────────────────
: print-time  ( buf -- )
  DUP     C@ print-year         \ year
  CHAR . EMIT
  DUP 1 + C@ 2dig               \ month
  CHAR . EMIT
  DUP 2 + C@ 2dig               \ day
  32 EMIT
  DUP 3 + C@ 2dig               \ hour
  CHAR : EMIT
  DUP 4 + C@ 2dig               \ minute
  CHAR : EMIT
      5 + C@ 2dig ;             \ second


\ Wait for FujiNet with a visible heartbeat.  Emits one '.' per
\ VSYNC tick (~60 Hz) so we can see activity on screen and tell
\ "no FN present" apart from "crashed".  Inlines fn-ready so the
\ library word can stay silent.
: wait-for-fn  ( -- )
  BEGIN
    CHAR . EMIT
    vsync
    fn-ping
  UNTIL ;

: main  ( -- )
  cls-green
  CR CR
  CHAR F EMIT CHAR U EMIT CHAR J EMIT CHAR I EMIT 32 EMIT
  CHAR T EMIT CHAR I EMIT CHAR M EMIT CHAR E EMIT CR CR
  wait-for-fn                           \ hand-shake (visible)
  FN-CMD-TIME fn-cmd C!                 \ send $23
  fn-cmd 1 dw-write
  time-buf 6 dw-read DROP               \ read 6 bytes into buffer
  CR
  time-buf print-time
  CR
  HALT ;

main
