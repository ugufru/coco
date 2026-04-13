\ sprtest.fs — Jovian sprite visual tuning harness (#189)
\
\ Exhaustively generates all 256 appearance seeds (genome byte 2)
\ and renders them in grids on the RG6 display for evaluation via
\ screen capture MCP.
\
\ Controls:
\   Any key = next page
\   Pages: 4 pages white, 4 pages blue, 4 pages red (12 screens total)
\
\ Grid: 8 columns x 9 rows = 72 sprites per page
\ 4 pages x 72 = 288 (covers all 256 seeds per color pass)

\ ── Libraries ───────────────────────────────────────────────────────────

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs
INCLUDE ../../forth/lib/datawrite.fs
INCLUDE ../../forth/lib/sprite.fs
INCLUDE ../../forth/lib/rng.fs
INCLUDE ../../forth/lib/font-art.fs
INCLUDE ../../forth/lib/rg-text.fs

\ ── Constants ───────────────────────────────────────────────────────────

$8200 CONSTANT SPR-BUF              \ 23-byte sprite buffer

\ Grid layout:
\   128 artifact px wide ÷ 16 = 8 columns
\   192 pixel rows: 8 rows × 20px = 160px (32px spare at bottom)
\   Each cell: 16×20 pixels
\   Label at text (col*4, row*2)     — pixel y = row*20  (glyph: y+0 to y+7)
\   Sprite at pixel (col*16+4, row*20+10) — 2px below label bottom
\   64 sprites per page, 4 pages = 256 seeds
8 CONSTANT GCOLS
8 CONSTANT GROWS

VARIABLE cur-color                  \ forced 2bpp color: 1=blue 2=red 3=white

\ ── Text helpers (same pattern as spacewarp.fs) ────────────────────────

VARIABLE tcx
VARIABLE tcy
: at-xy  ( cx cy -- )  tcy ! tcx ! ;
: rg-emit  ( char -- )  tcx @ tcy @ rg-char  1 tcx +! ;
: rg-u.  ( u -- )  10 /MOD ?DUP IF rg-u. THEN  CHAR 0 + rg-emit ;
: rg-type  ( addr len -- )  0 DO DUP I + C@ rg-emit LOOP DROP ;

: init-text  ( -- )
  init-font
  rv @ cv !  32 cb !
  rv @ KVAR-RGVRAM !
  $9000 KVAR-RGFONT !
  $20 KVAR-RGCHARMIN C!
  8 KVAR-RGGLYPHSZ C!
  8 KVAR-RGNROWS C!
  32 KVAR-RGBPR C!
  10 KVAR-RGROWH C!
  $F8 set-pia ;

\ ── Sprite generation (extracted from spacewarp.fs gen-jov-sprite) ─────
\ gen-test-sprite ( seed color -- )
\ Generates a sprite into SPR-BUF ($8200) from appearance seed and
\ forced 2bpp color.  Uses JOV-SPRWORK scratch at $80E4-$80EF.

CODE gen-test-sprite   \ ( seed color -- )
        PSHS    X               ; save IP
        LDA     1,U             ; A = color (low byte)
        STA     $80E6           ; forced 2bpp color
        LDA     3,U             ; A = seed (low byte)
        LEAU    4,U             ; pop 2 args
        STA     $80E9           ; PRNG state
        ; --- Sprite buffer ---
        LDD     #$8200
        STD     $80E4           ; SPRWORK+0 = sprite addr
        ; --- Width from bits 7-6: 00=5, 01=7, 10=7, 11=9 ---
        LDA     $80E9
        TFR     A,B             ; save seed in B
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA
        LSRA                    ; A = seed >> 6
        CMPA    #0
        BNE     @nw5
        LDA     #5
        BRA     @wdn
@nw5    CMPA    #3
        BNE     @w7
        LDA     #9
        BRA     @wdn
@w7     LDA     #7
@wdn    STA     $80EA           ; width
        LDX     $80E4
        STA     ,X              ; sprite header byte 0
        ; --- Height from bits 5-4: 00/01=5, 10/11=7 ---
        TFR     B,A             ; restore seed
        LSRA
        LSRA
        LSRA
        LSRA
        ANDA    #$03
        CMPA    #2
        BHS     @h7
        LDA     #5
        BRA     @hdn
@h7     LDA     #7
@hdn    STA     $80EB           ; height
        STA     1,X             ; sprite header byte 1
        ; --- Half-width ---
        LDA     $80EA
        INCA
        LSRA
        STA     $80E7           ; half_width
        ; === Clear sprite data ===
        LDX     $80E4
        LEAX    2,X
        LDA     $80EA
        ADDA    #3
        LSRA
        LSRA                    ; A = bpr
        LDB     $80EB
        MUL
        TSTB
        BEQ     @clrdn
@clrlp  CLR     ,X+
        DECB
        BNE     @clrlp
@clrdn
        ; === Per-row pixel generation ===
        CLR     $80ED           ; row = 0
@rowlp  LDB     $80E9           ; PRNG state
        LDA     #5
        MUL
        ADDB    #3
        STB     $80E9
        LDA     $80ED
        STA     $80EF           ; row for @setpx
        CLR     $80EC           ; col = 0
@collp  LDA     $80E9           ; state
        LDB     $80EC
        BEQ     @nsh
@shlp   LSRA
        DECB
        BNE     @shlp
@nsh    BITA    #$01
        BEQ     @nopx
        LDA     $80EC
        STA     $80EE           ; col for @setpx
        BSR     @setpx
        LDA     $80E7           ; half_width
        DECA
        CMPA    $80EC           ; center?
        BEQ     @nopx
        LDA     $80EA           ; width
        DECA
        SUBA    $80EC           ; mirror col
        STA     $80EE
        BSR     @setpx
@nopx   INC     $80EC
        LDA     $80EC
        CMPA    $80E7
        BCS     @collp
        INC     $80ED
        LDA     $80ED
        CMPA    $80EB
        BCS     @rowlp
        ; === Center column guarantee ===
        LDA     $80E7
        DECA
        STA     $80EE
        LDA     $80EB
        LSRA
        STA     $80EF
        BSR     @setpx
        ;
        PULS    X
        ;NEXT
        ;
@setpx  LDA     $80EA
        ADDA    #3
        LSRA
        LSRA
        LDB     $80EF
        MUL
        PSHS    D
        LDB     $80EE
        LSRB
        LSRB
        CLRA
        ADDD    ,S++
        LDX     $80E4
        LEAX    2,X
        LEAX    D,X
        LDA     $80EE
        ANDA    #$03
        NEGA
        ADDA    #3
        ASLA
        LDB     $80E6
        TSTA
        BEQ     @sns
@ssh    ASLB
        DECA
        BNE     @ssh
@sns    ORB     ,X
        STB     ,X
        RTS
;CODE

\ ── Page rendering ──────────────────────────────────────────────────────

\ Print 3-digit decimal with leading zeros
: rg-u.3  ( u -- )
  DUP 100 < IF CHAR 0 rg-emit THEN
  DUP 10 < IF CHAR 0 rg-emit THEN
  rg-u. ;

\ Render one page of 72 sprites starting at seed-start
: draw-page  ( seed-start -- )
  rv @ 6144 0 FILL                  \ clear screen
  GROWS 0 DO
    GCOLS 0 DO
      DUP 256 < IF
        \ Generate sprite
        DUP cur-color @ gen-test-sprite
        \ I = col (inner), J = row (outer)
        \ Label at text (col*4, row*2) = pixel y = row*20
        DUP
        I 4 *
        J 2 *
        at-xy rg-u.3
        \ Sprite at pixel (col*16+4, row*20+10)
        SPR-BUF
        I 16 * 4 +
        J 20 * 10 +
        spr-draw
      THEN
      1 +
    LOOP
  LOOP
  DROP ;

\ ── Main ────────────────────────────────────────────────────────────────

VARIABLE page-seed

\ Wait ~8 seconds (480 frames at 60Hz)
: wait-page  ( -- )  480 0 DO vsync LOOP ;

: show-color  ( color -- )
  cur-color !
  0 page-seed !
  BEGIN
    page-seed @ 256 < IF
      page-seed @ draw-page
      wait-page
      page-seed @ GCOLS GROWS * + page-seed !
    THEN
    page-seed @ 256 < 0=
  UNTIL ;


\ Show a single page: given color and starting seed
: show-page  ( color seed -- )
  SWAP cur-color !
  draw-page
  wait-page ;

: main  ( -- )
  $8000 256 0 FILL
  rg-init
  init-text

  1 64 show-page                    \ blue 064-127
  BEGIN AGAIN ;

main
