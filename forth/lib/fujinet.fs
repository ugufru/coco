\ fujinet.fs — FujiNet device support for the CoCo
\
\ Wraps HDB-DOS's DriveWire bit-banger driver as exposed by the
\ FujiNet CoCo cartridge.  HDB-DOS publishes DriveWire entry
\ vectors inside its cart ROM at $D93F (read) and $D941 (write);
\ the vectors dereference to DWRead/DWWrite routines at $C000-
\ $DFFF.  The FujiNet cart ships these addresses by default —
\ same layout as the CMOC fujinet-lib CoCo client.
\
\ Because these vectors and their routines live in cart ROM, the
\ kernel's all-RAM mode has to be suspended for each call.  Each
\ primitive:
\   1. saves the kernel return stack (S) — it lives at $DFxx,
\      inside the ROM overlay window, so we switch to a small
\      temp stack held inside the CODE word body
\   2. masks IRQ/FIRQ (bit-banger timing is cycle-sensitive)
\   3. flips SAM TY=0 (ROM mode) so cart ROM is visible
\   4. JSR through the vector
\   5. flips SAM TY=1 back to all-RAM
\   6. restores the kernel S
\ Same ROM/RAM pattern discussed in src/sound/SOUND.md.
\
\ All internal references use PC-relative (PCR) addressing so the
\ words are position-independent — fc.py assembles each CODE word
\ at ORG $0000 then relocates the bytes into the app binary.
\
\ Provides:
\   dw-write ( buf len -- )       send len bytes from buf
\   dw-read  ( buf len -- ok )    read len bytes into buf; ok = 1 on success
\   fn-ready ( -- )               FujiNet $E2,$00 ping until the device acks
\   fn-time  ( buf -- )           write 6-byte time into buf:
\                                   buf[0]=year-1900  buf[3]=hour
\                                   buf[1]=month      buf[4]=minute
\                                   buf[2]=day        buf[5]=second
\
\ Reference: github.com/FujiNetWIFI/fujinet-lib (coco/src/bus/
\ dwread.c, dwwrite.c) and HDB-DOS source hdbdos.asm ($D930+ table).


\ ── Low-level DriveWire bit-banger primitives ────────────────────────

CODE dw-write
        ;;; ( buf len -- )  send len bytes via HDB-DOS DWWrite vector at $D941
        PSHS    X                       ; save IP on kernel return stack
        LDX     2,U                     ; X = buf addr (NOS)
        LDD     ,U                      ; D = len (TOS)
        TFR     D,Y                     ; Y = len  (DWWrite API)
        LEAU    4,U                     ; pop 2 cells from data stack

        STS     @save_s,PCR             ; remember kernel S
        LEAS    @stk_top,PCR            ; switch to local temp stack

        ORCC    #$50                    ; mask IRQ + FIRQ for timing
        STA     $FFDE                   ; SAM TY=0 — cart ROM visible
        JSR     [$D941]                 ; HDB-DOS DWWrite vector
        STA     $FFDF                   ; SAM TY=1 — all-RAM restored

        LDS     @save_s,PCR             ; restore kernel S
        PULS    X                       ; restore IP
        ;NEXT

@save_s FDB     0
        RMB     128                     ; temp-stack body
@stk_top
;CODE


CODE dw-read
        ;;; ( buf len -- ok )  read len bytes from HDB-DOS DWRead vector at $D93F
        ;;; ok = 1 on successful read (Z flag set on return), 0 on timeout
        PSHS    X
        LDX     2,U                     ; X = buf
        LDD     ,U                      ; D = len
        TFR     D,Y                     ; Y = len
        LEAU    2,U                     ; pop len, leave buf cell for result

        STS     @save_s,PCR
        LEAS    @stk_top,PCR

        ORCC    #$50
        STA     $FFDE
        JSR     [$D93F]                 ; HDB-DOS DWRead vector
        TFR     CC,B                    ; capture status flags before STA
        STA     $FFDF                   ; back to all-RAM

        LDS     @save_s,PCR             ; restore kernel S

        ;;; Extract Z flag from captured CC.  CC bit 2 is Z; DWRead
        ;;; returns with Z=1 on success (matches CMOC fujinet-lib
        ;;; convention: lsrb / lsrb / andb #$01).
        LSRB
        LSRB
        ANDB    #$01
        CLRA
        STD     ,U                      ; overwrite TOS cell with (0, ok)

        PULS    X
        ;NEXT

@save_s FDB     0
        RMB     128
@stk_top
;CODE


\ ── DriveWire FujiNet opcodes ──────────────────────────────────────
$E2 CONSTANT FN-OP-FUJI                 \ Fuji device opcode
$00 CONSTANT FN-CMD-READY               \ ping / handshake
$23 CONSTANT FN-CMD-TIME                \ get time, returns 6 bytes


\ ── 2-byte scratch buffer for outgoing command bytes ──────────────
VARIABLE fn-cmd


\ ── fn-ping: single handshake attempt ( -- ok ) ─────────────────
\ Sends (FN-OP-FUJI, FN-CMD-READY) and reads 1 byte.  Returns
\ 1 if the FujiNet answered, 0 on timeout.
: fn-ping  ( -- ok )
  FN-OP-FUJI    fn-cmd     C!
  FN-CMD-READY  fn-cmd 1 + C!
  fn-cmd 2 dw-write
  fn-cmd 1 dw-read ;

\ ── fn-ready: silent retry until the device acks ────────────────
\ Spins forever if no FujiNet is attached.  Callers that want
\ visible progress should build their own loop around fn-ping.
: fn-ready  ( -- )
  BEGIN fn-ping UNTIL ;

\ ── fn-ready/N: bounded retry, returns -1 on ack, 0 on giveup ───
\ Stack-only solution would need WHILE/REPEAT (which fc.py lacks),
\ so use a small flag variable.  Typical caller passes ~30 retries
\ for a ~1-2 second boot probe.
VARIABLE fn-rdy-ok
: fn-ready/N  ( n -- ok )
  0 fn-rdy-ok !
  BEGIN
    fn-ping IF -1 fn-rdy-ok ! THEN
    1 -
    DUP 0=  fn-rdy-ok @  OR
  UNTIL
  DROP fn-rdy-ok @ ;


\ ── fn-time: read 6 bytes of wall-clock time into buf ────────────
: fn-time  ( buf -- )
  fn-ready
  FN-CMD-TIME fn-cmd C!
  fn-cmd 1 dw-write
  6 dw-read DROP ;

\ ── fn-time/N: bounded version of fn-time ───────────────────────
\ Returns -1 on success (buf populated), 0 on giveup (buf untouched).
: fn-time/N  ( buf n -- ok )
  fn-ready/N IF
    FN-CMD-TIME fn-cmd C!
    fn-cmd 1 dw-write
    6 dw-read DROP
    -1
  ELSE
    DROP 0
  THEN ;
