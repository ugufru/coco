\ Chapter 3 example — Make Your Own Words
\ LETTER takes a number off the stack (0=A, 1=B ... 25=Z)
\ and prints the corresponding uppercase letter.

: LETTER  CHAR A + EMIT ;

: SP  32 EMIT ;

: COLOR
  CHAR C CHAR A - LETTER
  CHAR O CHAR A - LETTER
  CHAR L CHAR A - LETTER
  CHAR O CHAR A - LETTER
  CHAR R CHAR A - LETTER ;

: FORTH
  CHAR F CHAR A - LETTER
  CHAR O CHAR A - LETTER
  CHAR R CHAR A - LETTER
  CHAR T CHAR A - LETTER
  CHAR H CHAR A - LETTER ;

: TITLE  COLOR SP FORTH CR ;

TITLE HALT
