;;; forth/kernel/kernel.asm
;;;
;;; CoCo Forth Executor Kernel — Hello World prototype
;;; Assembled with lwasm (lwtools)
;;;
;;; Register convention:
;;;   X  IP  (instruction pointer into the thread)
;;;   U  DSP (data stack pointer, grows downward)
;;;   S  RSP (return stack pointer, grows downward)
;;;   Y  scratch (NEXT and primitives)
;;;   D  scratch accumulator (A = high byte, B = low byte)
;;;
;;; Threading model: Indirect Threaded Code (ITC)
;;;   - Each word has a Code Field Address (CFA): a 2-byte pointer to machine code
;;;   - The thread is a sequence of CFA addresses
;;;   - NEXT fetches the next CFA address, jumps through it
;;;
;;; NEXT (inlined at end of every primitive):
;;;     LDY  ,X++    ; fetch CFA address from thread, advance IP by 2
;;;     JMP  [,Y]    ; jump to machine code via CFA (indirect)

        PRAGMA  6809

SCREEN  EQU     $0400           ; video RAM base (32×16 alphanumeric text)
NSCR    EQU     512             ; 32 cols × 16 rows

;;; ─── Variables ───────────────────────────────────────────────────────────────
;;; Placed in low RAM, accessed via extended addressing.
;;; (Can be moved to direct page later for a 1-cycle saving per access.)

        ORG     $0050

VAR_CUR         FDB     0       ; cursor offset into video RAM (0–511)
VAR_KEY_PREV    FCB     0       ; last accepted key ASCII (KEY debounce)
VAR_KEY_SHIFT   FCB     0       ; SHIFT flag (nonzero = shift held)
VAR_KEY_RELCNT  FCB     0       ; release debounce counter
VAR_KEY_REPDLY  FDB     0       ; auto-repeat countdown (16-bit)
VAR_RGVRAM      FDB     $5000   ; RG6 VRAM base address (written by rg-init)
;;; Bresenham line drawing scratch (used by CODE_RGLINE)
VAR_LINE_CX     FCB     0       ; current x
VAR_LINE_CY     FCB     0       ; current y
VAR_LINE_X2     FCB     0       ; target x
VAR_LINE_Y2     FCB     0       ; target y
VAR_LINE_SX     FCB     0       ; step x (+1 or -1)
VAR_LINE_SY     FCB     0       ; step y (+1 or -1)
VAR_LINE_DX     FCB     0       ; |x2-x1|
VAR_LINE_DY     FCB     0       ; |y2-y1|
VAR_LINE_ERR    FDB     0       ; error accumulator (signed 16-bit)
VAR_LINE_E2     FDB     0       ; 2*error temp
VAR_LINE_COL    FCB     0       ; line color
;;; Sprite drawing scratch (used by CODE_SPRDRAW / CODE_SPERASEBOX)
VAR_SPR_SA      FDB     0       ; sprite data base (byte 2+)
VAR_SPR_SX      FCB     0       ; screen X origin
VAR_SPR_SY      FCB     0       ; screen Y origin
VAR_SPR_W       FCB     0       ; sprite width (pixels)
VAR_SPR_H       FCB     0       ; sprite height (rows)
VAR_SPR_BPR     FCB     0       ; bytes per row = ceil(width/4)
VAR_SPR_ROW     FCB     0       ; current row counter
VAR_SPR_COL     FCB     0       ; current pixel column
VAR_SPR_VROW    FDB     0       ; VRAM row base (precomputed)
VAR_SPR_SRC     FDB     0       ; current sprite data pointer
VAR_SPR_DBYTE   FCB     0       ; current data byte
VAR_SPR_SHIFT   FCB     0       ; pixel shift within byte

;;; ─── Kernel ──────────────────────────────────────────────────────────────────

        ORG     $1000

;;; DOCOL — enter a colon definition
;;;   Called via JMP [,Y] where Y = address of the word's CFA.
;;;   The CFA contains the address of DOCOL.
;;;   The thread (body) begins at Y+2, immediately after the CFA cell.

DOCOL
        PSHS    X               ; save current IP on return stack
        LEAX    2,Y             ; new IP = first cell of body (Y+2)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; DOVAR — enter a variable definition
;;;   Called via JMP [,Y] where Y = address of the variable's CFA cell.
;;;   The CFA contains the address of DOVAR.
;;;   The 2-byte data cell begins at Y+2, immediately after the CFA cell.
;;;   Push the data cell address, then NEXT.

DOVAR
        LEAY    2,Y             ; Y = address of variable's data cell (Y+2)
        STY     ,--U            ; push that address onto data stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── CFA table ───────────────────────────────────────────────────────────────
;;; Each entry is a 2-byte pointer to the primitive's machine code.
;;; The thread stores addresses of these CFA entries.

CFA_EXIT        FDB     CODE_EXIT
CFA_LIT         FDB     CODE_LIT
CFA_EMIT        FDB     CODE_EMIT
CFA_HALT        FDB     CODE_HALT
CFA_ADD         FDB     CODE_ADD
CFA_SUB         FDB     CODE_SUB
CFA_CR          FDB     CODE_CR
CFA_DUP         FDB     CODE_DUP
CFA_DROP        FDB     CODE_DROP
CFA_SWAP        FDB     CODE_SWAP
CFA_OVER        FDB     CODE_OVER
CFA_FETCH       FDB     CODE_FETCH
CFA_STORE       FDB     CODE_STORE
CFA_DO          FDB     CODE_DO
CFA_LOOP        FDB     CODE_LOOP
CFA_I           FDB     CODE_I
CFA_MUL         FDB     CODE_MUL
CFA_DIVMOD      FDB     CODE_DIVMOD
CFA_KEY         FDB     CODE_KEY
CFA_0BRANCH     FDB     CODE_0BRANCH
CFA_BRANCH      FDB     CODE_BRANCH
CFA_EQ          FDB     CODE_EQ
CFA_NEQ         FDB     CODE_NEQ
CFA_LT          FDB     CODE_LT
CFA_GT          FDB     CODE_GT
CFA_ZEQU        FDB     CODE_ZEQU
CFA_AT          FDB     CODE_AT
CFA_CSTORE      FDB     CODE_CSTORE
CFA_CFETCH      FDB     CODE_CFETCH
CFA_AND         FDB     CODE_AND
CFA_OR          FDB     CODE_OR
CFA_KBD_SCAN    FDB     CODE_KBD_SCAN
CFA_KEY_NB      FDB     CODE_KEY_NB
CFA_FILL        FDB     CODE_FILL
CFA_CMOVE       FDB     CODE_CMOVE
CFA_LSHIFT      FDB     CODE_LSHIFT
CFA_RSHIFT      FDB     CODE_RSHIFT
CFA_NEGATE      FDB     CODE_NEGATE
CFA_QDUP        FDB     CODE_QDUP
CFA_RGPSET      FDB     CODE_RGPSET
CFA_RGLINE      FDB     CODE_RGLINE
CFA_SPRDRAW     FDB     CODE_SPRDRAW
CFA_SPERASEBOX  FDB     CODE_SPERASEBOX

;;; ─── EXIT ( -- ) ─────────────────────────────────────────────────────────────
;;; Return from a colon definition.

CODE_EXIT
        PULS    X               ; restore saved IP from return stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── LIT ( -- n ) ────────────────────────────────────────────────────────────
;;; Push the next 16-bit cell in the thread as a literal value.

CODE_LIT
        LDD     ,X++            ; fetch literal from thread, advance IP past it
        STD     ,--U            ; push onto data stack (A=high, B=low)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── EMIT ( c -- ) ───────────────────────────────────────────────────────────
;;; Write one character to the video buffer at the current cursor, advance it.
;;;
;;; ASCII → VDG alphanumeric encoding:
;;;   screen_byte = $40 | (ascii & $3F)
;;;
;;; For uppercase A–Z (ASCII $41–$5A): screen_byte = ascii (no change).
;;; For space  (ASCII $20):            screen_byte = $60.
;;; For digits, punctuation:           $40 | low-6-bits.
;;;
;;; CoCo text mode is uppercase-only; lowercase will appear as uppercase.
;;; Cursor wraps to top-left at end of screen (no scrolling yet).

CODE_EMIT
        LDB     1,U             ; character = low byte of TOS
        LEAU    2,U             ; drop TOS
        ANDB    #$3F            ; strip top 2 bits → 6-bit VDG char code
        ORB     #$40            ; set normal-video bit
        LDY     VAR_CUR         ; Y = current cursor offset
        STB     SCREEN,Y        ; write VDG byte to video RAM
        LEAY    1,Y             ; advance cursor
        CMPY    #NSCR           ; past end of screen?
        BLO     EMIT_OK
        LDY     #0              ; wrap to top-left
EMIT_OK
        STY     VAR_CUR         ; save cursor position
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── + ( n1 n2 -- sum ) ──────────────────────────────────────────────────────
;;; Pop two values, push their sum.
;;;   Before: [ n2 | n1 | ... ]   U points at n2 (TOS)
;;;   After:  [ n1+n2 | ... ]

CODE_ADD
        LDD     ,U              ; D = TOS (n2), no auto-increment
        LEAU    2,U             ; pop TOS: U now points at n1
        ADDD    ,U              ; D = n2 + n1
        STD     ,U              ; write sum back; n1 slot becomes result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── - ( n1 n2 -- diff ) ────────────────────────────────────────────────────
;;; Pop two values, push n1 - n2.
;;;   Before: [ n2 | n1 | ... ]   U points at n2 (TOS)
;;;   After:  [ n1-n2 | ... ]

CODE_SUB
        LDD     2,U             ; D = NOS (n1)
        SUBD    ,U              ; D = n1 - n2 (TOS)
        LEAU    2,U             ; pop TOS: U now points at n1
        STD     ,U              ; write difference back; n1 slot becomes result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── CR ( -- ) ───────────────────────────────────────────────────────────────
;;; Advance cursor to the start of the next row.
;;; new_cursor = (cursor + 32) & $FFE0  — rounds up to next multiple of 32.

CODE_CR
        LDD     VAR_CUR         ; D = current cursor (0–511)
        ADDD    #32             ; move into next-row region
        ANDB    #$E0            ; clear low 5 bits → align to column 0
        CMPD    #NSCR           ; past end of screen?
        BLO     CR_OK
        LDD     #0              ; wrap to top-left
CR_OK
        STD     VAR_CUR         ; save new cursor
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── DUP ( n -- n n ) ────────────────────────────────────────────────────────
;;; Duplicate the top of stack.

CODE_DUP
        LDD     ,U              ; D = TOS
        STD     ,--U            ; push copy (2-byte pre-decrement)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── DROP ( n -- ) ───────────────────────────────────────────────────────────
;;; Discard the top of stack.

CODE_DROP
        LEAU    2,U             ; pop TOS (discard)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── SWAP ( n1 n2 -- n2 n1 ) ─────────────────────────────────────────────────
;;; Exchange the top two stack items.

CODE_SWAP
        LDD     ,U              ; D = TOS (n2)
        LDY     2,U             ; Y = NOS (n1)
        STD     2,U             ; old TOS → NOS slot
        STY     ,U              ; old NOS → TOS slot
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── OVER ( n1 n2 -- n1 n2 n1 ) ─────────────────────────────────────────────
;;; Copy the second stack item to the top.

CODE_OVER
        LDD     2,U             ; D = NOS (n1)
        STD     ,--U            ; push copy on top (2-byte pre-decrement)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── @ ( addr -- n ) ─────────────────────────────────────────────────────────
;;; Fetch a 16-bit value from the address on top of stack.
;;;   Before: [ addr | ... ]
;;;   After:  [ value | ... ]

CODE_FETCH
        LDY     ,U              ; Y = address (TOS)
        LDD     ,Y              ; D = 16-bit value at that address
        STD     ,U              ; replace TOS with fetched value
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── ! ( n addr -- ) ─────────────────────────────────────────────────────────
;;; Store a 16-bit value at the address on top of stack.
;;;   Before: [ addr | n | ... ]   TOS=addr, NOS=n
;;;   After:  [ ... ]              both popped

CODE_STORE
        LDY     ,U              ; Y = address (TOS)
        LDD     2,U             ; D = value (NOS)
        STD     ,Y              ; store value at address
        LEAU    4,U             ; pop both addr (TOS) and value (NOS)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── HALT ────────────────────────────────────────────────────────────────────
;;; Spin forever — end of application.

;;; ─── DO ( limit start -- ) ( R: -- start limit ) ────────────────────────────
;;; Pop limit and start from data stack, push both onto return stack.
;;; Return stack layout after DO: TOS=index(start), NOS=limit.

CODE_DO
        LDD     ,U              ; D = start (TOS)
        LDY     2,U             ; Y = limit (NOS)
        LEAU    4,U             ; pop both from data stack
        STY     ,--S            ; push limit → NOS of return stack
        STD     ,--S            ; push index → TOS of return stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── LOOP ( -- ) ( R: index limit -- or loop continues ) ────────────────────
;;; Increment index (TOS of R). If equal to limit (NOS of R), pop both and
;;; fall through (skip the back-branch offset cell). Otherwise branch back
;;; via the signed 16-bit offset that follows CFA_LOOP in the thread.

CODE_LOOP
        LDD     ,S              ; D = index (TOS of R)
        ADDD    #1
        STD     ,S              ; store incremented index
        CMPD    2,S             ; compare with limit (NOS of R)
        BEQ     LOOP_DONE
        LDD     ,X              ; D = back-branch offset
        LEAX    2,X             ; advance IP past the offset cell
        LEAX    D,X             ; apply signed offset
        LDY     ,X++            ; NEXT
        JMP     [,Y]
LOOP_DONE
        LEAS    4,S             ; pop index + limit from return stack
        LEAX    2,X             ; skip over the offset cell
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── I ( -- n ) ──────────────────────────────────────────────────────────────
;;; Copy the current loop index (TOS of return stack) to the data stack.

CODE_I
        LDD     ,S              ; D = loop index (TOS of R)
        STD     ,--U            ; push onto data stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── * ( n1 n2 -- prod ) ─────────────────────────────────────────────────────
;;; 16×16 → 16 signed/unsigned multiply (low 16 bits of product).
;;;
;;; Algorithm: low16(n1 × n2) = n1l×n2l + (n1h×n2l + n1l×n2h)×256
;;;   n1 at U+2..U+3 (hi..lo), n2 at U..U+1 (hi..lo)
;;; Uses S stack for two single-byte temporaries; restores S before NEXT.

CODE_MUL
        LDA     2,U             ; A = n1_hi
        LDB     1,U             ; B = n2_lo
        MUL                     ; D = n1_hi × n2_lo
        PSHS    B               ; save low byte (cross term 1)
        LDA     3,U             ; A = n1_lo
        LDB     ,U              ; B = n2_hi
        MUL                     ; D = n1_lo × n2_hi
        ADDB    ,S+             ; cross_sum = cross1 + low8(n1_lo×n2_hi), pop S
        PSHS    B               ; save cross_sum
        LDA     3,U             ; A = n1_lo
        LDB     1,U             ; B = n2_lo
        MUL                     ; D = n1_lo × n2_lo (base product)
        ADDA    ,S+             ; result_hi = base_hi + cross_sum, pop S
        LEAU    4,U             ; pop both operands
        STD     ,--U            ; push result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── /MOD ( n1 n2 -- rem quot ) ──────────────────────────────────────────────
;;; Unsigned 16÷16 division via repeated subtraction.
;;; n1 = dividend (NOS), n2 = divisor (TOS).
;;; Pushes remainder then quotient (quotient on top).
;;; Division by zero loops forever — caller must ensure n2 ≠ 0.

CODE_DIVMOD
        LDD     ,U              ; D = divisor
        PSHS    D               ; save divisor on S stack
        LDD     2,U             ; D = dividend
        LEAU    4,U             ; pop both operands from data stack
        LDY     #0              ; Y = quotient = 0
DIVMOD_LOOP
        CMPD    ,S              ; dividend − divisor (unsigned)
        BLO     DIVMOD_DONE     ; if dividend < divisor, we're done
        SUBD    ,S              ; dividend −= divisor
        LEAY    1,Y             ; quotient++
        BRA     DIVMOD_LOOP
DIVMOD_DONE
        LEAS    2,S             ; restore S (pop saved divisor)
        STD     ,--U            ; push remainder (D = remainder)
        STY     ,--U            ; push quotient (Y = quotient)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── KBD-SCAN ( col_mask -- row_bits ) ──────────────────────────────────────
;;; Strobe one or more keyboard columns and return the active row bits.
;;;
;;; col_mask  — byte written to PIA0-B ($FF02); a bit that is LOW selects that
;;;             column.  Pass $00 to strobe all columns at once (any-key check).
;;; row_bits  — bits 0–6 set where a key is pressed (active-high after invert).
;;;             Bit 7 is always 0 (joystick comparator, masked out).
;;;
;;; PIA0-B is restored to $FF (all columns deselected) before returning so a
;;; strobe is never left asserted across unrelated code.

CODE_KBD_SCAN
        LDA     1,U             ; A = col_mask (low byte of TOS)
        STA     $FF02           ; strobe selected column(s)
        LDB     $FF00           ; B = row data (active-low; pressed = 0)
        COMB                    ; invert: pressed = 1
        ANDB    #$7F            ; mask bit 7 (joystick comparator)
        LDA     #$FF
        STA     $FF02           ; deselect all columns
        CLRA                    ; A = 0 (high byte of 16-bit result)
        STD     ,U              ; replace TOS with row_bits (D = 0:row_bits)
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── KEY ( -- c ) ────────────────────────────────────────────────────────────
;;; Block until a key is pressed; push its ASCII value.
;;; Uses direct PIA0 scanning — no ROM dependency.
;;;
;;; Debounce: scan all columns at once; require the result to CHANGE from the
;;; previous scan before accepting a key.  A held key triggers once per call.
;;; Modifier keys (SHIFT/CTRL/ALT return $00 from the table) reset the debounce
;;; counter so they cannot stall the loop.

KEY_INIT_DLY    EQU     30      ; initial repeat delay (30 frames = 500ms at 60Hz)
KEY_RPT_DLY     EQU     5       ; subsequent repeat interval (5 frames = 83ms)

CODE_KEY
KEY_POLL
        ; Pre-check SHIFT (PB7/PA6)
        LDA     #$7F            ; strobe column 7 (SHIFT column)
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$40            ; isolate PA6 (SHIFT row)
        STA     VAR_KEY_SHIFT   ; save shift flag
        LDA     #$FF
        STA     $FF02           ; deselect all columns
        ; Identify the current key
        BSR     MATRIX2ASCII    ; A = ASCII (0 if none/modifier)
        TSTA
        BNE     KEY_HAVE
        ; No key — reset repeat timer and count release debounce
        LDD     #KEY_INIT_DLY   ; reset repeat so holding requires full delay
        STD     VAR_KEY_REPDLY
        LDA     $FF03           ; clear any pending VSYNC flag
        LDA     $FF02
        LDA     VAR_KEY_RELCNT
        BEQ     KEY_POLL        ; already cleared → keep polling
        DECA
        STA     VAR_KEY_RELCNT
        BNE     KEY_POLL        ; not enough consecutive releases yet
        CLR     VAR_KEY_PREV    ; confirmed release — allow same key again
        BRA     KEY_POLL
KEY_HAVE
        ; Key is pressed — reset release counter
        LDB     #40             ; ~40 polls release debounce
        STB     VAR_KEY_RELCNT
        ; Same key as last accepted?
        CMPA    VAR_KEY_PREV    ; A still has MATRIX2ASCII result
        BEQ     KEY_REPEAT      ; yes → check auto-repeat timer
        ; New key — accept it
        STA     VAR_KEY_PREV    ; remember this key
        LDD     #KEY_INIT_DLY   ; load initial repeat delay
        STD     VAR_KEY_REPDLY
        LDA     $FF03           ; clear any pending VSYNC flag
        LDA     $FF02
        LDA     VAR_KEY_PREV    ; reload key (D/A were clobbered)
        BRA     KEY_ACCEPT
KEY_REPEAT
        ; Same key held — only decrement on VSYNC (60Hz hardware timer)
        LDA     $FF03           ; check VSYNC flag (bit 7 of PIA0 CRB)
        BPL     KEY_POLL        ; no VSYNC yet → poll again
        LDA     $FF02           ; clear VSYNC flag (read port B data reg)
        LDD     VAR_KEY_REPDLY
        SUBD    #1
        STD     VAR_KEY_REPDLY
        BNE     KEY_POLL        ; not yet → keep waiting
        ; Repeat fired — reload with shorter interval
        LDD     #KEY_RPT_DLY
        STD     VAR_KEY_REPDLY
        LDA     VAR_KEY_PREV    ; reload key
KEY_ACCEPT
        ; Apply shift if flagged
        TST     VAR_KEY_SHIFT
        BEQ     KEY_NOSHF
        LBSR    SHIFT_APPLY
KEY_NOSHF
        TFR     A,B             ; B = ASCII low byte
        CLRA                    ; A = 0 (high byte)
        STD     ,--U            ; push character onto data stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── MATRIX2ASCII — identify first pressed key; return ASCII in A ────────────
;;; Scans 8 keyboard columns (PB0–PB7) × 7 rows (PA0–PA6).
;;; PA7 is the joystick DAC comparator — not a keyboard row — and is masked.
;;;
;;; Column strobe sequence: $FE $FD $FB $F7 $EF $DF $BF $7F.
;;; Generated by SEC + ROLA: rotates the zero-bit left one position each step.
;;; After $7F, ROLA produces $FF → sentinel for "all 8 columns done".
;;;
;;; Entry:  —
;;; Exit:   A = ASCII of first pressed key, or 0 if none found.
;;;         Z flag set if A=0.
;;; Modifies: A, B, Y.   Preserves: X (=IP), U (=DSP), S (=RSP).

MATRIX2ASCII
        LDY     #KEY_TABLE      ; Y = ASCII table base
        LDA     #$FE            ; A = column 0 strobe (bit 0 low)
MAT_COL
        STA     $FF02           ; assert this column strobe
        PSHS    A               ; save strobe across row scan
        LDA     $FF00           ; read row bits (active-low)
        COMA                    ; invert: pressed=1
        ANDA    #$7F            ; mask PA7 (joystick comparator)
        LDB     #7              ; 7 rows to check (PA0–PA6)
MAT_ROW
        LSRA                    ; shift bit 0 into carry; advance bit window
        BCS     MAT_HIT         ; carry set → this row is pressed
        LEAY    1,Y             ; advance table pointer to next row entry
        DECB
        BNE     MAT_ROW
        ; No key in this column — advance strobe to next column
        PULS    A               ; restore column strobe
        ORCC    #$01            ; set carry=1 (6809: no SEC; ROLA needs C=1)
        ROLA                    ; $FE→$FD→$FB→$F7→$EF→$DF→$BF→$7F→$FF
        CMPA    #$FF            ; all 8 columns exhausted?
        BNE     MAT_COL         ; not $FF → still a valid strobe
        ; No key found across all columns (unexpected after debounce)
        LDA     #0
        BRA     MAT_DONE
MAT_HIT
        LDA     ,Y              ; A = ASCII for this (col, row)
        PULS    B               ; discard saved strobe (preserves A; B discarded below)
MAT_DONE
        LDB     #$FF
        STB     $FF02           ; deselect all columns
        TSTA                    ; set Z flag: Z=1 if A=0 (modifier/no key)
        RTS

;;; ─── KEY_TABLE — 8 columns × 7 rows ASCII lookup ─────────────────────────────
;;; 8 columns (PB0–PB7) × 7 rows (PA0–PA6).  Index = col*7 + row.
;;; PA7 is the joystick DAC comparator, NOT a keyboard row.
;;; Modifier keys (ALT/CTL/SHF) return $00 so CODE_KEY treats them as transparent.
;;; Arrow keys use C0 control codes $1C–$1F (FS GS RS US).

KEY_TABLE
        ; Col 0 ($FE) — PB0
        FCB     '@','H','P','X','0','8',$0D
        ; Col 1 ($FD) — PB1
        FCB     'A','I','Q','Y','1','9',$0C
        ; Col 2 ($FB) — PB2
        FCB     'B','J','R','Z','2',':',$03
        ; Col 3 ($F7) — PB3
        FCB     'C','K','S',$1C,'3',';',$00
        ; Col 4 ($EF) — PB4
        FCB     'D','L','T',$1D,'4',',',$00
        ; Col 5 ($DF) — PB5
        FCB     'E','M','U',$1E,'5','-',$00
        ; Col 6 ($BF) — PB6
        FCB     'F','N','V',$1F,'6','.',$00
        ; Col 7 ($7F) — PB7  (row 6 = SHIFT, returns $00 = modifier)
        FCB     'G','O','W',$20,'7','/',$00

;;; ─── KEY? ( -- char|0 ) ──────────────────────────────────────────────────────
;;; Non-blocking key check. Returns the ASCII value of a currently-pressed key,
;;; or 0 if nothing is pressed. Does not spin; returns immediately either way.
;;;
;;; Shares MATRIX2ASCII with KEY for the per-column identification scan.
;;; Modifier-only presses return 0.

CODE_KEY_NB
        CLR     $FF02           ; strobe all columns
        LDA     $FF00           ; read all row bits (active-low)
        COMA                    ; invert: pressed=1
        ANDA    #$7F            ; mask joystick bit — avoid spurious scan overhead
        BEQ     KEY_NB_DONE     ; nothing in rows 0–6 → fall through with A=0
        ; pre-check SHIFT (PB7/PA6)
        LDA     #$7F
        STA     $FF02
        LDA     $FF00
        COMA
        ANDA    #$40
        STA     VAR_KEY_SHIFT   ; save shift flag
        LDA     #$FF
        STA     $FF02
        ; identify key
        LBSR    MATRIX2ASCII    ; A = ASCII of first pressed key (0 = modifier)
        BEQ     KEY_NB_DONE     ; modifier only → return 0
        TST     VAR_KEY_SHIFT
        BEQ     KEY_NB_DONE2
        BSR     SHIFT_APPLY
KEY_NB_DONE2
KEY_NB_DONE
        TFR     A,B             ; B = char (or 0)
        CLRA                    ; A = 0 (high byte)
        STD     ,--U            ; push result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── SHIFT_APPLY — transform character in A to its shifted variant ──────────
;;; Called when SHIFT was detected (pre-checked before MATRIX2ASCII).
;;; Transforms shiftable characters:
;;;   $31–$3B  (1–9 : ;) → subtract $10  (! " # $ % & ' ( ) * +)
;;;   $2C–$2F  (, - . /) → add $10       (< = > ?)
;;; Letters and other characters are unchanged.
;;;
;;; Entry:  A = ASCII character (non-zero)
;;; Exit:   A = shifted character (or unchanged)

SHIFT_APPLY
        CMPA    #$2C
        BLO     SHFT_DONE       ; below ',' → no shift mapping
        CMPA    #$2F
        BLS     SHFT_ADD        ; $2C–$2F (, - . /) → add $10
        CMPA    #$31
        BLO     SHFT_DONE       ; '0' → no change
        CMPA    #$3B
        BHI     SHFT_DONE       ; letters/others → no change
        SUBA    #$10            ; $31–$3B (1–9 : ;) → shifted symbol
        RTS
SHFT_ADD
        ADDA    #$10            ; $2C–$2F (, - . /) → shifted symbol
SHFT_DONE
        RTS

;;; ─── 0BRANCH ( flag -- ) ────────────────────────────────────────────────────
;;; Pop flag; if zero, apply signed offset from thread; else skip over it.

CODE_0BRANCH
        LDD     ,U
        LEAU    2,U
        BNE     OBR_SKIP
        LDD     ,X
        LEAX    2,X
        LEAX    D,X
        LDY     ,X++
        JMP     [,Y]
OBR_SKIP
        LEAX    2,X
        LDY     ,X++
        JMP     [,Y]

;;; ─── BRANCH ( -- ) ───────────────────────────────────────────────────────────
;;; Unconditional branch via signed offset cell in the thread.

CODE_BRANCH
        LDD     ,X
        LEAX    2,X
        LEAX    D,X
        LDY     ,X++
        JMP     [,Y]

;;; ─── = ( n1 n2 -- flag ) ─────────────────────────────────────────────────────
;;; Push -1 if n1 = n2, else push 0.

CODE_EQ
        LDD     2,U
        SUBD    ,U
        LEAU    4,U
        BNE     EQ_FALSE
        LDD     #$FFFF
        BRA     EQ_DONE
EQ_FALSE
        LDD     #0
EQ_DONE
        STD     ,--U
        LDY     ,X++
        JMP     [,Y]

;;; ─── <> ( n1 n2 -- flag ) ────────────────────────────────────────────────────
;;; Push -1 if n1 <> n2, else push 0.

CODE_NEQ
        LDD     2,U
        SUBD    ,U
        LEAU    4,U
        BEQ     NEQ_FALSE
        LDD     #$FFFF
        BRA     NEQ_DONE
NEQ_FALSE
        LDD     #0
NEQ_DONE
        STD     ,--U
        LDY     ,X++
        JMP     [,Y]

;;; ─── < ( n1 n2 -- flag ) ─────────────────────────────────────────────────────
;;; Push -1 if n1 < n2 (signed), else push 0.

CODE_LT
        LDD     2,U
        CMPD    ,U
        LEAU    4,U
        BGE     LT_FALSE
        LDD     #$FFFF
        BRA     LT_DONE
LT_FALSE
        LDD     #0
LT_DONE
        STD     ,--U
        LDY     ,X++
        JMP     [,Y]

;;; ─── > ( n1 n2 -- flag ) ─────────────────────────────────────────────────────
;;; Push -1 if n1 > n2 (signed), else push 0.

CODE_GT
        LDD     2,U
        CMPD    ,U
        LEAU    4,U
        BLE     GT_FALSE
        LDD     #$FFFF
        BRA     GT_DONE
GT_FALSE
        LDD     #0
GT_DONE
        STD     ,--U
        LDY     ,X++
        JMP     [,Y]

;;; ─── 0= ( n -- flag ) ────────────────────────────────────────────────────────
;;; Push -1 if n = 0, else push 0.  (Replaces TOS in-place.)

CODE_ZEQU
        LDD     ,U
        BNE     ZEQU_F
        LDD     #$FFFF
        BRA     ZEQU_S
ZEQU_F  LDD     #0
ZEQU_S  STD     ,U
        LDY     ,X++
        JMP     [,Y]

;;; AT ( col row -- )
;;; Set cursor to row*32+col by storing the result in VAR_CUR.
CODE_AT
        LDB     1,U             ; row low byte (TOS, 0–15)
        LDA     #32
        MUL                     ; D = row*32 (0–480, fits in 9 bits)
        ADDB    3,U             ; + col low byte (NOS, 0–31)
        ADCA    #0              ; propagate carry
        LEAU    4,U             ; pop both stack values
        STD     VAR_CUR
        LDY     ,X++
        JMP     [,Y]

;;; ─── C! ( byte addr -- ) ────────────────────────────────────────────────────
;;; Store the low byte of value to addr. Enables direct video RAM writes for
;;; VDG semigraphic characters ($80–$FF) without EMIT's encoding.

CODE_CSTORE
        LDY     ,U              ; Y = address (TOS)
        LDA     3,U             ; A = low byte of value (NOS)
        STA     ,Y              ; store byte at address
        LEAU    4,U             ; pop both
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── C@ ( addr -- byte ) ────────────────────────────────────────────────────
;;; Fetch a single byte from addr; push it as a 16-bit value (high byte = 0).

CODE_CFETCH
        LDY     ,U              ; Y = address (TOS)
        LDB     ,Y              ; B = byte at address
        CLRA                    ; A = 0 (high byte)
        STD     ,U              ; replace TOS with result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── AND ( n1 n2 -- n ) ──────────────────────────────────────────────────────
;;; Bitwise AND of top two stack items.

CODE_AND
        LDD     ,U              ; D = TOS (n2)
        LEAU    2,U             ; pop TOS; U now points at NOS (n1)
        ANDA    ,U              ; A = n2_hi & n1_hi
        ANDB    1,U             ; B = n2_lo & n1_lo
        STD     ,U              ; replace NOS with result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── OR ( n1 n2 -- n ) ───────────────────────────────────────────────────────
;;; Bitwise OR of top two stack items.

CODE_OR
        LDD     ,U              ; D = TOS (n2)
        LEAU    2,U             ; pop TOS; U now points at NOS (n1)
        ORA     ,U              ; A = n2_hi | n1_hi
        ORB     1,U             ; B = n2_lo | n1_lo
        STD     ,U              ; replace NOS with result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── FILL ( addr count byte -- ) ────────────────────────────────────────────
;;; Fill count bytes starting at addr with byte (low byte of TOS).
;;; X (IP) is saved/restored since the fill loop uses X as dest pointer.

CODE_FILL
        PSHS    X               ; save IP
        LDB     1,U             ; B = fill byte (low byte of TOS)
        LDY     2,U             ; Y = count
        BEQ     FILL_DONE       ; count = 0 → skip loop
        LDX     4,U             ; X = dest addr
FILL_LP STB     ,X+
        LEAY    -1,Y
        BNE     FILL_LP
FILL_DONE
        LEAU    6,U             ; pop 3 cells
        PULS    X               ; restore IP
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── CMOVE ( src dest count -- ) ───────────────────────────────────────────
;;; Copy count bytes from src to dest (low-to-high).
;;; Uses X=dest, Y=src (both saved/restored).  Count on S stack.

CODE_CMOVE
        PSHS    X               ; save IP
        LDD     ,U              ; D = count
        BEQ     CMOV_DONE       ; count = 0 → skip
        PSHS    D               ; save count on S
        LDX     2,U             ; X = dest
        LDY     4,U             ; Y = src
CMOV_LP LDA     ,Y+             ; A = byte from src
        STA     ,X+             ; store to dest
        LDD     ,S              ; count--
        SUBD    #1
        STD     ,S
        BNE     CMOV_LP
        LEAS    2,S             ; pop count
CMOV_DONE
        LEAU    6,U             ; pop 3 cells
        PULS    X               ; restore IP
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── LSHIFT ( n count -- n' ) ──────────────────────────────────────────────
;;; Logical left shift n by count bits.

CODE_LSHIFT
        LDA     1,U             ; A = shift count (low byte of TOS)
        ANDA    #$0F            ; clamp to 0–15
        PSHS    A               ; save count on return stack
        LDD     2,U             ; D = value to shift (NOS)
        TST     ,S              ; count = 0?
        BEQ     LSH_DONE
LSH_LP  ASLB                    ; shift D left one bit
        ROLA
        DEC     ,S              ; count--
        BNE     LSH_LP
LSH_DONE
        LEAS    1,S             ; pop count byte
        LEAU    2,U             ; pop count cell (keep one cell for result)
        STD     ,U              ; store shifted value
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── RSHIFT ( n count -- n' ) ──────────────────────────────────────────────
;;; Logical right shift n by count bits.

CODE_RSHIFT
        LDA     1,U             ; A = shift count (low byte of TOS)
        ANDA    #$0F            ; clamp to 0–15
        PSHS    A               ; save count on return stack
        LDD     2,U             ; D = value to shift (NOS)
        TST     ,S              ; count = 0?
        BEQ     RSH_DONE
RSH_LP  LSRA                    ; shift D right one bit (logical)
        RORB
        DEC     ,S              ; count--
        BNE     RSH_LP
RSH_DONE
        LEAS    1,S             ; pop count byte
        LEAU    2,U             ; pop count cell
        STD     ,U              ; store shifted value
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── NEGATE ( n -- -n ) ──────────────────────────────────────────────────────
;;; Two's complement negate: -n = ~n + 1.

CODE_NEGATE
        LDD     ,U              ; D = TOS
        COMA                    ; ones' complement high byte
        COMB                    ; ones' complement low byte
        ADDD    #1              ; two's complement
        STD     ,U              ; replace TOS
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── ?DUP ( x -- x x | 0 ) ─────────────────────────────────────────────────
;;; Duplicate TOS only if it is non-zero.

CODE_QDUP
        LDD     ,U              ; D = TOS
        BEQ     QDUP_DONE       ; zero → leave stack unchanged
        STD     ,--U            ; non-zero → push duplicate
QDUP_DONE
        LDY     ,X++            ; NEXT
        JMP     [,Y]

CODE_HALT
        BRA     CODE_HALT

;;; ─── RG-PSET ( x y color -- ) ──────────────────────────────────────────────
;;; Plot one 2bpp artifact pixel in RG6 mode.
;;; x: 0-127, y: 0-191, color: 0-3.
;;; VRAM addr = VAR_RGVRAM + y*32 + x/4.  Sub-pixel = x AND 3.
;;; ~45 cycles vs ~500 in ITC Forth.

CODE_RGPSET
        LDA     5,U             ; A = x (low byte of 3rd item)
        LDB     3,U             ; B = y (low byte of 2nd item)
        PSHS    A               ; save x on S
        ; Y = VRAM base + y*32 + x/4
        LDA     #32
        MUL                     ; D = y * 32
        ADDD    VAR_RGVRAM      ; D += VRAM base
        TFR     D,Y             ; Y = row start
        LDA     ,S              ; A = x
        LSRA
        LSRA                    ; A = x / 4
        LEAY    A,Y             ; Y = VRAM byte address
        ; Shift count = 6 - (x%4)*2
        LDA     ,S+             ; A = x, pop S
        ANDA    #$03            ; A = x % 4
        ASLA                    ; A = (x%4)*2
        NEGA
        ADDA    #6              ; A = 6 - (x%4)*2
        PSHS    A               ; save shift count
        ; Shift color into position
        LDA     1,U             ; A = color (0-3)
        ANDA    #$03
        LDB     ,S              ; B = shift count
        BEQ     PSET_NS
PSET_SH ASLA
        DECB
        BNE     PSET_SH
PSET_NS PSHS    A               ; save shifted color
        ; Build clear mask: ~(3 << shift)
        LDA     #$03
        LDB     1,S             ; B = shift count
        BEQ     PSET_NM
PSET_SM ASLA
        DECB
        BNE     PSET_SM
PSET_NM COMA                    ; A = clear mask
        ; Read-modify-write VRAM
        ANDA    ,Y              ; clear old pixel
        ORA     ,S              ; OR in new color
        STA     ,Y              ; write back
        LEAS    2,S             ; clean S (shifted color + shift count)
        LEAU    6,U             ; pop 3 data stack items
        LDY     ,X++            ; NEXT
        JMP     [,Y]


;;; ─── RG-LINE ( x1 y1 x2 y2 color -- ) ─────────────────────────────────────
;;; Bresenham line drawing with inlined pixel write.  All 8 octants.
;;; ~150 cycles/pixel vs ~1500 in ITC Forth.
;;; Uses fixed RAM variables VAR_LINE_* for loop state.

CODE_RGLINE
        PSHS    X               ; save IP
        ; Load args: U+0,1=color U+2,3=y2 U+4,5=x2 U+6,7=y1 U+8,9=x1
        LDA     1,U
        STA     VAR_LINE_COL
        LDA     5,U
        STA     VAR_LINE_X2
        LDA     3,U
        STA     VAR_LINE_Y2
        LDA     9,U
        STA     VAR_LINE_CX
        LDA     7,U
        STA     VAR_LINE_CY
        LEAU    10,U            ; pop 5 args

        ; dx = |x2-x1|, sx = sign(x2-x1)  — 16-bit sub for correct sign
        CLRA
        LDB     VAR_LINE_CX
        PSHS    D               ; push x1
        CLRA
        LDB     VAR_LINE_X2
        SUBD    ,S++            ; D = x2 - x1 (signed 16-bit)
        BPL     LNSX_P
        COMA
        COMB
        ADDD    #1              ; D = |x2-x1|
        STB     VAR_LINE_DX
        LDA     #$FF
        STA     VAR_LINE_SX
        BRA     LNSX_D
LNSX_P  STB     VAR_LINE_DX
        LDA     #$01
        STA     VAR_LINE_SX
LNSX_D

        ; dy = |y2-y1|, sy = sign(y2-y1)  — 16-bit sub for y > 127
        CLRA
        LDB     VAR_LINE_CY
        PSHS    D               ; push y1
        CLRA
        LDB     VAR_LINE_Y2
        SUBD    ,S++            ; D = y2 - y1 (signed 16-bit)
        BPL     LNSY_P
        COMA
        COMB
        ADDD    #1              ; D = |y2-y1|
        STB     VAR_LINE_DY
        LDA     #$FF
        STA     VAR_LINE_SY
        BRA     LNSY_D
LNSY_P  STB     VAR_LINE_DY
        LDA     #$01
        STA     VAR_LINE_SY
LNSY_D

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

LN_LOOP
        ; ── Plot pixel at (CX, CY) ──
        LDA     VAR_LINE_CY
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     VAR_LINE_CX
        LSRA
        LSRA
        LEAY    A,Y

        ; shift = 6 - (cx%4)*2
        LDA     VAR_LINE_CX
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A               ; save shift count

        ; Shift color
        LDA     VAR_LINE_COL
        ANDA    #$03
        LDB     ,S
        BEQ     LN_NS
LN_SH   ASLA
        DECB
        BNE     LN_SH
LN_NS   PSHS    A               ; save shifted color

        ; Build clear mask
        LDA     #$03
        LDB     1,S
        BEQ     LN_NM
LN_SM   ASLA
        DECB
        BNE     LN_SM
LN_NM   COMA

        ; Read-modify-write
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S

        ; ── Done check ──
        LDA     VAR_LINE_CX
        CMPA    VAR_LINE_X2
        BNE     LN_STEP
        LDA     VAR_LINE_CY
        CMPA    VAR_LINE_Y2
        BEQ     LN_DONE

LN_STEP
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
        ADDD    #1              ; D = -dy
        CMPD    VAR_LINE_E2
        BGE     LN_NOSX
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CX
        ADDA    VAR_LINE_SX
        STA     VAR_LINE_CX
LN_NOSX

        ; if e2 < dx: err += dx, cy += sy
        CLRA
        LDB     VAR_LINE_DX
        CMPD    VAR_LINE_E2
        BLE     LN_NOSY
        CLRA
        LDB     VAR_LINE_DX
        ADDD    VAR_LINE_ERR
        STD     VAR_LINE_ERR
        LDA     VAR_LINE_CY
        ADDA    VAR_LINE_SY
        STA     VAR_LINE_CY
LN_NOSY
        LBRA    LN_LOOP

LN_DONE
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── SPR-DRAW ( addr x y -- ) ─────────────────────────────────────────────
;;; Draw sprite at (x,y).  Color 0 pixels are transparent (skipped).
;;; Sprite format: byte 0 = width, byte 1 = height, byte 2+ = row data
;;; (2 bits/pixel, 4 pixels/byte, bits 7-6 = leftmost).
;;; ~60 cycles/pixel vs ~700 in ITC Forth.

CODE_SPRDRAW
        PSHS    X               ; save IP
        ; Pop args: U+0,1=y  U+2,3=x  U+4,5=addr
        LDA     3,U             ; A = x (low byte)
        STA     VAR_SPR_SX
        LDA     1,U             ; A = y (low byte)
        STA     VAR_SPR_SY
        LDY     4,U             ; Y = sprite addr
        LEAU    6,U             ; pop 3 args
        ; Read sprite header
        LDA     ,Y              ; width
        STA     VAR_SPR_W
        LDA     1,Y             ; height
        STA     VAR_SPR_H
        LEAY    2,Y             ; Y = sprite data start
        STY     VAR_SPR_SA
        ; Compute bytes per row = (width+3)/4
        LDA     VAR_SPR_W
        ADDA    #3
        LSRA
        LSRA
        STA     VAR_SPR_BPR
        ; Row loop
        CLR     VAR_SPR_ROW
SD_ROW
        ; Compute VRAM row base = RGVRAM + (sy+row)*32
        LDA     VAR_SPR_SY
        ADDA    VAR_SPR_ROW
        LDB     #32
        MUL                     ; D = (sy+row)*32
        ADDD    VAR_RGVRAM
        STD     VAR_SPR_VROW
        ; Compute sprite data pointer = SA + row*BPR
        LDA     VAR_SPR_ROW
        LDB     VAR_SPR_BPR
        MUL                     ; D = row*BPR
        ADDD    VAR_SPR_SA
        STD     VAR_SPR_SRC
        ; Column loop
        CLR     VAR_SPR_COL
SD_COL
        ; Load sprite data byte: src[col/4]
        LDA     VAR_SPR_COL
        LSRA
        LSRA                    ; A = col/4
        LDY     VAR_SPR_SRC
        LDA     A,Y             ; A = data byte
        ; Extract 2-bit pixel: shift = 6 - (col%4)*2
        LDB     VAR_SPR_COL
        ANDB    #$03
        ASLB                    ; B = (col%4)*2
        NEGB
        ADDB    #6              ; B = shift count
        STB     VAR_SPR_SHIFT
        ; Shift data byte right by shift count to get color in bits 1-0
        TSTB
        BEQ     SD_NOSR
SD_SR   LSRA
        DECB
        BNE     SD_SR
SD_NOSR ANDA    #$03            ; A = pixel color (0-3)
        BEQ     SD_SKIP         ; transparent — skip
        ; Write pixel to VRAM
        PSHS    A               ; save color
        ; VRAM byte addr = VROW + (sx+col)/4
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL     ; A = sx+col
        PSHS    A               ; save screen x
        LSRA
        LSRA                    ; A = (sx+col)/4
        LDY     VAR_SPR_VROW
        LEAY    A,Y             ; Y = VRAM byte address
        ; Shift count for screen pixel = 6 - ((sx+col)%4)*2
        LDA     ,S+             ; A = screen x, pop
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        ; Shift color into position
        LDB     ,S              ; B = color (from stack)
        TSTA
        BEQ     SD_NCS
SD_CSH  ASLB
        DECA
        BNE     SD_CSH
SD_NCS  STB     ,S              ; save shifted color back on S
        ; Build clear mask: ~(3 << shift)
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count again
        LDB     #$03
        TSTA
        BEQ     SD_NMS
SD_MSH  ASLB
        DECA
        BNE     SD_MSH
SD_NMS  COMB                    ; B = clear mask
        ; Read-modify-write
        ANDB    ,Y              ; clear old pixel
        ORB     ,S+             ; OR in new color, pop shifted color
        STB     ,Y              ; write back
SD_SKIP
        INC     VAR_SPR_COL
        LDA     VAR_SPR_COL
        CMPA    VAR_SPR_W
        BNE     SD_COL
        ; Next row
        INC     VAR_SPR_ROW
        LDA     VAR_SPR_ROW
        CMPA    VAR_SPR_H
        LBNE    SD_ROW
        ; Done
        PULS    X
        LDY     ,X++
        JMP     [,Y]


;;; ─── SPR-ERASE-BOX ( addr x y -- ) ───────────────────────────────────────
;;; Erase sprite bounding box to black (color 0).  Fast: no sprite data reads.
;;; ~35 cycles/pixel.

CODE_SPERASEBOX
        PSHS    X               ; save IP
        ; Pop args: U+0,1=y  U+2,3=x  U+4,5=addr
        LDA     3,U             ; A = x (low byte)
        STA     VAR_SPR_SX
        LDA     1,U             ; A = y (low byte)
        STA     VAR_SPR_SY
        LDY     4,U             ; Y = sprite addr
        LEAU    6,U             ; pop 3 args
        ; Read sprite header
        LDA     ,Y              ; width
        STA     VAR_SPR_W
        LDA     1,Y             ; height
        STA     VAR_SPR_H
        ; Row loop
        CLR     VAR_SPR_ROW
SE_ROW
        ; Compute VRAM row base = RGVRAM + (sy+row)*32
        LDA     VAR_SPR_SY
        ADDA    VAR_SPR_ROW
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        STD     VAR_SPR_VROW
        ; Column loop
        CLR     VAR_SPR_COL
SE_COL
        ; VRAM byte addr = VROW + (sx+col)/4
        LDA     VAR_SPR_SX
        ADDA    VAR_SPR_COL     ; A = sx+col
        PSHS    A               ; save screen x
        LSRA
        LSRA                    ; A = (sx+col)/4
        LDY     VAR_SPR_VROW
        LEAY    A,Y             ; Y = VRAM byte address
        ; Clear mask = ~(3 << (6 - ((sx+col)%4)*2))
        LDA     ,S+             ; A = screen x, pop
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6              ; A = shift count
        LDB     #$03
        TSTA
        BEQ     SE_NMS
SE_MSH  ASLB
        DECA
        BNE     SE_MSH
SE_NMS  COMB                    ; B = clear mask
        ANDB    ,Y              ; clear pixel
        STB     ,Y              ; write back
        ; Next column
        INC     VAR_SPR_COL
        LDA     VAR_SPR_COL
        CMPA    VAR_SPR_W
        BNE     SE_COL
        ; Next row
        INC     VAR_SPR_ROW
        LDA     VAR_SPR_ROW
        CMPA    VAR_SPR_H
        LBNE    SE_ROW
        ; Done
        PULS    X
        LDY     ,X++
        JMP     [,Y]


;;; Initialize stacks, clear screen, start the inner interpreter.
;;; The application thread is loaded separately at APP_BASE.

APP_BASE EQU    $2000           ; application binary loaded here

START
        ORCC    #$50            ; mask IRQ and FIRQ
        CLRA
        TFR     A,DP            ; direct page register = $00
        STA     $FFDE           ; ALL-RAM mode: page out BASIC ROMs

        LDS     #$8000          ; RSP: first push lands at $7FFE
        LDU     #$7E00          ; DSP: first push lands at $7DFE

        ; ── PIA0 init (bare-metal keyboard scan) ─────────────────────────────
        ; The 6821 PIA shares each data address between the Data Direction
        ; Register (DDR) and Data Register: CR bit 2 = 0 → DDR, 1 → Data.
        ;
        ; PIA0-A ($FF00/$FF01): keyboard row sense — configure as input
        CLR     $FF01           ; CR-A: bit 2=0 → next access to $FF00 = DDR
        CLR     $FF00           ; DDR-A: all 8 bits = input (0)
        LDA     #$04            ; CR-A: bit 2=1 → select data register, IRQs off
        STA     $FF01
        ;
        ; PIA0-B ($FF02/$FF03): keyboard column strobe — configure as output
        CLR     $FF03           ; CR-B: bit 2=0 → next access to $FF02 = DDR
        LDA     #$FF            ; DDR-B: all 8 bits = output (1)
        STA     $FF02
        LDA     #$04            ; CR-B: bit 2=1 → select data register, IRQs off
        STA     $FF03
        LDA     #$FF            ; deselect all columns (active-low: high = off)
        STA     $FF02

        ; Clear screen to VDG spaces ($60 = normal-video space)
        LDX     #SCREEN
        LDB     #$60
CLR_LP  STB     ,X+
        CMPX    #SCREEN+NSCR
        BNE     CLR_LP

        ; Initialize cursor to top-left
        CLRA
        CLRB
        STD     VAR_CUR

        ; Jump to application thread at APP_BASE
        LDX     #APP_BASE
        LDY     ,X++
        JMP     [,Y]

        END     START
