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

;;; ─── CFA table ───────────────────────────────────────────────────────────────
;;; Each entry is a 2-byte pointer to the primitive's machine code.
;;; The thread stores addresses of these CFA entries.

CFA_EXIT        FDB     CODE_EXIT
CFA_LIT         FDB     CODE_LIT
CFA_EMIT        FDB     CODE_EMIT
CFA_HALT        FDB     CODE_HALT
CFA_ADD         FDB     CODE_ADD

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

;;; ─── HALT ────────────────────────────────────────────────────────────────────
;;; Spin forever — end of application.

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
