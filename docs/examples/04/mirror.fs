\ Chapter 4 example — The Stack Is Your Friend
\ DUP lets you use a value twice without pushing it twice.
\ OVER lets you peek at the second item without losing the top.

: LETTER  CHAR A + EMIT ;
: SP      32 EMIT ;

\ DUP: print a letter twice  ( n -- )
: DOUBLE  DUP LETTER LETTER ;

\ OVER: print n1 n2 n1 — a three-letter palindrome  ( n1 n2 -- )
: MIRROR  OVER LETTER LETTER LETTER ;

\ Print "HH AHA"
: MAIN
  CHAR H CHAR A - DOUBLE          \ HH
  SP
  CHAR A CHAR A -                 \ push A offset (0) — goes to NOS
  CHAR H CHAR A -                 \ push H offset (7) — becomes TOS
  MIRROR                          \ prints AHA
  CR ;

MAIN HALT
