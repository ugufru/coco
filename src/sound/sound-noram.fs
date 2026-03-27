\ sound-noram.fs — Sound test WITHOUT all-RAM mode
\ This runs from regular BASIC memory space, no kernel, no bootstrap.
\ Just raw machine code that toggles the DAC.

\ We need a minimal program that:
\ 1. Inits PIA for sound
\ 2. Toggles DAC in a loop
\ 3. Halts

\ Since we can't use the kernel (it pages out ROMs), let's just
\ create a BASIC program that POKEs and calls SOUND.

\ Actually, let's test from BASIC directly.
\ Type this into BASIC manually:

\ 10 POKE 65315,PEEK(65315) OR 8
\ 20 FOR I=1 TO 500
\ 30 POKE 65312,252
\ 40 FOR J=1 TO 10:NEXT
\ 50 POKE 65312,0
\ 60 FOR J=1 TO 10:NEXT
\ 70 NEXT
