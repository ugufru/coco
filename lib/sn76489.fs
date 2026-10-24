\ sn76489.fs - SN76489 sound chip words and song stream player
\
\ Provides: sn!, sn-silence, cart-audio, sn-tone, sn-vol, sn-noise,
\           sn-start, sn-frame, sn-skip, sn-play, sn-atten,
\           sn-irq-on, sn-irq-off, sn-hold, sn-vsync, sn-ticks,
\           sn-over?, sn-frames,
\           sng-v2?, sng-frames, sng-title, sng-composer, sng-body,
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
\ A v2 song starts with a header ($EA, version, length, title, composer)
\ that sn-start skips; the sng- words read it.
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

\ sn-atten -- master attenuation, 0 full volume to 15 silent, added to
\ every level the envelope writes (#599)
VARIABLE sn-atten

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
    I sn-lvl + C@  sn-atten @ +  15 MIN  I sn-vol
  LOOP ;

\ ── v2 header (#600) ───────────────────────────────────────────────────
\ $EA, version, length in frames (big-endian), counted title, counted
\ composer, then the stream. A v1 song has none of it.

\ sng-v2? -- true if the song at addr has a header
: sng-v2?  ( addr -- f )  C@ $EA = ;

\ sng-frames -- length in frames (unsigned, repeats included), 0 if unknown
: sng-frames  ( addr -- n )  DUP sng-v2? IF 2 + @ ELSE DROP 0 THEN ;

\ sng-title -- the song's title, or an empty string
: sng-title  ( addr -- c-addr u )
  DUP sng-v2? IF 4 + COUNT ELSE 0 THEN ;

\ sng-composer -- the song's composer, or an empty string
: sng-composer  ( addr -- c-addr u )
  DUP sng-v2? IF 4 + COUNT + COUNT ELSE 0 THEN ;

\ sng-body -- the first byte of the stream proper
: sng-body  ( addr -- addr' )
  DUP sng-v2? IF 4 + COUNT + COUNT + THEN ;

\ _sn-step holds the address of the routine the field-sync handler runs
\ once a frame: the .SNG stepper below, or lib/vgm.fs's. Set by the
\ player's start word, so one handler serves both formats.
VARIABLE _sn-step

\ Make the handler step .SNG streams. The routine after ;NEXT is that
\ step, a subroutine (RTS) doing exactly what sn-frame does; it relies
\ on _sn-pl, _sn-pd and _sn-pf, which sn-irq-on sets.
CODE _sn-use-sng  \ ( -- )
        LEAY    @step,PCR
        STY     FVAR__sn_step
        ;NEXT
@step   LDD     FVAR__sn_wait
        SUBD    #1
        STD     FVAR__sn_wait
        BNE     @env
        LDX     FVAR__sn_sp
@ev     LDB     ,X+
        BPL     @wait           ; $00-$7F: the next wait (0 = end)
        TFR     B,A
        ANDA    #$90
        CMPA    #$90
        BNE     @nvol
        TFR     B,A             ; volume: set the channel's level
        ANDB    #$0F
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA
        ANDA    #$03
        LDY     FVAR__sn_pl
        STB     A,Y
        BRA     @ev
@nvol   CMPB    #$E8
        BNE     @nmark
        STX     FVAR__sn_mark   ; mark: one repeat owed from here
        LDD     #1
        STD     FVAR__sn_reps
        BRA     @ev
@nmark  CMPB    #$E9
        BNE     @chip
        LDD     FVAR__sn_reps
        BEQ     @ev
        LDD     #0              ; repeat: go back to the mark once
        STD     FVAR__sn_reps
        LDX     FVAR__sn_mark
        BRA     @ev
@chip   STB     $FF41
        CMPB    #$E0            ; noise control has no data byte
        BHS     @ev
        LDA     ,X+             ; a tone latch does
        STA     $FF41
        BRA     @ev
@wait   CLRA
        STD     FVAR__sn_wait
        STX     FVAR__sn_sp
@env    LDX     FVAR__sn_pl     ; decay toward the floors, write volumes
        LDY     FVAR__sn_pd
        LDU     FVAR__sn_pf
        LDB     #$90            ; channel 0 volume latch
@ech    LDA     ,Y+
        ANDA    FVAR__sn_tick+1
        BNE     @nodec
        LDA     ,X
        CMPA    ,U
        BHS     @nodec
        INC     ,X
@nodec  LDA     ,X+
        ADDA    FVAR_sn_atten+1
        CMPA    #15
        BLS     @att
        LDA     #15
@att    PSHS    B
        ORA     ,S+
        STA     $FF41
        LEAU    1,U
        ADDB    #$20            ; next channel's latch
        CMPB    #$10            ; past channel 3 ($F0)
        BNE     @ech
        RTS
;CODE

\ Run the current step routine once, from Forth (lib/vgm.fs seeks and
\ plays polled with it). The routine is free to use every register.
CODE _sn-step1  \ ( -- )
        PSHS    X,U
        LDY     FVAR__sn_step
        BEQ     @none
        JSR     ,Y
@none   PULS    X,U
        ;NEXT
;CODE

\ sn-start -- point the player at a song (v1 or v2) and reset it
: sn-start  ( addr -- )
  sng-body _sn-sp !  1 _sn-wait !  0 _sn-tick !  0 _sn-reps !
  4 0 DO 15 I sn-lvl + C! LOOP  _sn-use-sng ;

\ sn-frame -- advance the song one 60 Hz frame; true once it has ended
: sn-frame  ( -- done? )
  _sn-wait @ 0= IF 1 EXIT THEN
  1 _sn-tick +!
  _sn-wait @ 1 - DUP _sn-wait !  0= IF _sn-events THEN
  _sn-envelope
  _sn-wait @ 0= ;

\ sn-skip -- run n frames of the song at once, for seeking (#601)
\ Tone and noise bytes still reach the chip and volume bytes still set
\ the levels, but nothing decays and no volume is written, so it costs
\ one step per wait byte rather than per frame. Stops at the end.
: sn-skip  ( n -- )
  DUP _sn-tick +!
  BEGIN
    _sn-wait @ 0= IF DROP EXIT THEN
    DUP _sn-wait @ < IF  _sn-wait @ SWAP - _sn-wait !  EXIT THEN
    _sn-wait @ -  _sn-events
  DUP 0= UNTIL DROP ;

\ ── Interrupt-driven playing (#604) ────────────────────────────────────
\ sn-frame called from a busy main loop lands late whenever a pass
\ overruns its frame, and the music wobbles. sn-irq-on instead steps the
\ song from the field-sync interrupt (PIA0 CB1, 60 Hz, on every CoCo):
\ the handler counts the frame and calls the step in _sn-step, the .SNG
\ one (as sn-frame) or lib/vgm.fs's. Then:
\   - wait for frames with sn-vsync, not vsync: the handler clears the
\     field-sync flag that vsync polls, and counts sn-ticks instead;
\   - hold the player (1 sn-hold) around sn-start and sn-skip, and while
\     paused, so the handler never sees half-changed state;
\   - turn it off (sn-irq-off) before disk access: lib/decb.fs needs
\     IRQs masked, and BASIC needs its own handler back on quit.

VARIABLE sn-ticks    \ field syncs counted by the handler
VARIABLE _sn-hold    \ nonzero: the handler leaves the song alone
VARIABLE _sn-irq     \ nonzero while the handler is installed
VARIABLE _sn-pl      \ addresses of sn-lvl, sn-dmask, sn-floor for it
VARIABLE _sn-pd
VARIABLE _sn-pf
VARIABLE _sn-irq0    \ saved $010C (opcode, low byte)
VARIABLE _sn-irq1    \ saved $010D-$010E (address)

\ sn-hold -- true holds the song still, false lets it play on
: sn-hold  ( f -- )  _sn-hold ! ;

\ sn-over? -- true once the song has ended
: sn-over?  ( -- f )  _sn-wait @ 0= ;

\ sn-frames -- frames played since sn-start, seeking included
: sn-frames  ( -- n )  _sn-tick @ ;

\ Install the handler on the $010C IRQ trampoline and unmask IRQ.
CODE _sn-irq-on  \ ( -- )
        ORCC    #$50
        LDA     $010C
        STA     FVAR__sn_irq0+1
        LDD     $010D
        STD     FVAR__sn_irq1
        LEAY    @irq,PCR
        STY     $010D
        LDA     #$7E            ; JMP extended
        STA     $010C
        LDA     $FF03
        ORA     #$01            ; CB1 (field sync) raises IRQ
        STA     $FF03
        LDA     $FF02           ; drop a stale flag
        ANDCC   #$EF
        ;NEXT
@irq    LDA     $FF03
        LBPL    @rti            ; not field sync
        LDA     $FF02           ; clear the flag
        LDD     FVAR_sn_ticks
        ADDD    #1
        STD     FVAR_sn_ticks
        LDD     FVAR__sn_hold
        LBNE    @rti
        LDD     FVAR__sn_wait
        BEQ     @rti            ; the song is over
        LDD     FVAR__sn_tick
        ADDD    #1
        STD     FVAR__sn_tick
        LDY     FVAR__sn_step
        BEQ     @rti
        JSR     ,Y              ; one frame of the song
@rti    RTI
;CODE

\ Mask IRQ, stop field sync raising it, and put the old handler back.
CODE _sn-irq-off  \ ( -- )
        ORCC    #$50
        LDA     $FF03
        ANDA    #$FE
        STA     $FF03
        LDA     FVAR__sn_irq0+1
        STA     $010C
        LDD     FVAR__sn_irq1
        STD     $010D
        ;NEXT
;CODE

\ sn-irq-on -- step the song from the field-sync interrupt from now on
: sn-irq-on  ( -- )
  _sn-irq @ IF EXIT THEN
  sn-lvl _sn-pl !  sn-dmask _sn-pd !  sn-floor _sn-pf !
  _sn-irq-on  1 _sn-irq ! ;

\ sn-irq-off -- back to polled playing, with IRQs masked
: sn-irq-off  ( -- )
  _sn-irq @ 0= IF EXIT THEN
  _sn-irq-off  0 _sn-irq ! ;

\ sn-vsync -- wait for the next frame, with or without the handler
: sn-vsync  ( -- )
  _sn-irq @ IF
    sn-ticks @  BEGIN sn-ticks @ OVER <> UNTIL  DROP
  ELSE vsync THEN ;

\ sn-play -- play a song to the end, one frame per vsync; true if BREAK
: sn-play  ( addr -- brk? )
  sn-start
  BEGIN
    vsync
    sn-frame IF sn-silence 0 EXIT THEN
    key? 3 =
  UNTIL
  sn-silence 1 ;
