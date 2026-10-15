\ decb-test.fs - exercise lib/decb.fs against build/test.dsk (#586)
\
\ Run with a disk controller cart and the test disk in drive 0 (make run
\ does both). Shows HELLO.TXT through dk-getc, then reads SONG.SNG with
\ dk-getc and BIG.DAT with dk-load, checking length and byte sum against
\ what mkfiles.py wrote, and finally asks for a file that is not there.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/print.fs
INCLUDE ../../lib/decb.fs

\ Expected length and 16-bit sum: SONG.SNG then BIG.DAT.
DATA[PY expect
open('build/expect.bin', 'rb').read()
]DATA

VARIABLE n
VARIABLE sum

: ok?  ( f -- )  IF ."  OK" ELSE ."  FAIL" THEN CR ;

: show-file  ( -- )
  S" hello.txt" dk-open ?DUP IF ." OPEN ERR " . CR EXIT THEN
  BEGIN
    dk-getc DUP 0 < IF DROP 1 ELSE
      DUP 13 = IF DROP CR ELSE EMIT THEN 0
    THEN
  UNTIL
  ." END, ERR " dk-err @ . CR ;

\ Stream a file through dk-getc, counting and summing its bytes.
: stream  ( addr u -- )
  0 n !  0 sum !
  dk-open ?DUP IF ." OPEN ERR " . CR EXIT THEN
  BEGIN dk-getc DUP 0 < IF DROP 1 ELSE sum +! 1 n +! 0 THEN UNTIL ;

: check  ( i -- f ) \ compare n and sum with expect entry i
  4 * expect +  DUP @ n @ =  SWAP 2 + @ sum @ =  AND ;

: sum-range  ( addr len -- )
  0 sum !  0 DO DUP I + C@ sum +! LOOP DROP ;

: main  ( -- )
  cls-green
  show-file
  ." SONG.SNG GETC " S" SONG.SNG" stream  n @ u.  0 check ok?
  ." BIG.DAT LOAD "
  $5A00 $1E00 S" BIG.DAT" dk-load ?DUP IF ." ERR " . DROP CR ELSE
    DUP n !  DUP u.  $5A00 SWAP sum-range  1 check ok?
  THEN
  ." NOPE.BIN " S" NOPE.BIN" dk-open DUP . 1 = ok?
  ." DRIVE :7 " S" X:7" dk-open DUP . 11 = ok?
  dk-close
  CR ." ANY KEY" KEY DROP
  exit-basic ;

main
