\ ay8910.fs - AY-3-8910 / YM2149 sound chip words (#632)
\
\ Provides: ay-psg, ay-chip, ay-chips, ay!, ay-tone, ay-vol, ay-noise,
\           ay-mix, ay-env, ay-silence, ay-init, ay-psg-slow, ay-found?
\
\ Requires: kernel primitives only
\
\ Two cartridges carry the chip, and both are on the CoCo Zero
\ (xroar-waveshare-rp2350-pizero PIZERO-149, psg_ay8910.h). Stock XRoar
\ has neither.
\   CoCo PSG (a YM2149): $FF5E latches a register number, $FF5F writes
\     it. $FF5D bit 0 picks its clock: 0 = 2 MHz, 1 = 1 MHz.
\   Symphony-12 (Speech Systems, four AY-3-8910s, twelve voices): a 6821
\     PIA at $FF60-$FF63. Port A is the chips' data bus; port B has two
\     bits per chip (bits 1-0 chip 0 ... 7-6 chip 3): 11 latch, 10 write,
\     00 idle. Clocked at 14.31818 MHz / 16 = 894886 Hz. Chips 0 and 1
\     play on the left, 2 and 3 on the right.
\ The Deluxe CoCo's AY-3-8913 ($FF38/$FF39) is left out: on a CoCo 2
\ those addresses repeat PIA1, so the CoCo Zero has it off.
\
\ The chip: three square-wave tone channels A-C, f = clock / (16 * TP)
\ with a 12-bit period TP; one noise generator, f = clock / (16 * NP)
\ with a 5-bit NP; a mixer (R7, a bit set turns a source off); a level
\ per channel, 0 silent to 15 loudest, or 16 to follow the envelope; and
\ one envelope, a period (R11-R12) and a shape (R13, writing it restarts
\ the envelope). Shape 0 decays once and stays silent: a plucked note.
\   TP for a note: 125000 / Hz on the CoCo PSG at 2 MHz,
\                  55930 / Hz on the Symphony-12.
\
\ Every word acts on the selected chip or chips: ay-psg picks the CoCo
\ PSG, ay-chip one Symphony-12 chip, ay-chips several at once (a mask,
\ bit n = chip n), so one write can set a register on all of them.
\
\ Is it there? ay-found? writes register 2 and reads it back. Do that
\ before anything else: without the chip, a GameMaster takes every odd
\ address in $FF40-$FF5F, so writes meant for the CoCo PSG land in its
\ SN76489 as noise (and $FF5E selects its ROM bank). The probe's own
\ bytes, $9F, $BF and $FF, are SN76489 "channel off" commands, so it is
\ safe there. ay-init probes each cartridge before writing to it.
\
\ Usage:
\   ay-init  cart-audio          ( cart-audio is in lib/sn76489.fs )
\   ay-psg ay-found? IF
\     $3E ay-mix  250 0 ay-tone  15 0 ay-vol          \ 500 Hz on A
\   THEN

VARIABLE _ay-lat     \ Symphony-12 port B value that latches, 0 = PSG
VARIABLE _ay-wr      \ the one that writes
VARIABLE _ay-rd      \ and the one that reads

\ ay! -- write data to register reg of the selected chip(s)
CODE ay!  \ ( data reg -- )
        LDD     FVAR__ay_lat
        BNE     @sym
        LDB     1,U             ; CoCo PSG: register, then data
        STB     $FF5E
        LDB     3,U
        STB     $FF5F
        BRA     @out
@sym    LDA     1,U             ; Symphony-12: register on port A,
        STA     $FF60
        LDA     FVAR__ay_lat+1  ; latch it, back to idle,
        STA     $FF62
        CLRA
        STA     $FF62
        LDA     3,U             ; data on port A,
        STA     $FF60
        LDA     FVAR__ay_wr+1   ; write it, back to idle
        STA     $FF62
        CLRA
        STA     $FF62
@out    LEAU    4,U
        ;NEXT
;CODE

\ ay-chips -- select Symphony-12 chips by mask (bit n = chip n), or 0
\ for the CoCo PSG. Each chip bit becomes a pair: 11 latch, 10 write.
: ay-chips  ( mask -- )
  0  4 0 DO
    OVER 1 I LSHIFT AND IF  1 I 2 * LSHIFT OR  THEN
  LOOP  SWAP DROP
  DUP 3 * _ay-lat !  DUP 2 * _ay-wr !  _ay-rd ! ;

\ Read register reg back. The Symphony-12's port A turns to inputs for
\ the read, with the selected chips set to 01 (read) on port B; with
\ several selected their bits are ORed, as on the card.
CODE _ay-rd@  \ ( reg -- b )
        LDD     FVAR__ay_lat
        BNE     @sym
        LDB     1,U             ; CoCo PSG: latch, then read
        STB     $FF5E
        LDB     $FF5F
        BRA     @out
@sym    LDA     1,U             ; latch the register
        STA     $FF60
        LDA     FVAR__ay_lat+1
        STA     $FF62
        CLRA
        STA     $FF62
        STA     $FF61           ; port A to inputs
        STA     $FF60
        LDA     #4
        STA     $FF61
        LDA     FVAR__ay_rd+1   ; read
        STA     $FF62
        LDB     $FF60
        CLRA
        STA     $FF62           ; idle, and port A back to outputs
        STA     $FF61
        LDA     #$FF
        STA     $FF60
        LDA     #4
        STA     $FF61
@out    CLRA
        STD     ,U
        ;NEXT
;CODE

\ ay-found? -- true if the selected chip answers
: ay-found?  ( -- f )
  $9F 2 ay!  2 _ay-rd@ $9F =
  $BF 2 ay!  2 _ay-rd@ $BF = AND
  $FF 2 ay! ;

\ ay-chip -- select Symphony-12 chip 0-3
: ay-chip  ( n -- )  1 SWAP LSHIFT ay-chips ;

\ ay-psg -- select the CoCo PSG
: ay-psg  ( -- )  0 ay-chips ;

\ ay-tone -- set channel 0-2's 12-bit tone period
: ay-tone  ( tp ch -- )
  2 * >R  DUP $FF AND R@ ay!  8 RSHIFT $0F AND R> 1 + ay! ;

\ ay-vol -- set channel 0-2's level: 0 silent to 15, 16 = envelope
: ay-vol  ( level ch -- )  8 + ay! ;

\ ay-noise -- set the 5-bit noise period
: ay-noise  ( np -- )  $1F AND 6 ay! ;

\ ay-mix -- set the mixer: bits 0-2 tone A-C off, bits 3-5 noise A-C
\ off. $38 = three tones, no noise. The I/O port bits stay inputs.
: ay-mix  ( bits -- )  $3F AND 7 ay! ;

\ ay-env -- set the envelope period and shape, and restart it
: ay-env  ( ep shape -- )
  >R  DUP $FF AND 11 ay!  8 RSHIFT 12 ay!  R> $0F AND 13 ay! ;

\ ay-silence -- levels to 0 and the mixer off on the selected chip(s)
: ay-silence  ( -- )  3 0 DO 0 I ay-vol LOOP  $3F ay-mix ;

\ ay-psg-slow -- true runs the CoCo PSG at 1 MHz, false at 2 MHz
: ay-psg-slow  ( f -- )  IF 1 ELSE 0 THEN $FF5D C! ;

\ ay-init -- set up the Symphony-12's PIA, and silence whichever chips
\ answer (the PSG at 2 MHz); the CoCo PSG is selected after
: ay-init  ( -- )
  0 $FF61 C!  $FF $FF60 C!  4 $FF61 C!    \ port A: all outputs
  0 $FF63 C!  $FF $FF62 C!  4 $FF63 C!    \ port B: all outputs
  0 $FF62 C!                              \ every chip idle
  $0F ay-chips  ay-found? IF ay-silence THEN
  ay-psg  ay-found? IF 0 ay-psg-slow ay-silence THEN ;
