\ sound.fs — DAC sound for the CoCo (all-RAM compatible)
\
\ Sound subroutines live in the bootstrap area ($0Exx, always RAM).
\ They switch to ROM mode, play DAC audio, then restore all-RAM.
\ CODE words here just set up params and JSR to the subroutines.

\ ── snd-tone: square wave via bootstrap subroutine ────────────────────
\ ( pitch duration -- )
CODE snd-tone
        PSHS    X
        LDA     3,U             ; A = pitch
        LDB     1,U             ; B = duration
        LEAU    4,U
        JSR     SND_PLAY
        PULS    X
        ;NEXT
;CODE

\ ── snd-noise: noise burst via bootstrap subroutine ───────────────────
\ ( duration -- )
CODE snd-noise
        PSHS    X
        LDA     1,U
        LEAU    2,U
        JSR     SND_NOISE
        PULS    X
        ;NEXT
;CODE

\ ── Convenience words ─────────────────────────────────────────────────

: snd-beep  ( -- )  4 80 snd-tone ;
: snd-zap  ( -- )  20 255 snd-tone  40 255 snd-tone ;
: snd-boom  ( -- )  255 snd-noise ;
: snd-chirp  ( -- )  30 80 snd-tone  15 80 snd-tone ;
: snd-dock  ( -- )  6 60 snd-tone  10 60 snd-tone ;
: snd-hit  ( -- )  20 40 snd-tone ;
