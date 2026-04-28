\ bye.fs — Clean program exit
\
\ Provides: bye, basic-cold
\ Requires: vdg.fs (reset-text), screen.fs (cls-black), kernel halt
\
\ bye          — restore VDG text mode, clear screen black, halt.
\                Safe in any build profile.  In all-RAM mode this is
\                the only sane exit because the BASIC ROMs are paged
\                out (issue #474).
\
\ basic-cold   — ROM-mode-only.  JMPs to Color BASIC's cold-start
\                entry at $A027, returning the user to the OK prompt.
\                ONLY call this from kernels built with -DROM_MODE=1
\                (BASIC ROMs are alive at $A000).  In all-RAM mode
\                $A027 is RAM and a JMP there will crash.

INCLUDE vdg.fs
INCLUDE screen.fs

: bye  ( -- )
  reset-text
  cls-black
  halt ;

CODE basic-cold
        JMP     $A027
;CODE
