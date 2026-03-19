\ beam.fs — Beam pixel-save/restore system for artifact-free rendering
\
\ Provides: beam-trace, beam-draw-slice, beam-restore-slice
\ Requires: kernel (VAR_BEAM_BUF, VAR_BEAM_VRAM, VAR_BEAM_CNT,
\           VAR_LINE_*, VAR_RGVRAM), rg-pixel.fs
\
\ Path buffer format: 3 bytes per pixel
\   Offset 0: x  (1 byte, artifact pixel x 0-127)
\   Offset 1: y  (1 byte, screen row y 0-191)
\   Offset 2: c  (1 byte, original 2-bit color in low bits)
\
\ beam-trace walks a Bresenham line and records (x, y, saved_color)
\ into a path buffer WITHOUT drawing.  beam-draw-slice and
\ beam-restore-slice operate on slices of the buffer for animation.

\ ── beam-trace ( x1 y1 x2 y2 buf -- count ) ────────────────────────────
\ Walk Bresenham from (x1,y1) to (x2,y2).  At each pixel, read the
\ current VRAM color and store (x, y, color) triplet into buf.
\ Returns total pixel count.  Does NOT draw anything.
\
\ Uses VAR_LINE_* for Bresenham state (same as rg-line).
\ Uses VAR_BEAM_BUF for buffer pointer, VAR_BEAM_CNT for count.

CODE beam-trace
        PSHS    X               ; save IP
        ; Load args: U+0,1=buf U+2,3=y2 U+4,5=x2 U+6,7=y1 U+8,9=x1
        LDD     ,U              ; buf
        STD     VAR_BEAM_BUF
        LDA     5,U             ; x2
        STA     VAR_LINE_X2
        LDA     3,U             ; y2
        STA     VAR_LINE_Y2
        LDA     9,U             ; x1
        STA     VAR_LINE_CX
        LDA     7,U             ; y1
        STA     VAR_LINE_CY
        LEAU    10,U            ; pop 5 args
        ; Initialize pixel count = 0
        LDD     #0
        STD     VAR_BEAM_CNT
        ; dx = |x2-x1|, sx = sign(x2-x1)
        CLRA
        LDB     VAR_LINE_CX
        PSHS    D
        CLRA
        LDB     VAR_LINE_X2
        SUBD    ,S++
        BPL     @sx_p
        COMA
        COMB
        ADDD    #1
        STB     VAR_LINE_DX
        LDA     #$FF
        STA     VAR_LINE_SX
        BRA     @sx_d
@sx_p   STB     VAR_LINE_DX
        LDA     #$01
        STA     VAR_LINE_SX
@sx_d
        ; dy = |y2-y1|, sy = sign(y2-y1)
        CLRA
        LDB     VAR_LINE_CY
        PSHS    D
        CLRA
        LDB     VAR_LINE_Y2
        SUBD    ,S++
        BPL     @sy_p
        COMA
        COMB
        ADDD    #1
        STB     VAR_LINE_DY
        LDA     #$FF
        STA     VAR_LINE_SY
        BRA     @sy_d
@sy_p   STB     VAR_LINE_DY
        LDA     #$01
        STA     VAR_LINE_SY
@sy_d
        ; err = dx - dy (signed 16-bit)
        CLRA
        LDB     VAR_LINE_DX
        STD     VAR_LINE_ERR
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
@loop
        ; ── Read pixel at (CX, CY) and store to buffer ──
        ; Compute VRAM address
        LDA     VAR_LINE_CY
        LDB     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM
        TFR     D,Y             ; Y = row start
        LDA     VAR_LINE_CX
        LSRA
        LSRA                    ; A = x / 4
        LEAY    A,Y             ; Y = VRAM byte address
        ; Compute shift count: 6 - (x%4)*2
        LDA     VAR_LINE_CX
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        ; Read current pixel color
        LDB     ,Y              ; B = VRAM byte
        PSHS    A               ; save shift count
        BEQ     @nr             ; shift=0, already in low bits
@rsh    LSRB
        DECA
        BNE     @rsh
@nr     ANDB    #$03            ; B = 2-bit color
        LEAS    1,S             ; pop shift count
        ; Store (x, y, color) triplet to buffer
        LDY     VAR_BEAM_BUF
        LDA     VAR_LINE_CX
        STA     ,Y+             ; store x
        LDA     VAR_LINE_CY
        STA     ,Y+             ; store y
        STB     ,Y+             ; store saved color
        STY     VAR_BEAM_BUF    ; advance buffer pointer
        ; Increment count
        LDD     VAR_BEAM_CNT
        ADDD    #1
        STD     VAR_BEAM_CNT
        ; ── Done check ──
        LDA     VAR_LINE_CX
        CMPA    VAR_LINE_X2
        BNE     @step
        LDA     VAR_LINE_CY
        CMPA    VAR_LINE_Y2
        BEQ     @done
@step
        ; e2 = 2 * err
        LDD     VAR_LINE_ERR
        ASLB
        ROLA
        STD     VAR_LINE_E2
        ; if e2 > -dy: err -= dy, cx += sx
        CLRA
        LDB     VAR_LINE_DY
        COMA
        COMB
        ADDD    #1
        CMPD    VAR_LINE_E2
        BGE     @nosx
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CX
        ADDA    VAR_LINE_SX
        STA     VAR_LINE_CX
@nosx
        ; if e2 < dx: err += dx, cy += sy
        CLRA
        LDB     VAR_LINE_DX
        CMPD    VAR_LINE_E2
        BLE     @nosy
        CLRA
        LDB     VAR_LINE_DX
        ADDD    VAR_LINE_ERR
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CY
        ADDA    VAR_LINE_SY
        STA     VAR_LINE_CY
@nosy
        LBRA    @loop
@done
        ; Push pixel count as result
        LDD     VAR_BEAM_CNT
        STD     ,--U
        PULS    X               ; restore IP
        ;NEXT
;CODE

\ ── beam-draw-slice ( buf start count color -- ) ────────────────────────
\ Draw count pixels starting at index start in the path buffer,
\ all in the given color.  Each buffer entry is 3 bytes (x, y, c).

CODE beam-draw-slice
        PSHS    X               ; save IP
        LDA     1,U             ; A = color (low byte of TOS)
        ANDA    #$03
        STA     VAR_LINE_COL    ; stash color
        LDD     2,U             ; D = count
        STD     VAR_BEAM_CNT
        ; Compute buffer start: buf + start*3
        ; start*3 = start + start*2
        LDD     4,U             ; D = start index
        PSHS    D               ; save start on S
        ASLB
        ROLA                    ; D = start*2
        ADDD    ,S++            ; D = start*3 (pop saved start)
        ADDD    6,U             ; D = buf + start*3
        STD     VAR_BEAM_BUF
        LEAU    8,U             ; pop 4 args
        ; Check count
        LDD     VAR_BEAM_CNT
        BEQ     @done           ; count=0, skip
        LDX     VAR_BEAM_BUF    ; X = buffer pointer
@ploop
        ; Load (x, y) from buffer entry
        LDA     ,X              ; A = x
        LDB     1,X             ; B = y
        PSHS    X               ; save buffer pointer
        ; Compute VRAM address: RGVRAM + y*32 + x/4
        PSHS    A               ; save x
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM
        TFR     D,Y             ; Y = row start
        LDA     ,S              ; A = x
        LSRA
        LSRA                    ; A = x/4
        LEAY    A,Y             ; Y = VRAM byte addr
        ; Shift = 6 - (x%4)*2
        LDA     ,S+             ; A = x, pop
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        PSHS    A               ; save shift
        ; Shift color into position
        LDA     VAR_LINE_COL
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A               ; save shifted color
        ; Build clear mask
        LDA     #$03
        LDB     1,S             ; shift count
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA                    ; A = clear mask
        ANDA    ,Y              ; clear old pixel
        ORA     ,S              ; OR in new color
        STA     ,Y              ; write back
        LEAS    2,S             ; clean shift+color
        ; Advance
        PULS    X               ; restore buffer pointer
        LEAX    3,X             ; next entry (3 bytes per pixel)
        LDD     VAR_BEAM_CNT
        SUBD    #1
        STD     VAR_BEAM_CNT
        BNE     @ploop
@done
        PULS    X               ; restore IP
        ;NEXT
;CODE

\ ── beam-restore-slice ( buf start count -- ) ───────────────────────────
\ Restore count pixels starting at index start in the path buffer.
\ Each entry's saved color (byte 2) is written back to (x, y).

CODE beam-restore-slice
        PSHS    X               ; save IP
        LDD     ,U              ; D = count
        STD     VAR_BEAM_CNT
        ; Compute buffer start: buf + start*3
        LDD     2,U             ; D = start index
        PSHS    D               ; save start on S
        ASLB
        ROLA                    ; D = start*2
        ADDD    ,S++            ; D = start*3 (pop saved start)
        ADDD    4,U             ; D = buf + start*3
        STD     VAR_BEAM_BUF
        LEAU    6,U             ; pop 3 args
        ; Check count
        LDD     VAR_BEAM_CNT
        BEQ     @done           ; count=0, skip
        LDX     VAR_BEAM_BUF    ; X = buffer pointer
@ploop
        ; Load (x, y, saved_color) from buffer entry
        LDA     ,X              ; A = x
        LDB     1,X             ; B = y
        PSHS    X               ; save buffer pointer
        ; Compute VRAM address
        PSHS    A               ; save x
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM
        TFR     D,Y             ; Y = row start
        LDA     ,S              ; A = x
        LSRA
        LSRA                    ; A = x/4
        LEAY    A,Y             ; Y = VRAM byte addr
        ; Shift = 6 - (x%4)*2
        LDA     ,S+             ; A = x, pop
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        PSHS    A               ; save shift
        ; Get saved color from buffer entry byte 2
        LDX     1,S             ; X = buffer pointer (saved on S)
        LDA     2,X             ; A = saved color (0-3)
        ANDA    #$03
        LDB     ,S              ; B = shift count
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A               ; save shifted color
        ; Build clear mask
        LDA     #$03
        LDB     1,S             ; shift count
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA                    ; A = clear mask
        ANDA    ,Y              ; clear old pixel
        ORA     ,S              ; OR in saved color
        STA     ,Y              ; write back
        LEAS    2,S             ; clean shift+color
        ; Advance
        PULS    X               ; restore buffer pointer
        LEAX    3,X             ; next entry
        LDD     VAR_BEAM_CNT
        SUBD    #1
        STD     VAR_BEAM_CNT
        BNE     @ploop
@done
        PULS    X               ; restore IP
        ;NEXT
;CODE
