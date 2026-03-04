\ Chapter 5 example — Remember Things
\ BASE holds a letter offset.  THIS prints the stored letter;
\ NEXT prints the one after it.  Demonstrates VARIABLE, !, and @.

: LETTER  CHAR A + EMIT ;

VARIABLE BASE

: THIS    BASE @ LETTER ;       \ ( -- ) print the stored letter
: NEXT    BASE @ 1 + LETTER ;   \ ( -- ) print the letter one past it

CHAR H CHAR A - BASE !          \ store H offset (7) in BASE
THIS                            \ prints H
NEXT                            \ prints I
CR HALT
