\ bye.fs — Clean program exit
\
\ Provides: bye
\ Requires: vdg.fs (reset-text), screen.fs (cls-black), kernel halt
\
\ Restores the VDG to default alpha/SG4 text mode, clears VRAM to a
\ black background, and halts.  In a cartridge-delivered system the
\ physical RESET button is the real restart path — bye gives a clean
\ visual landing while we wait for it.  Proper Color BASIC ROM handoff
\ is tracked in issue #474.

INCLUDE vdg.fs
INCLUDE screen.fs

: bye  ( -- )
  reset-text
  cls-black
  halt ;
