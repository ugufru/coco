\ Chapter 10 example — The Calculator

: NEGATE   0 SWAP - ;
: ?DUP     DUP IF DUP THEN ;

: U.  ( u -- )
  10 /MOD
  ?DUP IF U. THEN
  CHAR 0 + EMIT ;

: .  ( n -- )
  DUP 0 < IF NEGATE CHAR - EMIT THEN
  U. ;

\ Return ( c 0 ) if c is not a digit 0-9, else ( c -1 )
: DIGIT?  ( c -- c flag )
  DUP CHAR 0 < IF 0 EXIT THEN
  DUP CHAR 9 > IF 0 EXIT THEN
  -1 ;

\ Process an operator key already on the stack; print result
: DISPATCH  ( key -- )
  DUP CHAR + = IF DROP +              . CR EXIT THEN
  DUP CHAR - = IF DROP -              . CR EXIT THEN
  DUP CHAR * = IF DROP *              . CR EXIT THEN
  DUP CHAR / = IF DROP /MOD SWAP DROP . CR EXIT THEN
  DUP 13 =     IF DROP DUP            . CR EXIT THEN
  DUP CHAR C = IF DROP DROP                  EXIT THEN
  DROP ;

: CALC
  BEGIN
    KEY DIGIT? IF
      DUP EMIT 32 EMIT   \ echo digit and a space
      CHAR 0 -
    ELSE
      DISPATCH
    THEN
  AGAIN ;

CALC HALT
