\ badinerie.fs - J. S. Bach's Badinerie on the SN76489 (GameMaster cart)
\
\ The chip words and the song player live in lib/sn76489.fs. The notes
\ live in badinerie.py; sn76489.py turns them into the byte stream the
\ player walks. Run with -cart gmc.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/sn76489.fs

DATA[PY song
open('build/badinerie.snd', 'rb').read()
]DATA

: banner  ( -- )
  cls-black
  4 2 AT ." J. S. BACH"
  4 4 AT ." BADINERIE"
  4 5 AT ." ORCHESTRAL SUITE NO. 2"
  4 6 AT ." BWV 1067"
  4 9 AT ." SN76489 / GAMEMASTER" ;

: playing-footer  ( -- )
  4 13 AT ." BREAK    QUIT      "
  4 14 AT ."                    " ;

: done-footer  ( -- )
  4 13 AT ." ANY KEY  PLAY AGAIN"
  4 14 AT ." BREAK    QUIT      " ;

VARIABLE k

\ Wait for every key to come up, then for a fresh press. KEY? only
\ reports what is held, so without the first loop a key still down from
\ before would count as the answer.
: fresh-key  ( -- c )
  BEGIN vsync key? 0= UNTIL
  BEGIN vsync key? DUP k ! UNTIL
  k @ ;

\ Any key plays the piece again; BREAK quits.
: again?  ( -- f )  done-footer  fresh-key 3 <> ;

: main  ( -- )
  banner
  sn-silence
  cart-audio
  BEGIN
    playing-footer
    song sn-play IF 0 ELSE again? THEN
  0= UNTIL
  exit-basic ;

main
