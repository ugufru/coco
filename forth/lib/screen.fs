\ screen.fs — VDG screen utilities and vsync
\
\ Provides: vsync, cls-black, cls-green
\ Requires: kernel primitives C@, AND, DROP, AT
\
\ vsync waits for the VDG vertical sync signal (60 Hz).
\ PIA0 CB1 flag ($FF03 bit 7) is set by VDG; reading $FF02 clears it.

: vsync  ( -- )
  BEGIN  $FF03 C@ $80 AND  UNTIL
  $FF02 C@ DROP ;

\ wait-past-row ( row -- )
\ After VSYNC, poll HSYNC to wait until the beam has passed the given
\ display row (0-191).  Adds a blanking offset so row 0 means "beam
\ just entered the active display area."
\ Uses: $FF01 bit 7 = HSYNC flag, cleared by reading $FF00.
\ Cost: ~20cy per line waited.  Row 96 costs ~2,600cy.
CODE wait-past-row
        PSHS    X
        LDB     1,U             ; row (0-191)
        LEAU    2,U             ; pop arg
        ADDB    #35             ; add top blanking lines
        BEQ     @done           ; 0 lines to wait (shouldn't happen)
@wt     LDA     $FF01           ; check HSYNC flag (bit 7)
        BPL     @wt             ; not set yet — keep polling
        LDA     $FF00           ; clear flag (read PIA0-A data)
        DECB
        BNE     @wt             ; count down
@done   PULS    X
        ;NEXT
;CODE

\ count-blanking ( -- n )
\ Diagnostic: after VSYNC, count HSYNC pulses until we've waited
\ a fixed number of lines.  Returns the count.  Use to verify
\ the blanking interval length on real hardware / XRoar.
\ Waits for VSYNC first, then counts 100 HSYNC pulses (should take
\ ~6.35ms).  The real diagnostic: call this, then check if VRAM
\ writes during the first N lines cause tearing.
CODE count-blanking
        PSHS    X
        ; Wait for VSYNC
@vs     LDA     $FF03           ; poll VSYNC flag
        BPL     @vs
        LDA     $FF02           ; clear VSYNC flag
        ; Now count HSYNC pulses for 100 lines
        CLRA
        CLRB                    ; D = 0 (counter)
        LDY     #100            ; count 100 HSYNC pulses
@hs     LDA     $FF01           ; check HSYNC flag
        BPL     @hs
        LDA     $FF00           ; clear flag
        ADDD    #1              ; increment counter (always counts to 100)
        LEAY    -1,Y
        BNE     @hs
        ; D = 100 (confirmation that we counted 100 lines)
        ; The real test: did we see tearing during those 100 lines?
        LEAU    -2,U
        STD     ,U              ; push count
        PULS    X
        ;NEXT
;CODE

: cls-black  ( -- )
  512 0 DO  $80 $0400 I + C!  LOOP ;

: cls-green  ( -- )
  512 0 DO  $60 $0400 I + C!  LOOP
  0 0 AT ;
