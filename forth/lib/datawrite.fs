\ datawrite.fs — Data table construction helpers
\
\ Provides: tp (variable), tb, tw
\ Requires: kernel primitives C!, !, @, +
\
\ Generic helpers for building lookup tables, data structures, or any
\ structured data in a RAM region.  Set tp to the target address, then
\ call tb/tw to write bytes/words sequentially.
\
\ Usage:
\   INCLUDE lib/datawrite.fs
\   $5800 tp !                    \ point at target address
\   $FF tb                        \ write one byte, advance tp
\   $1234 tw                      \ write one word (big-endian), advance tp

VARIABLE tp                       \ data write pointer

: tb  ( byte -- )  tp @ C!  tp @ 1 + tp ! ;
: tw  ( word -- )  tp @ !   tp @ 2 + tp ! ;
