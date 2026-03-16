\ rg-text.fs — RG mode text rendering (1 bit/pixel)
\
\ Provides: cv (variable), cb (variable), rg-char (CODE word)
\ Requires: font library (font5x7.fs or font-art.fs)
\
\ rg-char uses kernel config variables at fixed RAM addresses:
\   $75 = font base (FDB)    $77 = min char (FCB)
\   $78 = glyph size (FCB)   $79 = nrows (FCB)
\   $7A = bpr (FCB)          $7B = row height (FCB)
\
\ Before calling rg-char, write the config variables directly.
\ cv and cb are still available for other uses (e.g. vdg-modes).
\
\ Usage:
\   INCLUDE lib/font5x7.fs
\   INCLUDE lib/rg-text.fs
\   init-font
\   $3000 cv !  16 cb !
\   $3000 $57 !  $6000 $75 !  $20 $77 C!  7 $78 C!  7 $79 C!
\   16 $7A C!  8 $7B C!
\   CHAR A 2 1 rg-char

VARIABLE cv                       \ cached VRAM base address
VARIABLE cb                       \ cached bytes per row

\ ── rg-char ( char cx cy -- ) ──────────────────────────────────────────
\ Render a font glyph into RG-mode VRAM.  ~70 cycles vs ~900 in Forth.

CODE rg-char
        PSHS    X               ; save IP
        ; Pop args: U+0,1=cy  U+2,3=cx  U+4,5=char
        LDA     1,U             ; A = cy
        LDB     VAR_RGROWH      ; B = row height (8 or 10)
        MUL
        TFR     B,A             ; A = pixel row
        LDB     VAR_RGBPR       ; B = bytes per VRAM row
        MUL
        ADDB    3,U             ; add cx
        ADCA    #0
        ADDD    VAR_RGVRAM
        PSHS    D               ; save dest
        LDA     5,U             ; A = char
        CMPA    VAR_RGCHARMIN
        BHS     @over
        LDA     VAR_RGCHARMIN
@over   SUBA    VAR_RGCHARMIN
        LDB     VAR_RGGLYPHSZ
        MUL
        ADDD    VAR_RGFONT
        TFR     D,Y             ; Y = glyph source
        LDB     VAR_RGNROWS     ; B = row counter
        LDX     ,S++            ; X = dest
@copy   LDA     ,Y+
        STA     ,X
        LDA     VAR_RGBPR
        LEAX    A,X
        DECB
        BNE     @copy
        LEAU    6,U             ; pop 3 data stack items
        PULS    X               ; restore IP
        ;NEXT
;CODE
