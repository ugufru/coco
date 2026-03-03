\ Chapter 11 example — Anywhere on Screen

\ Draw a horizontal bar of dashes starting at (col, row)
: HBAR  ( len col row -- )
  AT  0 DO CHAR - EMIT LOOP ;

: MAIN
  \ Title centred on row 3
  10  3 AT
  CHAR C EMIT CHAR O EMIT CHAR L EMIT CHAR O EMIT CHAR R EMIT
  32 EMIT
  CHAR F EMIT CHAR O EMIT CHAR R EMIT CHAR T EMIT CHAR H EMIT

  \ Underline the title
  11 10  4 HBAR

  \ Tagline on row 6
  10  6 AT
  CHAR O EMIT CHAR N EMIT 32 EMIT
  CHAR Y EMIT CHAR O EMIT CHAR U EMIT CHAR R EMIT 32 EMIT
  CHAR C EMIT CHAR O EMIT CHAR C EMIT CHAR O EMIT

  \ Park cursor at bottom-left
  0 15 AT ;

MAIN HALT
