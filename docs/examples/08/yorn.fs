\ Chapter 8 example — Read the Keyboard

: .YES   CHAR Y EMIT CHAR E EMIT CHAR S EMIT ;
: .NO    CHAR N EMIT CHAR O EMIT ;

: YORN  ( -- )
  KEY CHAR Y = IF .YES ELSE .NO THEN CR ;

: MAIN
  YORN
  YORN
  YORN ;

MAIN HALT
