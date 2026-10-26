\ orch90.fs - Orchestra-90CC stereo DAC words (#633)
\
\ Provides: orch!, orch-silence, orch-wave, orch-play, orch-hz>inc,
\           orch-frames
\
\ Requires: kernel primitives only. Wavetables come from lib/wavetable.fs
\           (gen-sine etc.): generate one, then orch-wave ! it.
\
\ Tandy's Orchestra-90CC has two 8-bit DACs, left at $FF7A and right at
\ $FF7B, write only; $80 is the middle. XRoar has it (-cart orch90), as
\ does the CoCo Zero (PIZERO-148).
\
\ orch-play synthesises one voice per side by direct digital synthesis:
\ each side has a 16-bit phase, and each sample adds that side's
\ increment and looks the high byte up in a 256-byte wavetable. One
\ sample takes 79 cycles (measured with fc.py --cycles), so on a CoCo 2
\ at 894886 Hz the rate is 11328 samples a second:
\   increment = Hz * 65536 / 11328 = Hz * 5.785    (orch-hz>inc)
\   samples   = frames * 189                       (orch-frames)
\ IRQs are masked while it plays. The phases carry over between calls,
\ so notes join without clicks; an increment of 0 holds its side still,
\ which is silence. A CoCo 3 at its fast speed plays twice as high.
\
\ Usage:
\   wave gen-sine  wave orch-wave !
\   440 orch-hz>inc  220 orch-hz>inc  30 orch-frames  orch-play

\ orch! -- write a raw sample to each DAC
: orch!  ( left right -- )  $FF7B C!  $FF7A C! ;

\ orch-silence -- both DACs to the middle
: orch-silence  ( -- )  $80 $80 orch! ;

VARIABLE orch-wave   \ the 256-byte signed wavetable both sides play
VARIABLE _orch-il    \ left and right phase increments
VARIABLE _orch-ir
VARIABLE _orch-pl    \ left and right phases, kept between calls
VARIABLE _orch-pr

\ orch-hz>inc -- phase increment for a pitch, up to 2600 Hz
: orch-hz>inc  ( hz -- inc )  DUP 5 *  SWAP 25 * 5 RSHIFT + ;

\ orch-frames -- samples in n 60 Hz frames
: orch-frames  ( n -- samples )  189 * ;

\ orch-play -- play n samples, one voice left and one right
CODE orch-play  \ ( inc-left inc-right n -- )
        LDD     4,U
        STD     FVAR__orch_il
        LDD     2,U
        STD     FVAR__orch_ir
        LDD     ,U
        LEAU    6,U
        PSHS    CC,X,U
        ORCC    #$50            ; no IRQs: the loop is the clock
        PSHS    D               ; samples left, at ,S
        BEQ     @done
        LDX     FVAR_orch_wave
        LEAX    128,X           ; A,X takes A as signed: index from the middle
        LDY     FVAR__orch_pl
        LDU     FVAR__orch_pr
@lp     TFR     Y,D             ; left
        ADDD    FVAR__orch_il
        TFR     D,Y
        LDB     A,X
        ADDB    #$80
        STB     $FF7A
        TFR     U,D             ; right
        ADDD    FVAR__orch_ir
        TFR     D,U
        LDB     A,X
        ADDB    #$80
        STB     $FF7B
        LDD     ,S
        SUBD    #1
        STD     ,S
        BNE     @lp
        STY     FVAR__orch_pl
        STU     FVAR__orch_pr
@done   LEAS    2,S
        PULS    CC,X,U
        ;NEXT
;CODE
