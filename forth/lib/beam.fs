\ beam.fs — Beam pixel-save/restore system for artifact-free rendering
\
\ Provides: beam-trace, beam-draw-slice (kernel primitives)
\           beam-restore-slice (CODE word, defined here)
\ Requires: kernel (VAR_BEAM_BUF, VAR_BEAM_VRAM, VAR_BEAM_CNT,
\           VAR_LINE_*, VAR_RGVRAM), rg-pixel.fs
\
\ Path buffer format: 3 bytes per pixel
\   Offset 0: x  (1 byte, artifact pixel x 0-127)
\   Offset 1: y  (1 byte, screen row y 0-191)
\   Offset 2: c  (1 byte, original 2-bit color in low bits)
\
\ beam-trace ( x1 y1 x2 y2 buf -- count )         — kernel primitive
\ beam-draw-slice ( buf start count color -- )    — kernel primitive
\ beam-restore-slice ( buf start count -- )       — CODE word (below)

CODE beam-restore-slice
        PSHS    X
        LDD     ,U
        STD     VAR_BEAM_CNT
        LDD     2,U
        PSHS    D
        ASLB
        ROLA
        ADDD    ,S++
        ADDD    4,U
        STD     VAR_BEAM_BUF
        LEAU    6,U
        LDD     VAR_BEAM_CNT
        BEQ     @done
        LDX     VAR_BEAM_BUF
@ploop  LDA     ,X
        LDB     1,X
        PSHS    X
        PSHS    A
        LDA     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     ,S
        LSRA
        LSRA
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDX     1,S
        LDA     2,X
        ANDA    #$03
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        PULS    X
        LEAX    3,X
        LDD     VAR_BEAM_CNT
        SUBD    #1
        STD     VAR_BEAM_CNT
        BNE     @ploop
@done   PULS    X
        ;NEXT
;CODE
