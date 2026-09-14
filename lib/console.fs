\ console.fs - Scrolling text console with MC6847T1 true lowercase
\
\ The CoCo 2B's MC6847T1 VDG has a real lowercase character set.  Setting
\ PIA1 $FF22 bit 4 (GM0) drives the T1 INT/EXT pin, and the VDG then uses
\ all 7 bits of each text byte as the glyph index, in normal video:
\
\   screen $01-$1A  lowercase a-z
\   screen $40-$5F  @ A-Z [ \ ] ^ _
\   screen $60-$7F  space, digits, punctuation
\   screen $20-$3F  inverse digits/punctuation ($20 = solid block)
\
\ So ASCII maps to screen codes as:
\   c >= $60        c AND $1F
\   $40 <= c < $60  c
\   $20 <= c < $40  c OR $40
\
\ Plain 6847 machines (CoCo 1, early CoCo 2) ignore bit 4 in text mode, so
\ lowercase shows as inverse-video garbage there.  Run on coco2bus.
\
\ The kernel EMIT is uppercase-only and wraps instead of scrolling; the
\ ROM-mode kernel has no room left to grow it (#554, #555), so this console
\ lives here as a CODE word.  It shares the kernel cursor (VAR_CUR), so AT
\ still positions it.
\
\ Provides: lower-on, lower-off, con-emit, con-type, con-cls
\ Requires: kernel symbols SCREEN, NSCR, VAR_CUR
\
\ lower-on  ( -- )          enable T1 lowercase (set $FF22 bit 4)
\ lower-off ( -- )          back to the standard character set
\ con-emit  ( c -- )        print c: $20-$7E drawn, $0D CR, $08 BS,
\                           $0C clear; other control bytes ignored.
\                           Scrolls up one row past the bottom line.
\ con-type  ( addr len -- ) con-emit each byte of a string
\ con-cls   ( -- )          clear screen, home cursor


: lower-on   ( -- )  $FF22 C@ $10 OR  $FF22 C! ;
: lower-off  ( -- )  $FF22 C@ $EF AND $FF22 C! ;


CODE con-emit
        ;;; ( c -- )  T1-lowercase console output with scroll
        LDB     1,U                     ; B = character
        LEAU    2,U                     ; drop it
        PSHS    X                       ; save IP, X is our cursor
        CMPB    #$0D
        BEQ     @cr
        CMPB    #$08
        BEQ     @bs
        CMPB    #$0C
        BEQ     @cls
        CMPB    #$20
        BLO     @done                   ; other control bytes: ignore
        CMPB    #$7F
        BHS     @done                   ; DEL and 8-bit: ignore
        CMPB    #$60
        BLO     @notlc
        ANDB    #$1F                    ; $60-$7E -> $00-$1E (lowercase)
        BRA     @put
@notlc  CMPB    #$40
        BHS     @put                    ; $40-$5F unchanged
        ORB     #$40                    ; $20-$3F -> $60-$7F
@put    LDX     VAR_CUR
        STB     SCREEN,X
        LEAX    1,X
        CMPX    #NSCR
        BLO     @setcur
        BSR     @scroll                 ; X = start of bottom row
@setcur STX     VAR_CUR
@done   PULS    X                       ; restore IP
        ;NEXT
        ;;; CR: column 0 of the next row, scrolling off the bottom
@cr     LDD     VAR_CUR
        ADDD    #32
        ANDB    #$E0                    ; round down to row start
        TFR     D,X
        CMPX    #NSCR
        BLO     @setcur
        BSR     @scroll
        BRA     @setcur
        ;;; BS: back one cell and blank it (stops at top-left)
@bs     LDX     VAR_CUR
        BEQ     @done
        LEAX    -1,X
        LDA     #$60
        STA     SCREEN,X
        BRA     @setcur
        ;;; CLS: fill with spaces, home
@cls    LDX     #SCREEN
        LDA     #$60
@clsl   STA     ,X+
        CMPX    #SCREEN+NSCR
        BLO     @clsl
        LDX     #0
        BRA     @setcur
        ;;; scroll: rows 1-15 up one, blank row 15, return X = 480
@scroll LDX     #SCREEN
@sclp   LDA     32,X
        STA     ,X+
        CMPX    #SCREEN+NSCR-32
        BLO     @sclp
        LDA     #$60
@sclr   STA     ,X+
        CMPX    #SCREEN+NSCR
        BLO     @sclr
        LDX     #NSCR-32
        RTS
;CODE


: con-type  ( addr len -- )
  DUP IF
    OVER + SWAP DO  I C@ con-emit  LOOP
  ELSE
    2DROP
  THEN ;

: con-cls  ( -- )  $0C con-emit ;
