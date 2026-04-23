;;; forth/kernel/kernel.asm
;;;
;;; CoCo Forth Executor Kernel
;;; Assembled with lwasm (lwtools)
;;;
;;; ─── Overview ──────────────────────────────────────────────────────────────
;;;
;;; A minimal ITC (Indirect Threaded Code) Forth executor for the TRS-80
;;; Color Computer.  The kernel provides DOCOL/DOVAR inner interpreters,
;;; a CFA table of primitive words, and a START routine that initialises
;;; hardware and jumps to the application thread at APP_BASE ($2000).
;;;
;;; The application is cross-compiled by fc.py, which reads the kernel's
;;; .map file to resolve CFA addresses automatically.
;;;
;;; ─── Memory layout ────────────────────────────────────────────────────────
;;;
;;; The kernel lives at $E000, in the CoCo's ROM 2 region.  The SAM's
;;; all-RAM mode ($FFDF) pages out ROMs so that the full 64K address
;;; space ($0000–$FEFF) is readable/writable RAM.
;;;
;;; Because BASIC's CLOADM runs before all-RAM mode is enabled, it cannot
;;; load bytes directly to $E000 (still ROM at that point).  The solution:
;;;
;;;   1. lwasm assembles the kernel at ORG $E000 (final addresses).
;;;   2. fc.py remaps the $E000 DECB record to $1000 (staging address).
;;;   3. CLOADM loads the staged kernel to $1000 in low RAM.
;;;   4. A bootstrap at $0E00 enables all-RAM mode, copies $1000→$E000,
;;;      then JMPs to START.
;;;
;;;   $0050         Kernel variables (44 bytes)
;;;   $0600–$1FFF   RG6 VRAM (6144 bytes, set by rg-init after boot)
;;;   $2000–$7FFF   Application code (contiguous, ~24K via CLOADM)
;;;   $8000–$DDFF   Runtime RAM (24K — variables, tables, buffers; not CLOADM-loadable)
;;;   $DE00         Data stack base (U, grows down)
;;;   $E000         Return stack init (S, grows down from $DFFF)
;;;   $E000–$E4xx   Kernel code (~1.1K)
;;;   $E4xx–$FEFF   Static data / kernel growth (~7K)
;;;   $FF00–$FFFF   I/O + hardware vectors (always mapped)
;;;
;;; Note: CLOADM runs with ROM at $8000–$FEFF, so it can only load to
;;; the lower 32K.  The $8000–$DDFF region is available at runtime (after
;;; the bootstrap enables all-RAM) but cannot hold CLOADM-loaded code.
;;;
;;; DECB record layout (combined binary):
;;;
;;;   Record  DECB addr  Content
;;;   ------  ---------  -------
;;;   1       $0050      Kernel variables (44 bytes)
;;;   2       $0E00      Bootstrap (~25 bytes)
;;;   3       $1000      Staged kernel (~1.1K, remapped from $E000)
;;;   4       $2000      Application (contiguous)
;;;   Exec    $0E00      Bootstrap entry point
;;;
;;; ─── SAM all-RAM mode ─────────────────────────────────────────────────────
;;;
;;; The MC6883/SN74LS783 SAM uses address-decoded write-only registers at
;;; $FFC0–$FFDF.  Each pair is clear/set: even address clears the bit,
;;; odd address sets it.  The TY (map type) bit is the last pair:
;;;
;;;   STA $FFDE  →  TY=0  (normal: ROM at $8000–$FEFF)
;;;   STA $FFDF  →  TY=1  (all-RAM: RAM at $8000–$FEFF)
;;;
;;; The written data is irrelevant — only the address matters.
;;; Requires 64K×1 DRAM chips (4164).  XRoar: use -ram 64.
;;;
;;; ─── Register convention ──────────────────────────────────────────────────
;;;
;;;   X  IP  (instruction pointer into the thread)
;;;   U  DSP (data stack pointer, grows downward)
;;;   S  RSP (return stack pointer, grows downward)
;;;   Y  scratch (NEXT and primitives)
;;;   D  scratch accumulator (A = high byte, B = low byte)
;;;
;;; ─── Threading model ──────────────────────────────────────────────────────
;;;
;;; Indirect Threaded Code (ITC):
;;;   - Each word has a Code Field Address (CFA): a 2-byte pointer to
;;;     machine code.
;;;   - The thread is a sequence of CFA addresses.
;;;   - NEXT fetches the next CFA, jumps through it.
;;;
;;; NEXT (inlined at end of every primitive):
;;;     LDY  ,X++    ; fetch CFA address from thread, advance IP by 2
;;;     JMP  [,Y]    ; jump to machine code via CFA (indirect)

        PRAGMA  6809

KERN_VERSION    EQU     $0100   ; kernel version 1.0 (major.minor, BCD)

SCREEN  EQU     $0400           ; video RAM base (32×16 alphanumeric text)
NSCR    EQU     512             ; 32 cols × 16 rows

;;; ─── Bootstrap ──────────────────────────────────────────────────────────────
;;; DECB exec address points here.  Runs once at load time, then never again.
;;;
;;; Sequence:
;;;   1. Mask interrupts (no IRQ/FIRQ during setup).
;;;   2. Enable all-RAM mode via SAM TY bit ($FFDF sets, $FFDE clears).
;;;      After this, $8000–$FEFF is writable RAM; BASIC ROMs are gone.
;;;   3. Word-copy the staged kernel from $1000 to $8000.
;;;      The copy uses LDD/STD (2 bytes per iteration) with BLO, which
;;;      handles odd kernel sizes safely (copies one extra byte at most).
;;;   4. JMP START to initialise hardware and enter the Forth application.
;;;
;;; The bootstrap itself sits at $0E00, safely below the staged kernel
;;; at $1000 and the application at $2000.

        ORG     $0E00

BOOTSTRAP
        ORCC    #$50            ; mask IRQ/FIRQ
        STA     $FFDF           ; all-RAM mode
        LDX     #$1000          ; source: staged kernel
        LDY     #$E000          ; dest: final location
BOOT_LP LDD     ,X++
        STD     ,Y++
        CMPY    #KERN_END
        BLO     BOOT_LP
        JMP     START

;;; ─── Kernel ──────────────────────────────────────────────────────────────────

        ORG     $E000

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
CFA_TYPE        FDB     CODE_TYPE
CFA_COUNT       FDB     CODE_COUNT
CFA_PLUS_STORE  FDB     CODE_PLUS_STORE
CFA_2DROP       FDB     CODE_2DROP
CFA_2DUP        FDB     CODE_2DUP
CFA_ROT         FDB     CODE_ROT
CFA_PROX_SCAN   FDB     CODE_PROX_SCAN
CFA_TOR         FDB     CODE_TOR
CFA_FROMR       FDB     CODE_FROMR
CFA_RAT         FDB     CODE_RAT
CFA_MIN         FDB     CODE_MIN
CFA_MAX         FDB     CODE_MAX
CFA_ABS         FDB     CODE_ABS
CFA_MDIST       FDB     CODE_MDIST
CFA_UNLOOP      FDB     CODE_UNLOOP
CFA_PICK        FDB     CODE_PICK
CFA_INVERT      FDB     CODE_INVERT
CFA_XOR         FDB     CODE_XOR
CFA_J           FDB     CODE_J
CFA_PLUS_LOOP   FDB     CODE_PLUS_LOOP
CFA_ULT         FDB     CODE_ULT
CFA_ZEROMAX     FDB     CODE_ZEROMAX
CFA_TWOSTAR     FDB     CODE_TWOSTAR
CFA_TWOSLASH    FDB     CODE_TWOSLASH
CFA_ZEROMIN     FDB     CODE_ZEROMIN
CFA_WITHIN      FDB     CODE_WITHIN

;;; ─── Sprite data table ─────────────────────────────────────────────────────
;;; DOVAR entry: calling sprite-data pushes the address of the first byte.
;;; 5 sprites × 12 bytes = 60 bytes inline.  Format: width, height, then
;;; 2bpp artifact-color rows (2 bytes/row).  Color encoding:
;;;   00=transparent  01=blue  10=red  11=white
;;;
;;; Application copies to its sprite region with:
;;;   sprite-data <dest> 60 CMOVE

CFA_SPRITE_DATA FDB     DOVAR
        ;;; Endever — blue chevron, twin red engines, 7×5
        FCB     7,5
        FCB     $01,$00,$04,$40,$15,$50,$55,$54,$42,$84
        ;;; Jovian — red (2) diamond, 7×5
        FCB     7,5
        FCB     $02,$00,$08,$80,$22,$20,$08,$80,$02,$00
        ;;; Base — blue ring, spokes, hollow center, 7×7
        FCB     7,7
        FCB     $05,$40,$11,$10,$41,$04,$54,$54,$41,$04,$11,$10,$05,$40
        ;;; Missile frame 1 — red (2) plus +, 3×3
        FCB     3,3
        FCB     $20,$A8,$20
        ;;; Missile frame 2 — red (2) cross x, 3×3
        FCB     3,3
        FCB     $88,$20,$88


;;; ─── Font glyph table ──────────────────────────────────────────────────────
;;; DOVAR entry: calling font-data pushes the address of the first byte.
;;; 59 glyphs × 8 bytes = 472 bytes inline.  Covers ASCII $20–$5A
;;; (space, punctuation, digits, uppercase A–Z).
;;; 1bpp artifact-color: each byte is one row, 3 artifact pairs in bits 7–2.
;;; Pixel pair 11=on, 00=off.  Bit 1–0 always 00 (inter-character gap).
;;;
;;; Application copies to its font region with:
;;;   font-data $9000 472 CMOVE

CFA_FONT_DATA   FDB     DOVAR
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $20 Space
        FCB     $30,$30,$30,$30,$30,$00,$30,$00  ; $21 !
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $22 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $23 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $24 unused
        FCB     $E3,$03,$0C,$30,$C3,$00,$00,$00  ; $25 %
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $26 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $27 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $28 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $29 unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $2A unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $2B unused
        FCB     $00,$00,$00,$00,$00,$30,$C0,$00  ; $2C ,
        FCB     $00,$00,$00,$FC,$00,$00,$00,$00  ; $2D -
        FCB     $00,$00,$00,$00,$00,$00,$30,$00  ; $2E .
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $2F unused
        FCB     $FC,$CC,$CC,$CC,$CC,$CC,$FC,$00  ; $30 0
        FCB     $30,$F0,$30,$30,$30,$30,$FC,$00  ; $31 1
        FCB     $FC,$CC,$0C,$FC,$C0,$C0,$FC,$00  ; $32 2
        FCB     $FC,$0C,$0C,$FC,$0C,$0C,$FC,$00  ; $33 3
        FCB     $CC,$CC,$CC,$FC,$0C,$0C,$0C,$00  ; $34 4
        FCB     $FC,$C0,$FC,$0C,$0C,$CC,$FC,$00  ; $35 5
        FCB     $FC,$C0,$C0,$FC,$CC,$CC,$FC,$00  ; $36 6
        FCB     $FC,$0C,$0C,$30,$30,$30,$30,$00  ; $37 7
        FCB     $FC,$CC,$CC,$FC,$CC,$CC,$FC,$00  ; $38 8
        FCB     $FC,$CC,$CC,$FC,$0C,$0C,$FC,$00  ; $39 9
        FCB     $00,$00,$30,$00,$00,$30,$00,$00  ; $3A :
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $3B unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $3C unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $3D unused
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $3E unused
        FCB     $FC,$0C,$0C,$3C,$00,$00,$30,$00  ; $3F ?
        FCB     $00,$00,$00,$00,$00,$00,$00,$00  ; $40 unused
        FCB     $FC,$CC,$CC,$FC,$CC,$CC,$CC,$00  ; $41 A
        FCB     $F0,$CC,$CC,$F0,$CC,$CC,$F0,$00  ; $42 B
        FCB     $FC,$C0,$C0,$C0,$C0,$C0,$FC,$00  ; $43 C
        FCB     $F0,$CC,$CC,$CC,$CC,$CC,$F0,$00  ; $44 D
        FCB     $FC,$C0,$C0,$FC,$C0,$C0,$FC,$00  ; $45 E
        FCB     $FC,$C0,$C0,$FC,$C0,$C0,$C0,$00  ; $46 F
        FCB     $FC,$C0,$C0,$FC,$CC,$CC,$FC,$00  ; $47 G
        FCB     $CC,$CC,$CC,$FC,$CC,$CC,$CC,$00  ; $48 H
        FCB     $FC,$30,$30,$30,$30,$30,$FC,$00  ; $49 I
        FCB     $3C,$0C,$0C,$0C,$0C,$CC,$3C,$00  ; $4A J
        FCB     $CC,$CC,$F0,$F0,$CC,$CC,$CC,$00  ; $4B K
        FCB     $C0,$C0,$C0,$C0,$C0,$C0,$FC,$00  ; $4C L
        FCB     $CC,$FC,$FC,$CC,$CC,$CC,$CC,$00  ; $4D M
        FCB     $CC,$CC,$FC,$FC,$CC,$CC,$CC,$00  ; $4E N
        FCB     $FC,$CC,$CC,$CC,$CC,$CC,$FC,$00  ; $4F O
        FCB     $FC,$CC,$CC,$FC,$C0,$C0,$C0,$00  ; $50 P
        FCB     $FC,$CC,$CC,$CC,$CC,$FC,$0C,$00  ; $51 Q
        FCB     $FC,$CC,$CC,$FC,$F0,$CC,$CC,$00  ; $52 R
        FCB     $FC,$C0,$C0,$FC,$0C,$0C,$FC,$00  ; $53 S
        FCB     $FC,$30,$30,$30,$30,$30,$30,$00  ; $54 T
        FCB     $CC,$CC,$CC,$CC,$CC,$CC,$FC,$00  ; $55 U
        FCB     $CC,$CC,$CC,$CC,$CC,$F0,$F0,$00  ; $56 V
        FCB     $CC,$CC,$CC,$CC,$FC,$FC,$CC,$00  ; $57 W
        FCB     $CC,$CC,$FC,$FC,$FC,$CC,$CC,$00  ; $58 X
        FCB     $CC,$CC,$CC,$FC,$30,$30,$30,$00  ; $59 Y
        FCB     $FC,$0C,$0C,$FC,$C0,$C0,$FC,$00  ; $5A Z


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
        BSR     KEY_SCAN        ; A = ASCII key or 0
        TFR     A,B             ; B = char (or 0)
        CLRA                    ; A = 0 (high byte)
        STD     ,--U            ; push result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── KEY_SCAN — subroutine: scan keyboard, return ASCII in A ───────────────
;;; Returns A = ASCII character, or 0 if no key pressed.
;;; Callable from CODE words via BSR/LBSR. Preserves X, U.

KEY_SCAN
        CLR     $FF02           ; strobe all columns
        LDA     $FF00           ; read all row bits (active-low)
        COMA                    ; invert: pressed=1
        ANDA    #$7F            ; mask joystick bit
        BEQ     @done           ; nothing → A=0
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
        BEQ     @done           ; modifier only → return 0
        TST     VAR_KEY_SHIFT
        BEQ     @done
        BSR     SHIFT_APPLY
@done   RTS

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

;;; ─── MIN ( n1 n2 -- smaller ) ───────────────────────────────────────────────
;;; Keep the smaller of two signed values.

CODE_MIN
        LDD     ,U              ; D = n2 (TOS)
        CMPD    2,U             ; n2 vs n1
        BLE     MIN_OK          ; n2 <= n1, keep n2
        LDD     2,U             ; else keep n1
MIN_OK  LEAU    2,U             ; pop one cell
        STD     ,U              ; store result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── MAX ( n1 n2 -- larger ) ───────────────────────────────────────────────
;;; Keep the larger of two signed values.

CODE_MAX
        LDD     ,U              ; D = n2 (TOS)
        CMPD    2,U             ; n2 vs n1
        BGE     MAX_OK          ; n2 >= n1, keep n2
        LDD     2,U             ; else keep n1
MAX_OK  LEAU    2,U             ; pop one cell
        STD     ,U              ; store result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── ABS ( n -- |n| ) ─────────────────────────────────────────────────────
;;; Absolute value. If negative, negate.

CODE_ABS
        LDD     ,U              ; D = TOS
        BPL     ABS_OK          ; already positive
        COMA
        COMB
        ADDD    #1              ; negate
        STD     ,U
ABS_OK  LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── U< ( u1 u2 -- flag ) ──────────────────────────────────────────────────
;;; Unsigned less-than comparison.

CODE_ULT
        LDD     2,U             ; D = u1 (NOS)
        CMPD    ,U              ; u1 vs u2
        BLO     ULT_T           ; unsigned: u1 < u2
        LDD     #0
        BRA     ULT_D
ULT_T   LDD     #1
ULT_D   LEAU    2,U             ; pop one cell
        STD     ,U              ; store flag
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── 0MAX ( n -- max(n,0) ) ────────────────────────────────────────────────
;;; Clamp to non-negative. If negative, replace with 0.

CODE_ZEROMAX
        LDD     ,U              ; D = TOS
        BPL     ZMAX_OK         ; already >= 0
        CLRA
        CLRB
        STD     ,U
ZMAX_OK LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── 2* ( n -- n*2 ) ───────────────────────────────────────────────────────
;;; Arithmetic shift left by 1.

CODE_TWOSTAR
        LDD     ,U              ; D = TOS
        ASLB
        ROLA                    ; D = D * 2
        STD     ,U
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── 2/ ( n -- n/2 ) ───────────────────────────────────────────────────────
;;; Arithmetic shift right by 1 (sign-preserving).

CODE_TWOSLASH
        LDD     ,U              ; D = TOS
        ASRA
        RORB                    ; D = D / 2 (signed)
        STD     ,U
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── 0MIN ( n -- min(n,0) ) ────────────────────────────────────────────────
;;; Clamp to non-positive. If positive, replace with 0.

CODE_ZEROMIN
        LDD     ,U              ; D = TOS
        BMI     ZMIN_OK         ; already < 0
        BEQ     ZMIN_OK         ; zero is fine
        CLRA
        CLRB
        STD     ,U
ZMIN_OK LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── WITHIN ( n lo hi -- flag ) ────────────────────────────────────────────
;;; True if lo <= n < hi (standard Forth WITHIN using unsigned trick).
;;; Implementation: n-lo u< hi-lo

CODE_WITHIN
        LDD     2,U             ; D = lo
        PSHS    D               ; save lo
        LDD     ,U              ; D = hi
        SUBD    ,S              ; D = hi - lo
        PSHS    D               ; save (hi - lo)
        LDD     4+2,U           ; D = n
        SUBD    2,S             ; D = n - lo
        CMPD    ,S              ; (n - lo) vs (hi - lo)
        LEAS    4,S             ; clean temp
        BLO     WITH_T          ; unsigned: n-lo < hi-lo
        LDD     #0
        BRA     WITH_D
WITH_T  LDD     #1
WITH_D  LEAU    4,U             ; pop 3 cells, push 1
        STD     ,U
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── MDIST ( addr1 addr2 -- d ) ────────────────────────────────────────────
;;; Manhattan distance between two (x,y) byte pairs in memory.
;;; d = |addr1[0]-addr2[0]| + |addr1[1]-addr2[1]|

CODE_MDIST
        PSHS    X               ; save IP
        LDY     ,U              ; Y = addr2
        LDX     2,U             ; X = addr1
        LEAU    2,U             ; pop one cell (result replaces addr1)
        LDA     ,X              ; A = x1
        SUBA    ,Y              ; A = x1 - x2
        BPL     MDIST_AX
        NEGA                    ; A = |x1 - x2|
MDIST_AX
        PSHS    A               ; save |dx|
        LDA     1,X             ; A = y1
        SUBA    1,Y             ; A = y1 - y2
        BPL     MDIST_AY
        NEGA                    ; A = |y1 - y2|
MDIST_AY
        ADDA    ,S+             ; A = |dx| + |dy|
        TFR     A,B             ; B = result low byte
        CLRA                    ; D = 16-bit result
        STD     ,U              ; store result
        PULS    X               ; restore IP
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── UNLOOP ( -- ) (R: index limit -- ) ─────────────────────────────────────
;;; Remove DO/LOOP control parameters from return stack for early EXIT.

CODE_UNLOOP
        LEAS    4,S             ; pop index + limit from return stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── PICK ( n -- x ) ──────────────────────────────────────────────────────
;;; Copy the nth stack item to TOS. 0 PICK = DUP, 1 PICK = OVER.

CODE_PICK
        LDD     ,U              ; D = n
        ASLB
        ROLA                    ; D = n*2 (byte offset)
        LDD     D,U             ; D = stack[n]
        STD     ,U              ; replace TOS
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── INVERT ( n -- ~n ) ───────────────────────────────────────────────────
;;; Bitwise complement (ones' complement).

CODE_INVERT
        LDD     ,U
        COMA
        COMB
        STD     ,U
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── XOR ( n1 n2 -- n3 ) ─────────────────────────────────────────────────
;;; Bitwise exclusive or.

CODE_XOR
        LDD     ,U              ; D = n2
        EORA    2,U             ; A ^= n1 high
        EORB    3,U             ; B ^= n1 low
        LEAU    2,U             ; pop one cell
        STD     ,U              ; store result
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── J ( -- n ) (R: ... outer-index outer-limit inner-index inner-limit)──
;;; Push the outer loop index in nested DO/LOOPs.

CODE_J
        LDD     4,S             ; outer index is 4 bytes deep
        STD     ,--U            ; push onto data stack
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── +LOOP ( n -- ) (R: index limit -- or loop continues) ────────────────
;;; Like LOOP but increments index by n. Uses signed overflow detection:
;;; loop terminates when the index crosses the limit boundary in either
;;; direction (the sign of (index-limit) changes after adding n).

CODE_PLUS_LOOP
        LDD     ,U              ; D = increment n
        LEAU    2,U             ; pop n from data stack
        PSHS    D               ; save n
        LDD     2,S             ; D = current index
        SUBD    4,S             ; D = index - limit (old sign)
        TFR     D,Y             ; Y = old (index - limit)
        LDD     ,S++            ; D = n, pop from S
        ADDD    ,S              ; D = index + n
        STD     ,S              ; store new index
        SUBD    2,S             ; D = new_index - limit (new sign)
        ; Loop terminates when sign of (index-limit) changes
        ; XOR old and new: if high bit differs, sign changed
        PSHS    A               ; save new sign byte
        TFR     Y,D             ; D = old (index-limit)
        EORA    ,S+             ; A = old_hi XOR new_hi
        BMI     PLOOP_DONE      ; sign bit set = sign changed = done
        ; Not done — branch back
        LDD     ,X              ; D = back-branch offset
        LEAX    2,X             ; advance IP past offset cell
        LEAX    D,X             ; apply signed offset
        LDY     ,X++            ; NEXT
        JMP     [,Y]
PLOOP_DONE
        LEAS    4,S             ; pop index + limit from return stack
        LEAX    2,X             ; skip over offset cell
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

;;; ─── TYPE ( addr len -- ) ───────────────────────────────────────────────────
;;; Output len characters starting at addr to the VDG text screen.
;;; Calls EMIT logic inline for speed (same encoding: $40 | (ascii & $3F)).

CODE_TYPE
        PSHS    X               ; save IP — S: [IP]
        LDD     ,U              ; D = count (TOS)
        BEQ     TYPE_DONE       ; zero count → skip
        LDX     2,U             ; X = string address (NOS)
        PSHS    D               ; save count — S: [count, IP]
TYPE_LP LDA     ,X+             ; A = next char
        PSHS    X               ; save string ptr — S: [ptr, count, IP]
        ANDA    #$3F            ; strip to 6-bit VDG
        ORA     #$40            ; set normal-video bit
        LDY     VAR_CUR
        STA     SCREEN,Y
        LEAY    1,Y
        CMPY    #NSCR
        BLO     TYPE_NOK
        LDY     #0
TYPE_NOK STY    VAR_CUR
        PULS    X               ; restore string ptr — S: [count, IP]
        LDD     ,S              ; load count
        SUBD    #1
        STD     ,S              ; save decremented count
        BNE     TYPE_LP
        LEAS    2,S             ; pop count — S: [IP]
TYPE_DONE
        LEAU    4,U             ; drop both args
        PULS    X               ; restore IP
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── COUNT ( c-addr -- addr+1 len ) ────────────────────────────────────────
;;; Convert a counted string (length byte + chars) to addr+len form.

CODE_COUNT
        LDY     ,U              ; Y = c-addr
        LDB     ,Y+             ; B = length byte
        STY     ,U              ; replace c-addr with addr+1
        CLRA                    ; D = 0:len
        STD     ,--U            ; push len
        LDY     ,X++            ; NEXT
        JMP     [,Y]

;;; ─── +! ( n addr -- ) ───────────────────────────────────────────────────────
;;; Add n to the cell at addr.

CODE_PLUS_STORE
        LDY     ,U++            ; pop addr
        LDD     ,U++            ; pop n
        ADDD    ,Y              ; add to cell
        STD     ,Y              ; store back
        LDY     ,X++
        JMP     [,Y]

;;; ─── 2DROP ( a b -- ) ──────────────────────────────────────────────────────
;;; Drop the top two stack items.

CODE_2DROP
        LEAU    4,U             ; drop 2 cells
        LDY     ,X++
        JMP     [,Y]

;;; ─── 2DUP ( a b -- a b a b ) ───────────────────────────────────────────────
;;; Duplicate the top two stack items.

CODE_2DUP
        LDD     2,U             ; a (NOS)
        STD     ,--U            ; push a
        LDD     2,U             ; b (was TOS, now at +2 after push)
        STD     ,--U            ; push b
        LDY     ,X++
        JMP     [,Y]

;;; ─── ROT ( a b c -- b c a ) ────────────────────────────────────────────────
;;; Rotate third item to top.

CODE_ROT
        LDD     4,U             ; a (third)
        LDY     2,U             ; b (second)
        STY     4,U             ; b → third
        LDY     ,U              ; c (TOS)
        STY     2,U             ; c → second
        STD     ,U              ; a → TOS
        LDY     ,X++
        JMP     [,Y]

;;; ─── PROX-SCAN ( cx cy radius array count -- bitmask ) ─────────────────────
;;; Spatial proximity query.  Scan count (x,y) byte pairs at array.
;;; Return 16-bit bitmask: bit N set if entry N is within Manhattan
;;; distance radius of (cx,cy).  Max 16 entries (bits 0–15).
;;;
;;; Stack on entry: count(TOS), array, radius, cy, cx (deepest)
;;; Uses return stack for loop parameters; saves/restores IP.

CODE_PROX_SCAN
        PSHS    X               ; save IP
        LDB     1,U             ; B = count (low byte of TOS)
        BEQ     PS_ZERO         ; count=0 → return 0
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
        ;;; Offsets: 0=bit_hi, 1=bit_lo, 2=count, 3=cx, 4=cy, 5=rad
PS_LOOP
        LDA     ,X+             ; A = entry x
        SUBA    3,S             ; A = x - cx
        BPL     PS_AX
        NEGA
PS_AX   LDB     ,X+             ; B = entry y
        SUBB    4,S             ; B = y - cy
        BPL     PS_AY
        NEGB
PS_AY   PSHS    A               ; save |dx|
        ADDB    ,S+             ; B = |dx| + |dy|, pop |dx|
        BCS     PS_SKIP         ; overflow > 255, definitely out of range
        CMPB    5,S             ; compare with radius
        BHS     PS_SKIP         ; B >= radius → out of range
        ;;; In range — OR bit into Y
        TFR     Y,D
        ORA     ,S              ; OR bit_hi
        ORB     1,S             ; OR bit_lo
        TFR     D,Y
PS_SKIP
        LSL     1,S             ; bit <<= 1
        ROL     ,S
        DEC     2,S             ; count--
        BNE     PS_LOOP
        ;;; Done — clean up and return
        LEAS    6,S             ; pop [bit, count, cx, cy, rad]
        STY     ,--U            ; push bitmask result
        PULS    X               ; restore IP
        LDY     ,X++
        JMP     [,Y]
PS_ZERO
        LEAU    10,U            ; pop all 5 args
        LDD     #0
        STD     ,--U            ; push 0
        PULS    X               ; restore IP
        LDY     ,X++
        JMP     [,Y]

;;; ─── >R ( n -- ) (R: -- n ) ─────────────────────────────────────────────────
;;; Move TOS to return stack.

CODE_TOR
        LDD     ,U++            ; pop TOS
        PSHS    D               ; push onto return stack
        LDY     ,X++
        JMP     [,Y]

;;; ─── R> ( -- n ) (R: n -- ) ─────────────────────────────────────────────────
;;; Move return stack TOS to data stack.

CODE_FROMR
        PULS    D               ; pop from return stack
        STD     ,--U            ; push onto data stack
        LDY     ,X++
        JMP     [,Y]

;;; ─── R@ ( -- n ) (R: n -- n ) ──────────────────────────────────────────────
;;; Copy return stack TOS to data stack (non-destructive).

CODE_RAT
        LDD     ,S              ; peek return stack TOS
        STD     ,--U            ; push onto data stack
        LDY     ,X++
        JMP     [,Y]

CODE_HALT
        BRA     CODE_HALT

;;; ─── START ─────────────────────────────────────────────────────────────────
;;; Hardware initialisation and application entry.
;;; Called by BOOTSTRAP after the kernel has been copied to $8000.
;;; At this point all-RAM mode is active and interrupts are masked.
;;;
;;; Sets up:
;;;   - Direct page register ($00)
;;;   - Return stack at $8000 (grows down into $7FFE, below kernel)
;;;   - Data stack at $7E00 (grows down)
;;;   - PIA0 for bare-metal keyboard scanning
;;;   - Text screen cleared to VDG spaces
;;;   - Cursor position zeroed
;;; Then enters the Forth application thread at APP_BASE via NEXT.

APP_BASE EQU    $2000           ; application binary loaded here

START
        CLRA
        TFR     A,DP            ; direct page register = $00

        LDS     #$E000          ; RSP: first push lands at $DFFE (below kernel)
        LDU     #$DE00          ; DSP: first push lands at $DDFE

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

;;; ─── VSYNC ( -- ) ──────────────────────────────────────────────────────────
;;; Wait for vertical sync.  Polls PIA0 CRB ($FF03) bit 7, clears by
;;; reading $FF02.  60 Hz on NTSC.

CFA_VSYNC       FDB     CODE_VSYNC
CODE_VSYNC
        PSHS    X
@poll   LDA     $FF03           ; bit 7 = VSYNC flag
        BPL     @poll           ; loop until set
        LDA     $FF02           ; clear flag
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── WAIT-PAST-ROW ( row -- ) ─────────────────────────────────────────────
;;; After VSYNC, poll HSYNC to wait until the beam has passed the given
;;; display row (0-191).  Blanking offset 70 lines (VSYNC to row 0).

CFA_WAIT_PAST_ROW FDB   CODE_WAIT_PAST_ROW
CODE_WAIT_PAST_ROW
        PSHS    X
        LDB     1,U             ; row (0-191)
        LEAU    2,U             ; pop arg
        ADDB    #70             ; add full blanking interval
        BEQ     @done
        LDA     $FF00           ; clear stale HSYNC flag
@wt     LDA     $FF01           ; check HSYNC flag (bit 7)
        BPL     @wt
        LDA     $FF00           ; clear flag
        DECB
        BNE     @wt
@done   PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── COUNT-BLANKING ( -- n ) ───────────────────────────────────────────────
;;; Diagnostic: after VSYNC, count 100 HSYNC pulses and return count.

CFA_COUNT_BLANKING FDB  CODE_COUNT_BLANKING
CODE_COUNT_BLANKING
        PSHS    X
@vs     LDA     $FF03           ; poll VSYNC flag
        BPL     @vs
        LDA     $FF02           ; clear VSYNC flag
        CLRA
        CLRB                    ; D = 0
        LDY     #100
@hs     LDA     $FF01           ; check HSYNC
        BPL     @hs
        LDA     $FF00           ; clear flag
        ADDD    #1
        LEAY    -1,Y
        BNE     @hs
        LEAU    -2,U
        STD     ,U              ; push count
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── rg-pset ( x y color -- ) ──────────────────────────────────────────────
CFA_RG_PSET     FDB     CODE_RG_PSET
CODE_RG_PSET
        LDA     5,U
        LDB     3,U
        PSHS    A
        LDA     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     ,S
        LSRA
        LSRA
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDA     1,U
        ANDA    #$03
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        LEAU    6,U
        LDY     ,X++
        JMP     [,Y]

;;; ─── rg-line ( x1 y1 x2 y2 color -- ) ────────────────────────────────────
CFA_RG_LINE     FDB     CODE_RG_LINE
CODE_RG_LINE
        PSHS    X
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
        LEAU    10,U
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
@sx_d   CLRA
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
@sy_d   CLRA
        LDB     VAR_LINE_DX
        STD     VAR_LINE_ERR
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
@loop   LDA     VAR_LINE_CY
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     VAR_LINE_CX
        LSRA
        LSRA
        LEAY    A,Y
        LDA     VAR_LINE_CX
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDA     VAR_LINE_COL
        ANDA    #$03
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        LDA     VAR_LINE_CX
        CMPA    VAR_LINE_X2
        BNE     @step
        LDA     VAR_LINE_CY
        CMPA    VAR_LINE_Y2
        BEQ     @done
@step   LDD     VAR_LINE_ERR
        ASLB
        ROLA
        STD     VAR_LINE_E2
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
@nosx   CLRA
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
@nosy   LBRA    @loop
@done   PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── spr-draw ( addr x y -- ) ─────────────────────────────────────────────
CFA_SPR_DRAW    FDB     CODE_SPR_DRAW
CODE_SPR_DRAW
        PSHS    X
        LDA     3,U
        STA     VAR_SPR_SX
        LDA     1,U
        STA     VAR_SPR_SY
        LDY     4,U
        LEAU    6,U
        LDA     ,Y
        STA     VAR_SPR_W
        LDA     1,Y
        STA     VAR_SPR_H
        LEAY    2,Y
        STY     VAR_SPR_SA
        LDA     VAR_SPR_W
        ADDA    #3
        LSRA
        LSRA
        STA     VAR_SPR_BPR
        CLR     VAR_SPR_ROW
@row    LDA     VAR_SPR_SY
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
@col    LDA     VAR_SPR_COL
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
@skip   INC     VAR_SPR_COL
        LDA     VAR_SPR_COL
        CMPA    VAR_SPR_W
        BNE     @col
        INC     VAR_SPR_ROW
        LDA     VAR_SPR_ROW
        CMPA    VAR_SPR_H
        LBNE    @row
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── spr-erase-box ( addr x y -- ) ────────────────────────────────────────
CFA_SPR_ERASE_BOX FDB   CODE_SPR_ERASE_BOX
CODE_SPR_ERASE_BOX
        PSHS    X
        LDA     3,U
        STA     VAR_SPR_SX
        LDA     1,U
        STA     VAR_SPR_SY
        LDY     4,U
        LEAU    6,U
        LDA     ,Y
        STA     VAR_SPR_W
        LDA     1,Y
        STA     VAR_SPR_H
        CLR     VAR_SPR_ROW
@row    LDA     VAR_SPR_SY
        ADDA    VAR_SPR_ROW
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        STD     VAR_SPR_VROW
        CLR     VAR_SPR_COL
@col    LDA     VAR_SPR_SX
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
        LDY     ,X++
        JMP     [,Y]

;;; ─── rg-char ( char cx cy -- ) ────────────────────────────────────────────
CFA_RG_CHAR     FDB     CODE_RG_CHAR
CODE_RG_CHAR
        PSHS    X
        LDA     1,U
        LDB     VAR_RGROWH
        MUL
        TFR     B,A
        LDB     VAR_RGBPR
        MUL
        ADDB    3,U
        ADCA    #0
        ADDD    VAR_RGVRAM
        PSHS    D
        LDA     5,U
        CMPA    VAR_RGCHARMIN
        BHS     @over
        LDA     VAR_RGCHARMIN
@over   SUBA    VAR_RGCHARMIN
        LDB     VAR_RGGLYPHSZ
        MUL
        ADDD    VAR_RGFONT
        TFR     D,Y
        LDB     VAR_RGNROWS
        LDX     ,S++
@copy   LDA     ,Y+
        STA     ,X
        LDA     VAR_RGBPR
        LEAX    A,X
        DECB
        BNE     @copy
        LEAU    6,U
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── beam-trace ( x1 y1 x2 y2 buf -- count ) ─────────────────────────────
CFA_BEAM_TRACE  FDB     CODE_BEAM_TRACE
CODE_BEAM_TRACE
        PSHS    X
        LDD     ,U
        STD     VAR_BEAM_BUF
        LDA     5,U
        STA     VAR_LINE_X2
        LDA     3,U
        STA     VAR_LINE_Y2
        LDA     9,U
        STA     VAR_LINE_CX
        LDA     7,U
        STA     VAR_LINE_CY
        LEAU    10,U
        LDD     #0
        STD     VAR_BEAM_CNT
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
@sx_d   CLRA
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
@sy_d   CLRA
        LDB     VAR_LINE_DX
        STD     VAR_LINE_ERR
        CLRA
        LDB     VAR_LINE_DY
        PSHS    D
        LDD     VAR_LINE_ERR
        SUBD    ,S++
        STD     VAR_LINE_ERR
@loop   LDA     VAR_LINE_CY
        LDB     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     VAR_LINE_CX
        LSRA
        LSRA
        LEAY    A,Y
        LDA     VAR_LINE_CX
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        LDB     ,Y
        PSHS    A
        TSTA                    ; bug fix (#446): BEQ must test A (shift),
        BEQ     @nr             ; not B (VRAM byte from LDB)
@rsh    LSRB
        DECA
        BNE     @rsh
@nr     ANDB    #$03
        LEAS    1,S
        LDY     VAR_BEAM_BUF
        LDA     VAR_LINE_CX
        STA     ,Y+
        LDA     VAR_LINE_CY
        STA     ,Y+
        STB     ,Y+
        STY     VAR_BEAM_BUF
        LDD     VAR_BEAM_CNT
        ADDD    #1
        STD     VAR_BEAM_CNT
        CMPD    #200
        BHS     @done
        LDA     VAR_LINE_CX
        CMPA    VAR_LINE_X2
        BNE     @step
        LDA     VAR_LINE_CY
        CMPA    VAR_LINE_Y2
        BEQ     @done
@step   LDD     VAR_LINE_ERR
        ASLB
        ROLA
        STD     VAR_LINE_E2
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
@nosx   CLRA
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
@nosy   LBRA    @loop
@done   LDD     VAR_BEAM_CNT
        STD     ,--U
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── beam-draw-slice ( buf start count color -- ) ─────────────────────────
CFA_BEAM_DRAW_SLICE FDB  CODE_BEAM_DRAW_SLICE
CODE_BEAM_DRAW_SLICE
        PSHS    X
        LDA     1,U
        ANDA    #$03
        STA     VAR_LINE_COL
        LDD     2,U
        STD     VAR_BEAM_CNT
        LDD     4,U
        PSHS    D
        ASLB
        ROLA
        ADDD    ,S++
        ADDD    6,U
        STD     VAR_BEAM_BUF
        LEAU    8,U
        LDD     VAR_BEAM_CNT
        BEQ     @done
        LDX     VAR_BEAM_BUF
@ploop  LDA     ,X
        LDB     1,X
        PSHS    X
        PSHS    A
        LDA     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     ,S
        LSRA
        LSRA
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDA     VAR_LINE_COL
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        PULS    X
        LEAX    3,X
        LDD     VAR_BEAM_CNT
        SUBD    #1
        STD     VAR_BEAM_CNT
        BNE     @ploop
@done   PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── beam-restore-slice ( buf start count -- ) ────────────────────────────
CFA_BEAM_RESTORE_SLICE FDB CODE_BEAM_RESTORE_SLICE
CODE_BEAM_RESTORE_SLICE
        PSHS    X
        LDD     ,U
        STD     VAR_BEAM_CNT
        LDD     2,U
        PSHS    D
        ASLB
        ROLA
        ADDD    ,S++
        ADDD    4,U
        STD     VAR_BEAM_BUF
        LEAU    6,U
        LDD     VAR_BEAM_CNT
        BEQ     @done
        LDX     VAR_BEAM_BUF
@ploop  LDA     ,X
        LDB     1,X
        PSHS    X
        PSHS    A
        LDA     #32
        MUL
        ADDD    VAR_RGVRAM
        TFR     D,Y
        LDA     ,S
        LSRA
        LSRA
        LEAY    A,Y
        LDA     ,S+
        ANDA    #$03
        ASLA
        NEGA
        ADDA    #6
        PSHS    A
        LDX     1,S
        LDA     2,X
        ANDA    #$03
        LDB     ,S
        BEQ     @ns
@sh     ASLA
        DECB
        BNE     @sh
@ns     PSHS    A
        LDA     #$03
        LDB     1,S
        BEQ     @nm
@sm     ASLA
        DECB
        BNE     @sm
@nm     COMA
        ANDA    ,Y
        ORA     ,S
        STA     ,Y
        LEAS    2,S
        PULS    X
        LEAX    3,X
        LDD     VAR_BEAM_CNT
        SUBD    #1
        STD     VAR_BEAM_CNT
        BNE     @ploop
@done   PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── beam-find-obstacle ( buf count -- index ) ────────────────────────────
CFA_BEAM_FIND_OBSTACLE FDB CODE_BEAM_FIND_OBSTACLE
CODE_BEAM_FIND_OBSTACLE
        PSHS    X
        LDD     ,U
        LDX     2,U
        LEAU    4,U
        CMPD    #0
        BEQ     @push
        TFR     D,Y
        CLRA
        CLRB
@lp     TST     2,X
        BNE     @push
        LEAX    3,X
        ADDD    #1
        LEAY    -1,Y
        BNE     @lp
@push   LEAU    -2,U
        STD     ,U
        PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── beam-scrub-pos ( buf count cx cy -- ) ────────────────────────────────
CFA_BEAM_SCRUB_POS FDB  CODE_BEAM_SCRUB_POS
CODE_BEAM_SCRUB_POS
        PSHS    X
        LDD     4,U
        BEQ     @done
        TFR     D,Y
        LDX     6,U
        LDA     3,U
        SUBA    #4
        PSHS    A
        ADDA    #8
        PSHS    A
        LDA     1,U
        SUBA    #3
        PSHS    A
        ADDA    #6
        PSHS    A
        LEAU    8,U
@lp     LDA     ,X
        CMPA    3,S
        BLO     @skip
        CMPA    2,S
        BHI     @skip
        LDA     1,X
        CMPA    1,S
        BLO     @skip
        CMPA    ,S
        BHI     @skip
        CLR     2,X
@skip   LEAX    3,X
        LEAY    -1,Y
        BNE     @lp
        LEAS    4,S
@done   PULS    X
        LDY     ,X++
        JMP     [,Y]

;;; ─── Kernel Variables ────────────────────────────────────────────────────────
;;; Placed in kernel space so they are copied to all-RAM with the kernel.
;;; Accessed via extended addressing (relocatable, no direct-page dependency).
;;; Living here keeps them out of BASIC's workspace, making LOADM safe.

VAR_CUR         FDB     0       ; cursor offset into video RAM (0–511)
VAR_KEY_PREV    FCB     0       ; last accepted key ASCII (KEY debounce)
VAR_KEY_SHIFT   FCB     0       ; SHIFT flag (nonzero = shift held)
VAR_KEY_RELCNT  FCB     0       ; release debounce counter
VAR_KEY_REPDLY  FDB     0       ; auto-repeat countdown (16-bit)
VAR_RGVRAM      FDB     $0600   ; RG6 VRAM base address (written by rg-init)
;;; Bresenham line drawing scratch (used by rg-line CODE word in rg-pixel.fs)
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
;;; Sprite drawing scratch (used by spr-draw/spr-erase-box CODE words in sprite.fs)
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
;;; RG-CHAR text rendering config (used by rg-char CODE word in rg-text.fs)
VAR_RGFONT      FDB     $7400   ; font table base address
VAR_RGCHARMIN   FCB     $20     ; minimum ASCII code (chars below → this)
VAR_RGGLYPHSZ   FCB     8       ; bytes per glyph
VAR_RGNROWS     FCB     8       ; rows to copy per glyph
VAR_RGBPR       FCB     32      ; bytes per VRAM row
VAR_RGROWH      FCB     10      ; row height for cy positioning (pixels)
;;; Beam system scratch (used by beam-trace/draw-slice/restore-slice in beam.fs)
VAR_BEAM_BUF    FDB     0       ; path buffer pointer (during trace/draw/restore)
VAR_BEAM_VRAM   FDB     0       ; VRAM byte address scratch
VAR_BEAM_CNT    FDB     0       ; pixel count / loop counter scratch

KERN_END                        ; end marker — bootstrap copies $E000..KERN_END-1

        END     BOOTSTRAP       ; DECB exec address = BOOTSTRAP ($0E00)
