\ badinerie.fs - J. S. Bach's Badinerie on the SN76489 (GameMaster cart)
\
\ The SN76489 sits at $FF41: XRoar's gmc cart sends any write to an odd
\ address in $FF40-$FF5F to the chip (xroar src/gmc.c). Run with -cart gmc.
\ $FF41 is also the Becker port, so no Becker cart in the same session.
\
\ The notes live in badinerie.py; sn76489.py turns them into a byte
\ stream (format documented there) that this player walks, one 60 Hz
\ frame at a time:
\   $00        end of song
\   $01-$7F    wait that many frames
\   $E8 $E9    mark / repeat-once (each half of the piece is stored once)
\   $80-$FF    SN76489 latch byte; a tone latch carries one data byte.
\              Volume latches set the channel level, and a per-channel
\              envelope decays it, so notes pluck instead of drone.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs

DATA[PY song
open('build/badinerie.snd', 'rb').read()
]DATA

\ Per channel 0-3: current attenuation (0 loud, 15 off), how often it
\ decays (frame mask: 0 every frame, 1 every 2nd, 3 every 4th), and the
\ level the decay stops at.
DATA[PY lvl
bytes([15, 15, 15, 15])
]DATA
DATA[PY dmask
bytes([1, 3, 3, 0])
]DATA
DATA[PY floor
bytes([8, 10, 8, 15])
]DATA

VARIABLE sp        \ stream pointer
VARIABLE wait      \ frames left before the next events
VARIABLE tick      \ frame counter, for the decay masks
VARIABLE mark      \ start of the section being repeated
VARIABLE reps      \ repeats still owed to that section

: sn!     ( b -- )  $FF41 C! ;
: next@   ( -- b )  sp @ C@  1 sp +! ;

\ A tone-frequency latch: bit 7 set, bit 4 clear, channel 0-2.
: tone?   ( b -- f )  DUP $90 AND $80 = IF $E0 < ELSE DROP 0 THEN ;

: set-lvl ( b -- )  DUP $0F AND  SWAP 5 RSHIFT 3 AND  lvl + C! ;

\ $E8 marks a section, $E9 plays it once more (sn76489.py).
: repeat  ( -- )
  reps @ IF 0 reps !  mark @ sp ! THEN ;

: cmd     ( b -- )
  DUP $90 AND $90 = IF set-lvl EXIT THEN
  DUP $E8 = IF DROP  sp @ mark !  1 reps !  EXIT THEN
  DUP $E9 = IF DROP  repeat  EXIT THEN
  DUP sn!  tone? IF next@ sn! THEN ;

\ Run every command up to the next wait byte; that byte (0 = end) is
\ the new wait count.
: events  ( -- )
  BEGIN next@ DUP $80 AND IF cmd 0 ELSE wait ! 1 THEN UNTIL ;

\ lvl entries are bytes, and there is no C+!, so add by hand.
: lvl+!   ( ch -- )  lvl +  DUP C@ 1 +  SWAP C! ;

\ Decay each channel one step toward its floor when its mask allows,
\ then write all four volume latches.
: envelope  ( -- )
  4 0 DO
    I dmask + C@ tick @ AND 0= IF
      I floor + C@  I lvl + C@  > IF I lvl+! THEN
    THEN
    I lvl + C@  I 5 LSHIFT OR  $90 OR  sn!
  LOOP ;

: silence ( -- )  $9F sn!  $BF sn!  $DF sn!  $FF sn! ;

\ Route the cartridge's SND line to the speaker. The analog mux source
\ is PIA0 CB2:CA2 (00 DAC, 01 tape, 10 cartridge) and PIA1 CB2 enables
\ sound at all. The kernel leaves all three as inputs, i.e. muted.
: cart-audio  ( -- )
  $34 $FF01 C!       \ PIA0 CA2 = output low  (mux bit 0 = 0)
  $3C $FF03 C!       \ PIA0 CB2 = output high (mux bit 1 = 1), IRQs off
  $3C $FF23 C! ;     \ PIA1 CB2 = output high: sound on

: banner  ( -- )
  cls-black
  4 2 AT ." J. S. BACH"
  4 4 AT ." BADINERIE"
  4 5 AT ." ORCHESTRAL SUITE NO. 2"
  4 6 AT ." BWV 1067"
  4 9 AT ." SN76489 / GAMEMASTER"
  4 13 AT ." BREAK  QUIT" ;

: main  ( -- )
  banner
  silence
  cart-audio
  song sp !  1 wait !  0 tick !
  BEGIN
    vsync
    1 tick +!
    wait @ 1 - DUP wait !  0= IF events THEN
    envelope
    key? 3 =  wait @ 0=  OR
  UNTIL
  silence
  exit-basic ;

main
