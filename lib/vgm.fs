\ vgm.fs - play VGM (Video Game Music) files on the SN76489 (#598)
\
\ Provides: vgm?, vgm-start, vgm-frame, vgm-skip, vgm-play,
\           vgm-frames, vgm-title, vgm-author, vgm-pitch
\
\ Requires: lib/sn76489.fs (the chip, sn-lvl, sn-atten, and the
\           field-sync handler: sn-irq-on, sn-hold, sn-over?, sn-frames)
\
\ VGM is the common log format for SN76489 music: Sega Master System and
\ Game Gear rips, and Furnace or DefleMask exports. The whole file is
\ loaded into RAM; the player walks it in place. .vgz (gzip) files have
\ to be unpacked on the host first.
\
\ The format (vgmrips.net VGM specification). Header fields are 32-bit
\ little-endian; every offset is relative to its own field, and a file
\ in RAM is under 64 KB, so only the low 16 bits are read.
\   $00 "Vgm "   $08 version   $0C SN76489 clock   $14 GD3 tag offset
\   $18 total samples   $1C loop offset   $20 loop samples
\   $34 data offset (version 1.50 on; before that the data is at $40)
\ Commands this player acts on:
\   $50 dd     write dd to the SN76489
\   $61 nn nn  wait nnnn samples (44100 a second)
\   $62 / $63  wait 735 / 882 samples (one 60 Hz / 50 Hz frame)
\   $7n        wait n+1 samples      $8n  YM2612 sample write, wait n
\   $66        end of data: go to the loop offset once, then stop
\   $67 ...    data block, skipped by its length
\ Every other chip's command is skipped by its length in the spec.
\
\ Timing: each 60 Hz frame is worth 735 samples. A frame runs every
\ command up to the wait that reaches past it, so a 50 Hz tune keeps its
\ tempo and writes land on the nearest frame.
\
\ Pitch: most VGMs were logged with the chip at 3579545 Hz, and the
\ GameMaster's runs at 4 MHz, which would play them about two semitones
\ sharp. The player keeps each channel's 10-bit divider as the file
\ builds it (latch byte, then a data byte, which may come alone) and
\ writes it scaled by 4000000 / clock, clamped to 1023. Noise rates 0-2
\ cannot be corrected; rate 3 follows tone 2, so it is.
\
\ Volume: the file writes every change itself, so there is no envelope.
\ A volume byte sets the channel's sn-lvl, and every frame writes all
\ four levels plus sn-atten, so the volume control and the jukebox's
\ level bars work as they do for .SNG songs.
\
\ Looping: a VGM with a loop offset would loop forever. This player
\ plays to the end, plays the looped part once more, and stops.
\
\ Usage:
\   cart-audio  sn-silence
\   tune vgm? IF tune vgm-play IF ( BREAK pressed ) THEN THEN
\ or from the field-sync interrupt, as the jukebox does:
\   1 sn-hold  tune vgm-start  sn-irq-on  0 sn-hold  ...  sn-over?

\ ── Header ─────────────────────────────────────────────────────────────

\ le@ -- the low 16 bits of a little-endian field
: le@  ( addr -- n )  DUP C@  SWAP 1 + C@ 8 LSHIFT OR ;

\ Unsigned 32 / 16 divide, quotient only; $FFFF when it would not fit.
CODE _vgm-um/  \ ( lo hi u -- q )
        LDD     2,U             ; high half: the running remainder
        CMPD    ,U
        BLO     @ok
        LDD     #$FFFF
        BRA     @out
@ok     LDY     #16
@lp     LSL     5,U             ; shift lo left, a quotient bit enters
        ROL     4,U
        ROLB
        ROLA
        BCS     @sub            ; 17-bit remainder: always over u
        CMPD    ,U
        BLO     @nx
@sub    SUBD    ,U
        INC     5,U
@nx     LEAY    -1,Y
        BNE     @lp
        LDD     4,U
@out    LEAU    4,U
        STD     ,U
        ;NEXT
;CODE

\ vgm? -- true if addr holds a VGM file with an SN76489 in it
: vgm?  ( addr -- f )
  DUP @ $5667 =  OVER 2 + @ $6D20 = AND  0= IF DROP 0 EXIT THEN
  DUP $0C + le@  SWAP $0E + le@ $3FFF AND  OR 0 <> ;

\ The SN76489 clock divided by 256: 13982 for 3579545 Hz. Bits 31-30
\ are flags (dual chip, T6W28), and real clocks stay under 16.7 MHz,
\ so the top byte is never needed.
: _vgm-clk  ( addr -- n )  $0D + le@ ;

\ samples -- frames, rounded down
: _vgm>frames  ( lo hi -- n )  735 _vgm-um/ ;

\ vgm-frames -- length in 60 Hz frames, the looped part counted twice
: vgm-frames  ( addr -- n )
  DUP $18 + le@  OVER $1A + le@  _vgm>frames
  OVER $1C + le@ IF
    OVER $20 + le@  2 PICK $22 + le@  _vgm>frames +
  THEN  SWAP DROP ;

\ ── GD3 tag: titles are UTF-16, kept here as plain ASCII ────────────────

DATA[PY _vgm-tbuf
bytes(48)
]DATA
DATA[PY _vgm-abuf
bytes(48)
]DATA

\ The first string of the tag, or 0 if the file has none.
: _gd3  ( addr -- p | 0 )
  DUP $14 + le@ ?DUP 0= IF DROP 0 EXIT THEN
  + $14 +  DUP @ $4764 = 0= IF DROP 0 EXIT THEN  12 + ;

\ Step past one zero-terminated UTF-16 string.
: _gd3-skip  ( p -- p' )  BEGIN DUP @ SWAP 2 + SWAP 0= UNTIL ;

\ Copy string i of the tag into buf as ASCII, at most 48 characters;
\ anything outside $20-$7E becomes '?'.
: _gd3-str  ( addr i buf -- c-addr u )
  >R  SWAP _gd3 ?DUP 0= IF DROP R> 0 EXIT THEN
  SWAP ?DUP IF 0 DO _gd3-skip LOOP THEN
  0 BEGIN                               ( p n )
    OVER @ 0=  OVER 48 < 0= OR IF 1 ELSE
      OVER 1 + C@ IF $3F ELSE OVER C@ THEN
      DUP $20 < OVER $7E > OR IF DROP $3F THEN
      OVER R@ + C!  1 +  SWAP 2 + SWAP  0
    THEN
  UNTIL
  SWAP DROP  R> SWAP ;

\ vgm-title -- the track name (English) from the GD3 tag, or empty
: vgm-title  ( addr -- c-addr u )  0 _vgm-tbuf _gd3-str ;

\ vgm-author -- the composer (English) from the GD3 tag, or empty
: vgm-author  ( addr -- c-addr u )  6 _vgm-abuf _gd3-str ;

\ ── Player ─────────────────────────────────────────────────────────────

VARIABLE _vgm-p      \ the next command
VARIABLE _vgm-w      \ samples left in the current wait
VARIABLE _vgm-d      \ samples this frame still has to give
VARIABLE _vgm-loop   \ loop address, 0 if none
VARIABLE _vgm-loops  \ loops still to play
VARIABLE _vgm-reg    \ last latched register: 0-7 = latch bits 6-4
VARIABLE _vgm-c      \ channel of the tone being written
VARIABLE _vgm-m      \ pitch scale, 8.8 fixed point (256 = none)
VARIABLE _vgm-pn     \ address of _vgm-div, for the step
VARIABLE _vgm-mute   \ nonzero: leave the volumes alone (seeking)

\ Each tone channel's divider as the file has built it, then (6 bytes
\ on) as last sent to the chip after scaling; big-endian.
DATA[PY _vgm-div
bytes(12)
]DATA

\ Make the field-sync handler step this player. The routine after ;NEXT
\ is the step, a subroutine run once a frame.
CODE _vgm-use  \ ( -- )
        LEAY    @step,PCR
        STY     FVAR__sn_step
        ;NEXT
@step   LDD     FVAR__vgm_w
        SUBD    #735
        BLO     @run            ; the wait ends inside this frame
        STD     FVAR__vgm_w
        LBRA    @vol
@run    COMA                    ; samples left in this frame after it
        COMB
        ADDD    #1
        STD     FVAR__vgm_d
        LDX     FVAR__vgm_p
@cmd    LDB     ,X+
        CMPB    #$50
        LBEQ    @sn
        CMPB    #$62
        BEQ     @w62
        CMPB    #$63
        BEQ     @w63
        CMPB    #$61
        BEQ     @w61
        CMPB    #$66
        LBEQ    @end
        TFR     B,A
        ANDA    #$F0
        CMPA    #$70
        BEQ     @w7
        CMPA    #$80
        BEQ     @w8
        CMPB    #$67
        LBEQ    @blk
        CMPB    #$68
        LBEQ    @l12
        CMPB    #$64
        LBEQ    @l4
        CMPB    #$4F
        LBEQ    @l2
        CMPA    #$90
        LBEQ    @c9
        CMPA    #$30
        LBLO    @cmd            ; $00-$2F: no operands
        LBEQ    @l2
        CMPA    #$60
        LBEQ    @cmd            ; $6x left over: no operands
        CMPA    #$C0
        LBLO    @l3             ; $40-$5F, $A0-$BF
        CMPA    #$E0
        LBLO    @l4             ; $C0-$DF
        LBRA    @l5             ; $E0-$FF
@w62    LDD     #735
        BRA     @wait
@w63    LDD     #882
        BRA     @wait
@w61    LDD     ,X++
        EXG     A,B
        BRA     @wait
@w7     ANDB    #$0F
        INCB
        CLRA
        BRA     @wait
@w8     ANDB    #$0F
        CLRA
@wait   CMPD    FVAR__vgm_d
        BHS     @wset           ; reaches the end of this frame
        PSHS    D
        LDD     FVAR__vgm_d
        SUBD    ,S++
        STD     FVAR__vgm_d
        LBRA    @cmd
@wset   SUBD    FVAR__vgm_d
        STD     FVAR__vgm_w
        STX     FVAR__vgm_p
        LBRA    @vol
@end    LDD     FVAR__vgm_loops
        BEQ     @stop
        LDY     FVAR__vgm_loop
        BEQ     @stop
        SUBD    #1
        STD     FVAR__vgm_loops
        TFR     Y,X
        LBRA    @cmd
@stop   LDD     #0              ; the song is over
        STD     FVAR__sn_wait
        STX     FVAR__vgm_p
        LDY     FVAR__sn_pl
        LDD     #$0F0F
        STD     ,Y
        STD     2,Y
        LBRA    @vol
@sn     LDB     ,X+             ; an SN76489 byte
        BPL     @data
        TFR     B,A             ; a latch: register = bits 6-4
        LSRA
        LSRA
        LSRA
        LSRA
        ANDA    #$07
        STA     FVAR__vgm_reg+1
        BITA    #$01
        BNE     @lvl
        CMPA    #$06
        BEQ     @noise
        LSRA                    ; tone latch: the low 4 bits
        STA     FVAR__vgm_c+1
        LSLA
        LDY     FVAR__vgm_pn
        LEAY    A,Y
        ANDB    #$0F
        LDA     1,Y
        ANDA    #$F0
        PSHS    B
        ORA     ,S+
        STA     1,Y
        BRA     @tone
@data   LDA     FVAR__vgm_reg+1 ; a data byte, for the last latch
        BITA    #$01
        BNE     @lvl
        CMPA    #$06
        BEQ     @noise
        LSRA                    ; tone data: the high 6 bits
        STA     FVAR__vgm_c+1
        LSLA
        LDY     FVAR__vgm_pn
        LEAY    A,Y
        ANDB    #$3F
        CLRA
        LSLB
        ROLA
        LSLB
        ROLA
        LSLB
        ROLA
        LSLB
        ROLA
        PSHS    D
        LDA     1,Y
        ANDA    #$0F
        ORA     1,S
        STA     1,Y
        LDA     ,S
        STA     ,Y
        LEAS    2,S
        BRA     @tone
@lvl    LSRA                    ; volume: set the channel's level
        LDY     FVAR__sn_pl
        ANDB    #$0F
        STB     A,Y
        LBRA    @cmd
@noise  ANDB    #$07
        ORB     #$E0
        STB     $FF41
        LBRA    @cmd
@tone   LDD     ,Y              ; divider x scale / 256, rounded
        PSHS    D
        LDA     1,S
        LDB     FVAR__vgm_m+1
        MUL
        ADDD    #$80
        TFR     A,B
        CLRA
        PSHS    D
        LDA     3,S
        LDB     FVAR__vgm_m
        MUL
        ADDD    ,S
        STD     ,S
        LDA     2,S
        LDB     FVAR__vgm_m+1
        MUL
        ADDD    ,S
        STD     ,S
        LDA     2,S
        LDB     FVAR__vgm_m
        MUL
        TFR     B,A
        CLRB
        ADDD    ,S
        LEAS    4,S
        CMPD    #1023
        BLS     @fits
        LDD     #1023
@fits   STD     6,Y             ; keep it for vgm-pitch
        PSHS    D               ; latch: $80 | ch << 5 | low 4 bits
        LDA     FVAR__vgm_c+1
        LSLA
        LSLA
        LSLA
        LSLA
        LSLA
        ORA     #$80
        ANDB    #$0F
        PSHS    B
        ORA     ,S+
        STA     $FF41
        PULS    D               ; data: the high 6 bits
        LSRA
        RORB
        LSRA
        RORB
        LSRA
        RORB
        LSRA
        RORB
        ANDB    #$3F
        STB     $FF41
        LBRA    @cmd
@c9     CMPB    #$92            ; DAC stream control
        BEQ     @l6
        CMPB    #$93
        BEQ     @l11
        CMPB    #$94
        BEQ     @l2
        CMPB    #$95
        LBHI    @cmd
        BRA     @l5
@blk    LDD     2,X             ; $67 $66 tt size(4): skip the block
        EXG     A,B
        LEAX    6,X
        LEAX    D,X
        LBRA    @cmd
@l2     LEAX    1,X
        LBRA    @cmd
@l3     LEAX    2,X
        LBRA    @cmd
@l4     LEAX    3,X
        LBRA    @cmd
@l5     LEAX    4,X
        LBRA    @cmd
@l6     LEAX    5,X
        LBRA    @cmd
@l11    LEAX    10,X
        LBRA    @cmd
@l12    LEAX    11,X
        LBRA    @cmd
@vol    LDA     FVAR__vgm_mute+1
        BNE     @done
        LDY     FVAR__sn_pl     ; all four levels, plus the master
        LDB     #$90
@vch    LDA     ,Y+
        ADDA    FVAR_sn_atten+1
        CMPA    #15
        BLS     @va
        LDA     #15
@va     PSHS    B
        ORA     ,S+
        STA     $FF41
        ADDB    #$20
        CMPB    #$10
        BNE     @vch
@done   RTS
;CODE

\ vgm-start -- point the player at a VGM file in RAM and reset it.
\ Hold the player (1 sn-hold) around it if the interrupt is on.
: vgm-start  ( addr -- )
  DUP 8 + le@ $150 < IF $40 ELSE
    DUP $34 + le@ ?DUP IF $34 + ELSE $40 THEN
  THEN  OVER + _vgm-p !
  DUP $1C + le@ ?DUP IF $1C + OVER + ELSE 0 THEN  _vgm-loop !
  \ scale = 4000000 / (clock / 256), unsigned, at most $3FFF
  _vgm-clk ?DUP IF
    $0900 $3D ROT _vgm-um/  DUP $C000 AND IF DROP $3FFF THEN
  ELSE 256 THEN  _vgm-m !
  1 _vgm-loops !  0 _vgm-w !  0 _vgm-reg !  0 _vgm-mute !
  _vgm-div 12 0 FILL  _vgm-div _vgm-pn !
  4 0 DO 15 I sn-lvl + C! LOOP  sn-lvl _sn-pl !
  0 _sn-tick !  1 _sn-wait !  _vgm-use ;

\ vgm-pitch -- the divider last sent to tone channel 0-2, at 4 MHz
: vgm-pitch  ( ch -- n )  2 * _vgm-div + 6 + @ ;

\ vgm-frame -- play one 60 Hz frame, polled; true once it has ended
: vgm-frame  ( -- done? )
  sn-over? IF 1 EXIT THEN
  1 _sn-tick +!  _sn-step1  sn-over? ;

\ vgm-skip -- run n frames at once with the volumes left alone, for
\ seeking. Tone writes still reach the chip; silence it first.
: vgm-skip  ( n -- )
  1 _vgm-mute !
  BEGIN
    DUP 0 >  sn-over? 0= AND IF
      1 _sn-tick +!  _sn-step1  1 -  0
    ELSE 1 THEN
  UNTIL DROP
  0 _vgm-mute ! ;

\ vgm-play -- play a VGM file to the end, polled; true if BREAK
: vgm-play  ( addr -- brk? )
  vgm-start
  BEGIN
    vsync
    vgm-frame IF sn-silence 0 EXIT THEN
    key? 3 =
  UNTIL
  sn-silence 1 ;
