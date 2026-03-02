\ Chapter 6 example — Count and Loop

: LETTER  CHAR A + EMIT ;

: ALPHA   26 0 DO  I LETTER       LOOP ;   \ A→Z
: BACKW   26 0 DO  25 I - LETTER  LOOP ;   \ Z→A

: MAIN
  ALPHA CR
  BACKW CR ;

MAIN HALT
