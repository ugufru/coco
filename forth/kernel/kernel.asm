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

VAR_CUR FDB     0               ; cursor offset into video RAM (0–511)

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

;;; ─── KEY ( -- c ) ────────────────────────────────────────────────────────────
;;; Block until a key is pressed; push its ASCII value.
;;; Uses POLCAT via the ROM hook at $A000 (Extended Colour BASIC).
;;; POLCAT returns the ASCII char in A, or 0 if no key is pressed.
;;; KEYIN saves and restores U, X, B so the Forth registers are safe.

CODE_KEY
KEYPOLL         JSR     [$A000]         ; call POLCAT via ROM hook
                TSTA                    ; A = 0 means no key yet
                BEQ     KEYPOLL
                TFR     A,B             ; move char to low byte
                CLRA                    ; high byte = 0
                STD     ,--U            ; push char onto data stack
                LDY     ,X++            ; NEXT
                JMP     [,Y]

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

CODE_HALT
        BRA     CODE_HALT

;;; ─── START — entry point ─────────────────────────────────────────────────────
;;; Initialize stacks, clear screen, start the inner interpreter.
;;; The application thread is loaded separately at APP_BASE.

APP_BASE EQU    $2000           ; application binary loaded here

START
        ORCC    #$50            ; mask IRQ and FIRQ
        CLRA
        TFR     A,DP            ; direct page register = $00

        LDS     #$8000          ; RSP: first push lands at $7FFE
        LDU     #$7E00          ; DSP: first push lands at $7DFE

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
