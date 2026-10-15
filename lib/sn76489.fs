\ sn76489.fs - SN76489 sound chip words and song stream player
\
\ Provides: sn!, sn-silence, cart-audio, sn-tone, sn-vol, sn-noise,
\           sn-start, sn-frame, sn-play,
\           sn-lvl, sn-dmask, sn-floor (envelope tables)
\
\ Requires: lib/screen.fs (vsync, for sn-play)
\
\ The SN76489 sits at $FF41. On the GameMaster cart (XRoar: -cart gmc)
\ any write to an odd address in $FF40-$FF5F reaches the chip, clocked
\ at 4 MHz, so a tone divider is N = 125000 / f (10 bits, about B2 at
\ the bottom). $FF41 is also the Becker port: no Becker cart alongside.
\
\ Songs are byte streams built on the host by
\ src/sound-sn76489/sn76489.py and midi2sng.py. The format is specified in
\ src/sound-sn76489/SNG-FORMAT.md; in short:
\   $00        end of song
\   $01-$7F    wait that many frames
\   $E8 $E9    mark / repeat-once
\   $80-$FF    SN76489 latch byte; a tone latch carries one data byte.
\              Volume latches set the channel level, and a per-channel
\              envelope decays it, so notes pluck instead of drone.
\ A stream is relocatable: the player holds only a pointer to it, so a
\ song can live in the binary (DATA[PY) or be loaded anywhere in RAM.
\
\ Usage:
\   cart-audio  sn-silence
\   song sn-play IF ( BREAK pressed ) THEN
\ or drive it yourself, one call per 60 Hz frame:
\   song sn-start  BEGIN vsync ... sn-frame UNTIL  sn-silence

\ ── Chip ───────────────────────────────────────────────────────────────

\ sn! -- write one byte to the SN76489
: sn!  ( b -- )  $FF41 C! ;

\ sn-silence -- set all four channels to full attenuation
: sn-silence  ( -- )  $9F sn!  $BF sn!  $DF sn!  $FF sn! ;

\ cart-audio -- route the cartridge SND line to the speaker
\ The analog mux source is PIA0 CB2:CA2 (00 DAC, 01 tape, 10 cartridge;
\ xroar src/dragon/dragon.c), and PIA1 CB2 enables sound at all. The
\ kernel leaves all three as inputs, i.e. muted.
: cart-audio  ( -- )
  $34 $FF01 C!       \ PIA0 CA2 = output low  (mux bit 0 = 0)
  $3C $FF03 C!       \ PIA0 CB2 = output high (mux bit 1 = 1), IRQs off
  $3C $FF23 C! ;     \ PIA1 CB2 = output high: sound on

\ sn-tone -- set tone channel 0-2 to a 10-bit divider (125000 / Hz)
: sn-tone  ( n ch -- )
  5 LSHIFT $80 OR  OVER $0F AND OR  sn!
  4 RSHIFT $3F AND  sn! ;

\ sn-vol -- set channel 0-3 attenuation, 0 loudest, 15 off
: sn-vol  ( att ch -- )  5 LSHIFT $90 OR  SWAP $0F AND OR  sn! ;

\ sn-noise -- set the noise control (bit 2 white, bits 1-0 rate)
: sn-noise  ( ctrl -- )  $07 AND $E0 OR  sn! ;

\ ── Player ─────────────────────────────────────────────────────────────

\ Per channel 0-3: current attenuation (0 loud, 15 off), how often it
\ decays (frame mask: 0 every frame, 1 every 2nd, 3 every 4th), and the
\ level the decay stops at. A song may C! its own values into
\ sn-dmask and sn-floor before playing.
DATA[PY sn-lvl
bytes([15, 15, 15, 15])
]DATA
DATA[PY sn-dmask
bytes([1, 3, 3, 0])
]DATA
DATA[PY sn-floor
bytes([8, 10, 8, 15])
]DATA

VARIABLE _sn-sp      \ stream pointer
VARIABLE _sn-wait    \ frames left before the next events
VARIABLE _sn-tick    \ frame counter, for the decay masks
VARIABLE _sn-mark    \ start of the section being repeated
VARIABLE _sn-reps    \ repeats still owed to that section

: _sn-next  ( -- b )  _sn-sp @ C@  1 _sn-sp +! ;

\ A tone-frequency latch: bit 7 set, bit 4 clear, channel 0-2.
: _sn-tone?  ( b -- f )  DUP $90 AND $80 = IF $E0 < ELSE DROP 0 THEN ;

: _sn-set-lvl  ( b -- )  DUP $0F AND  SWAP 5 RSHIFT 3 AND  sn-lvl + C! ;

: _sn-cmd  ( b -- )
  DUP $90 AND $90 = IF _sn-set-lvl EXIT THEN
  DUP $E8 = IF DROP  _sn-sp @ _sn-mark !  1 _sn-reps !  EXIT THEN
  DUP $E9 = IF DROP
    _sn-reps @ IF 0 _sn-reps !  _sn-mark @ _sn-sp ! THEN  EXIT THEN
  DUP sn!  _sn-tone? IF _sn-next sn! THEN ;

\ Run every command up to the next wait byte; that byte (0 = end) is
\ the new wait count.
: _sn-events  ( -- )
  BEGIN _sn-next DUP $80 AND IF _sn-cmd 0 ELSE _sn-wait ! 1 THEN UNTIL ;

\ sn-lvl entries are bytes, and there is no C+!, so add by hand.
: _sn-lvl+!  ( ch -- )  sn-lvl +  DUP C@ 1 +  SWAP C! ;

\ Decay each channel one step toward its floor when its mask allows,
\ then write all four volume latches.
: _sn-envelope  ( -- )
  4 0 DO
    I sn-dmask + C@ _sn-tick @ AND 0= IF
      I sn-floor + C@  I sn-lvl + C@  > IF I _sn-lvl+! THEN
    THEN
    I sn-lvl + C@  I sn-vol
  LOOP ;

\ sn-start -- point the player at a song stream and reset it
: sn-start  ( addr -- )
  _sn-sp !  1 _sn-wait !  0 _sn-tick !  0 _sn-reps !
  4 0 DO 15 I sn-lvl + C! LOOP ;

\ sn-frame -- advance the song one 60 Hz frame; true once it has ended
: sn-frame  ( -- done? )
  1 _sn-tick +!
  _sn-wait @ 1 - DUP _sn-wait !  0= IF _sn-events THEN
  _sn-envelope
  _sn-wait @ 0= ;

\ sn-play -- play a song to the end, one frame per vsync; true if BREAK
: sn-play  ( addr -- brk? )
  sn-start
  BEGIN
    vsync
    sn-frame IF sn-silence 0 EXIT THEN
    key? 3 =
  UNTIL
  sn-silence 1 ;
