\ vgm-test.fs - check lib/vgm.fs against its Python model (#598)
\
\ Embeds the Badinerie excerpt as a VGM (src/sound-sn76489/sng2vgm.py,
\ logged for a 3579545 Hz chip), prints its GD3 tag, length and pitch
\ scale, seeks 600 frames and prints the player's state in hex. `make
\ expect` prints what the screen should say. Then it plays the file
\ (twice through, for the loop): it should sound like BADINERI.SNG.
\ Run with -cart gmc.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/sn76489.fs
INCLUDE ../../lib/vgm.fs

DATA[PY tune
open('../sound-sn76489/build/badinerie.vgm', 'rb').read()
]DATA

: hex.  ( n -- )
  4 0 DO
    DUP 12 RSHIFT $0F AND  DUP 9 > IF 7 + THEN  $30 + EMIT  4 LSHIFT
  LOOP DROP  $20 EMIT ;

: main  ( -- )
  cls-black  cart-audio  sn-silence
  0 0 AT  tune vgm? IF ." VGM OK" ELSE ." NOT VGM" THEN
  0 1 AT  tune vgm-title TYPE
  0 2 AT  tune vgm-author TYPE
  tune vgm-start
  0 3 AT  ." FRAMES " tune vgm-frames hex.  ." M " _vgm-m @ hex.
  600 vgm-skip
  0 5 AT  ." AT 600:"
  0 6 AT  ." LVL "  4 0 DO I sn-lvl + C@ hex. LOOP
  0 7 AT  ." DIV "  3 0 DO I 2 * _vgm-div + @ hex. LOOP
  0 8 AT  ." OUT "  3 0 DO I vgm-pitch hex. LOOP
  0 9 AT  ." P " _vgm-p @ tune - hex.  ." W " _vgm-w @ hex.
  0 10 AT ." T " sn-frames hex.
  0 12 AT ." PLAYING, BREAK QUITS"
  tune vgm-play DROP
  0 13 AT ." DONE, T " sn-frames hex.
  BEGIN key? 3 = UNTIL
  exit-basic ;

main
