\ prox.fs — Spatial proximity query (Manhattan-distance bitmask)
\
\ Provides: prox-scan
\ Requires: kernel CODE infrastructure
\
\ prox-scan ( cx cy radius array count -- bitmask )
\   Scan `count` (x,y) byte pairs at `array`.  Return a 16-bit bitmask
\   with bit N set if entry N is within Manhattan distance `radius` of
\   (cx,cy).  Max 16 entries (bits 0-15).

CODE prox-scan
        PSHS    X               ; save IP
        LDB     1,U             ; B = count (low byte of TOS)
        BEQ     @zero           ; count=0 -> return 0
        LDX     2,U             ; X = array pointer
        LDA     5,U             ; A = radius (low byte)
        PSHS    A               ; S: [rad, IP]
        LDA     7,U             ; A = cy (low byte)
        PSHS    A               ; S: [cy, rad, IP]
        LDA     9,U             ; A = cx (low byte)
        PSHS    A               ; S: [cx, cy, rad, IP]
        LEAU    10,U            ; pop all 5 args
        PSHS    B               ; S: [count, cx, cy, rad, IP]
        LDY     #0              ; Y = result bitmask
        LDD     #1
        PSHS    D               ; S: [bit_hi, bit_lo, count, cx, cy, rad, IP]
@loop
        LDA     ,X+             ; A = entry x
        SUBA    3,S
        BPL     @ax
        NEGA
@ax     LDB     ,X+             ; B = entry y
        SUBB    4,S
        BPL     @ay
        NEGB
@ay     PSHS    A               ; save |dx|
        ADDB    ,S+             ; B = |dx| + |dy|, pop |dx|
        BCS     @skip
        CMPB    5,S
        BHS     @skip
        TFR     Y,D
        ORA     ,S
        ORB     1,S
        TFR     D,Y
@skip
        LSL     1,S
        ROL     ,S
        DEC     2,S
        BNE     @loop
        LEAS    6,S             ; drop loop frame
        STY     ,--U            ; push bitmask result
        PULS    X               ; restore IP
        ;NEXT
@zero
        LEAU    10,U
        LDD     #0
        STD     ,--U
        PULS    X
        ;NEXT
;CODE
