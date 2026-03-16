\ sprite.fs — Software sprite library for RG6 artifact-color mode
\
\ Provides: spr-w, spr-h (field accessors)
\           spr-draw, spr-erase-box (kernel assembly primitives)
\
\ Sprite data format (stored in memory):
\   +0  width  (1 byte) — width in artifact pixels (1-128)
\   +1  height (1 byte) — height in rows (1-192)
\   +2..  row data — 2 bits per artifact pixel, 4 pixels per byte
\         Each row is ceil(width/4) bytes, left-aligned in each byte.
\         Pixel order within byte: bits 7-6 = leftmost, bits 1-0 = rightmost.
\         Color 0 (00) = transparent, 1 = blue, 2 = red, 3 = white.

\ ── Sprite field access ──────────────────────────────────────────────────

: spr-w  ( addr -- width )   C@ ;
: spr-h  ( addr -- height )  1 + C@ ;

\ ── spr-draw ( addr x y -- ) ────────────────────────────────────────────
\ Draw sprite at (x,y).  Color 0 pixels are transparent (skipped).
\ ~60 cycles/pixel vs ~700 in ITC Forth.

CODE spr-draw
        PSHS    X               ; save IP
        LDA     3,U             ; A = x (low byte)
        STA     VAR_SPR_SX
        LDA     1,U             ; A = y (low byte)
        STA     VAR_SPR_SY
        LDY     4,U             ; Y = sprite addr
        LEAU    6,U             ; pop 3 args
        LDA     ,Y              ; width
        STA     VAR_SPR_W
        LDA     1,Y             ; height
        STA     VAR_SPR_H
        LEAY    2,Y             ; Y = sprite data start
        STY     VAR_SPR_SA
        LDA     VAR_SPR_W
        ADDA    #3
        LSRA
        LSRA
        STA     VAR_SPR_BPR
        CLR     VAR_SPR_ROW
@row
        LDA     VAR_SPR_SY
        ADDA    VAR_SPR_ROW
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        STD     VAR_SPR_VROW
        LDA     VAR_SPR_ROW
        LDB     VAR_SPR_BPR
        MUL
        ADDD    VAR_SPR_SA
        STD     VAR_SPR_SRC
        CLR     VAR_SPR_COL
@col
        LDA     VAR_SPR_COL
        LSRA
        LSRA
        LDY     VAR_SPR_SRC
        LDA     A,Y
        LDB     VAR_SPR_COL
        ANDB    #$03
        ASLB
        NEGB
        ADDB    #6
        STB     VAR_SPR_SHIFT
        TSTB
        BEQ     @nosr
@sr     LSRA
        DECB
        BNE     @sr
@nosr   ANDA    #$03
        BEQ     @skip
        PSHS    A
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL
        PSHS    A
        LSRA
        LSRA
        LDY     VAR_SPR_VROW
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        LDB     ,S
        TSTA
        BEQ     @ncs
@csh    ASLB
        DECA
        BNE     @csh
@ncs    STB     ,S
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        LDB     #$03
        TSTA
        BEQ     @nms
@msh    ASLB
        DECA
        BNE     @msh
@nms    COMB
        ANDB    ,Y
        ORB     ,S+
        STB     ,Y
@skip
        INC     VAR_SPR_COL
        LDA     VAR_SPR_COL
        CMPA    VAR_SPR_W
        BNE     @col
        INC     VAR_SPR_ROW
        LDA     VAR_SPR_ROW
        CMPA    VAR_SPR_H
        LBNE    @row
        PULS    X
        ;NEXT
;CODE

\ ── spr-erase-box ( addr x y -- ) ─────────────────────────────────────
\ Erase sprite bounding box to black (color 0).  ~35 cycles/pixel.

CODE spr-erase-box
        PSHS    X               ; save IP
        LDA     3,U             ; A = x (low byte)
        STA     VAR_SPR_SX
        LDA     1,U             ; A = y (low byte)
        STA     VAR_SPR_SY
        LDY     4,U             ; Y = sprite addr
        LEAU    6,U             ; pop 3 args
        LDA     ,Y              ; width
        STA     VAR_SPR_W
        LDA     1,Y             ; height
        STA     VAR_SPR_H
        CLR     VAR_SPR_ROW
@row
        LDA     VAR_SPR_SY
        ADDA    VAR_SPR_ROW
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        STD     VAR_SPR_VROW
        CLR     VAR_SPR_COL
@col
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL
        PSHS    A
        LSRA
        LSRA
        LDY     VAR_SPR_VROW
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        LDB     #$03
        TSTA
        BEQ     @nms
@msh    ASLB
        DECA
        BNE     @msh
@nms    COMB
        ANDB    ,Y
        STB     ,Y
        INC     VAR_SPR_COL
        LDA     VAR_SPR_COL
        CMPA    VAR_SPR_W
        BNE     @col
        INC     VAR_SPR_ROW
        LDA     VAR_SPR_ROW
        CMPA    VAR_SPR_H
        LBNE    @row
        PULS    X
        ;NEXT
;CODE
