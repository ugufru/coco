\ decb.fs - Read files from a DECB (RS-DOS) floppy, no Disk BASIC needed
\
\ Provides: dk-open, dk-open-fname, dk-getc, dk-load, dk-load-open,
\           dk-close, dk-entry, dk-sector, dk-name,
\           dk-drive, dk-err, dk-buf, dk-fname
\
\ Requires: kernel primitives only. IRQs must be masked (the kernel does
\           this at START). CoCo 1/2 only: the NMI vector is the RAM
\           trampoline at $0109 (a CoCo 3 uses $FEFD; not handled yet).
\
\ Footprint: ~1.1 KB code + 335 bytes of buffers (sector, FAT, name).
\
\ Talks to the WD17xx on a standard disk controller cart directly:
\   $FF40       drive latch: drive select (1, 2, 4, $40 for drives 0-3),
\               motor $08, precomp $10, double density + NMI enable $20,
\               HALT enable $80
\   $FF48       command (write) / status (read)
\   $FF49-$FF4B track, sector, data
\ Disk BASIC's workspace ($0600-$0DFF) is never touched.
\
\ Sector transfers use the CoCo's HALT trick: with $80 in the latch the
\ CPU is halted until the controller has the next byte (DRQ), so a plain
\ load/store loop keeps pace with the disk. The end of the sector raises
\ INTRQ, which the controller turns into an NMI, and our NMI handler
\ steers the CPU out of the loop. That loop and the handler are the only
\ assembly; everything above the sector level is Forth.
\
\ The disk: 35 tracks of 18 sectors of 256 bytes. Track 17 holds the FAT
\ (sector 2: one byte per granule, 68 granules, $FF free, $00-$43 next
\ granule, $C1-$C9 last granule using 1-9 sectors) and the directory
\ (sectors 3-11, eight 32-byte entries each: name 8, ext 3, type, ascii
\ flag, first granule, bytes used in the last sector). A granule is half
\ a track, 9 sectors; granules 34 and up skip track 17.
\
\ Errors are small numbers, 0 = fine:
\   1 file not found   3 I/O error       4 bad FAT chain
\   9 file too big     10 bad filename   11 bad drive      12 end of file
\
\ Usage:
\   S" SONG.SNG" dk-open IF ( error ) THEN
\   dk-getc ( c, or -1 at the end or on an error: see dk-err ) ...
\   dk-close
\ or, all at once into memory:
\   $5A00 $1E00 S" SONG.SNG" dk-load ( len err )  dk-close
\
\ Reference for the controller sequence and the directory rules (no code
\ copied): ~/github/DECB_Disk_File_Library, and Disk BASIC's DSKCON.

\ ── Buffers and state ──────────────────────────────────────────────────

\ dk-buf -- the 256-byte sector buffer
DATA[PY dk-buf
bytes(256)
]DATA
DATA[PY _dk-fat
bytes(68)
]DATA
\ dk-fname -- the 11-byte file name, 8+3 and space padded, as stored
\ in a directory entry (dk-name fills it; dk-open-fname uses it)
DATA[PY dk-fname
bytes(11)
]DATA
DATA[PY _dk-dmask
bytes([1, 2, 4, 0x40])
]DATA

\ dk-drive -- drive number 0-3 (set by dk-name from a :N suffix)
VARIABLE dk-drive
\ dk-err -- error number from the last dk-getc that returned -1
VARIABLE dk-err
VARIABLE _dk-done     \ NMI handler target while a transfer runs, else 0
VARIABLE _dk-nmi0     \ saved $0109 (opcode, low byte)
VARIABLE _dk-nmi1     \ saved $010A-$010B (address)
VARIABLE _dk-ready    \ NMI installed and heads restored
VARIABLE _dk-spun     \ motor already running
VARIABLE _dk-b        \ dk-sector arguments, kept for retries
VARIABLE _dk-t
VARIABLE _dk-s
VARIABLE _dk-np       \ dk-name: next name byte, its limit, state, error
VARIABLE _dk-nmax
VARIABLE _dk-nst
VARIABLE _dk-nerr
VARIABLE _dk-gran     \ open file: current granule
VARIABLE _dk-sec      \   sectors of it read so far
VARIABLE _dk-nsec     \   sectors it holds
VARIABLE _dk-last     \   true when it is the file's last granule
VARIABLE _dk-lastlen  \   bytes used in the file's last sector
VARIABLE _dk-pos      \   next byte in dk-buf
VARIABLE _dk-len      \   bytes valid in dk-buf
VARIABLE _dk-dest     \ dk-load destination and running total
VARIABLE _dk-total
VARIABLE _dk-max
VARIABLE _dk-e

\ ── Assembly: NMI handler and the sector transfer ──────────────────────

\ Point the $0109 NMI trampoline at our handler, saving what was there.
\ The handler returns straight away unless a transfer is running, so a
\ seek or restore finishing (which also raises INTRQ) is harmless.
CODE _dk-nmi-on  \ ( -- )
        LDA     $0109
        STA     FVAR__dk_nmi0+1
        LDD     $010A
        STD     FVAR__dk_nmi1
        LEAY    @nmi,PCR
        STY     $010A
        LDA     #$7E            ; JMP extended
        STA     $0109
        ;NEXT
@nmi    LDD     FVAR__dk_done
        BEQ     @rti
        STD     10,S            ; replace the stacked PC: leave the loop
        CLR     FVAR__dk_done
        CLR     FVAR__dk_done+1
@rti    RTI
;CODE

CODE _dk-nmi-off  \ ( -- )
        LDA     FVAR__dk_nmi0+1
        STA     $0109
        LDD     FVAR__dk_nmi1
        STD     $010A
        ;NEXT
;CODE

\ Read one sector into buf. The head is already on the track and the
\ motor running; latch is the drive latch value for this track.
\ Leaves the status bits that matter ($7C mask): 0 = good, $10 record
\ not found, $08 CRC error, $04 lost data; $80 = no data came (timeout).
CODE _dk-read  \ ( buf sec latch -- status )
        PSHS    X
        PSHS    CC
        LDA     3,U
        STA     $FF4A           ; sector register
        LDX     4,U             ; destination
        LDA     $FF48           ; clear any stale INTRQ
        LEAY    @done,PCR
        STY     FVAR__dk_done
        LDA     1,U
        ORA     #$80            ; the latch with HALT enabled
        LDY     #0
        ORCC    #$50
        LDB     #$80
        STB     $FF48           ; READ SECTOR
        EXG     A,A
        EXG     A,A
        LDB     #2
@wait   BITB    $FF48           ; poll for the first DRQ, with a timeout,
        BNE     @go             ; so a missing disk cannot halt us forever
        LEAY    -1,Y
        BNE     @wait
        CLR     FVAR__dk_done
        CLR     FVAR__dk_done+1
        LDB     #$D0
        STB     $FF48           ; force interrupt
        EXG     A,A
        EXG     A,A
        LDA     $FF48
        LDA     #$80
        BRA     @out
@go     LDB     $FF4B           ; HALT holds the CPU here until DRQ
        STB     ,X+
        STA     $FF40           ; re-arm HALT for the next byte
        BRA     @go
@done   LDA     $FF48           ; NMI lands here: sector finished
        ANDA    #$7C
@out    LDB     1,U
        STB     $FF40           ; latch without HALT
        PULS    CC
        PULS    X
        LEAU    4,U
        STA     1,U
        CLR     ,U
        ;NEXT
;CODE

\ ── Controller, in Forth ───────────────────────────────────────────────

: _dk-frames  ( n -- )  0 DO vsync LOOP ;

\ Latch value for a track on the current drive: motor, double density
\ and NMI enable, plus write precompensation from track 22 in.
: _dk-latch  ( trk -- b )
  21 > IF $10 ELSE 0 THEN  $28 OR
  dk-drive @ _dk-dmask + C@ OR ;

\ Select the drive and run the motor for this track, giving it about a
\ second to spin up the first time.
: _dk-motor  ( trk -- )
  _dk-latch $FF40 C!
  _dk-spun @ 0= IF 60 _dk-frames  1 _dk-spun ! THEN ;

\ Wait until the controller is not busy; leave its status.
: _dk-wait  ( -- st )
  BEGIN $FF48 C@ DUP 1 AND IF DROP 0 ELSE 1 THEN UNTIL ;

\ Head to track 0. Leaves 0, or $10 if the controller could not find it.
: _dk-restore  ( -- st )
  0 _dk-motor  $03 $FF48 C!  _dk-wait  5 _dk-frames  $10 AND ;

\ Step to a track (with verify). Leaves 0, or seek/CRC error bits.
: _dk-seek  ( trk -- st )
  DUP $FF49 C@ = IF DROP 0 EXIT THEN
  $FF4B C!  $17 $FF48 C!  _dk-wait  5 _dk-frames  $18 AND ;

: _dk-try  ( buf trk sec -- st )
  OVER _dk-motor
  OVER _dk-seek ?DUP IF >R 2DROP DROP R> EXIT THEN
  SWAP _dk-latch _dk-read ;

: _dk-start  ( -- )
  _dk-ready @ IF EXIT THEN
  _dk-nmi-on  0 _dk-done !  0 _dk-spun !
  _dk-restore DROP  1 _dk-ready ! ;

\ dk-sector -- read track/sector (1-18) of dk-drive into buf; 0 or 3
: dk-sector  ( buf trk sec -- err )
  _dk-start
  _dk-s ! _dk-t ! _dk-b !
  5 0 DO
    _dk-b @ _dk-t @ _dk-s @ _dk-try  0= IF 0 EXIT THEN
    _dk-restore DROP
  LOOP
  3 ;

\ dk-close -- stop the motor and give the NMI vector back
: dk-close  ( -- )
  0 $FF40 C!  0 _dk-spun !
  _dk-ready @ IF _dk-nmi-off  0 _dk-ready ! THEN ;

\ ── File names ─────────────────────────────────────────────────────────

: _dk-upper  ( c -- c )  DUP $61 $7B WITHIN IF $20 - THEN ;

: _dk-namec  ( c -- )
  _dk-nst @ IF
    $30 -  DUP 0 4 WITHIN 0= IF 11 _dk-nerr ! THEN  dk-drive !  EXIT
  THEN
  DUP $2E = IF DROP  8 _dk-np !  11 _dk-nmax !  EXIT THEN
  DUP $3A = IF DROP  1 _dk-nst !  EXIT THEN
  _dk-np @ _dk-nmax @ < 0= IF DROP  10 _dk-nerr !  EXIT THEN
  _dk-upper  dk-fname _dk-np @ + C!  1 _dk-np +! ;

\ dk-name -- parse NAME.EXT:D into the 8+3 directory form; 0 or error
: dk-name  ( addr u -- err )
  dk-fname 11 $20 FILL
  0 dk-drive !  0 _dk-np !  8 _dk-nmax !  0 _dk-nst !  0 _dk-nerr !
  DUP IF 0 DO DUP I + C@ _dk-namec LOOP ELSE DROP THEN DROP
  dk-fname C@ $20 = IF 10 ELSE _dk-nerr @ THEN ;

\ ── Directory and FAT ──────────────────────────────────────────────────

: _dk-match  ( entry -- f )
  11 0 DO
    DUP I + C@  dk-fname I + C@  <> IF DROP 0 EXIT THEN
  LOOP DROP 1 ;

\ Look through the directory sector in dk-buf: the matching entry, -1
\ at the end of the directory, or 0 to keep looking.
: _dk-find-in  ( -- entry | -1 | 0 )
  8 0 DO
    dk-buf I 5 LSHIFT +
    DUP C@ $FF = IF DROP -1 EXIT THEN
    DUP C@ IF DUP _dk-match IF EXIT THEN THEN
    DROP
  LOOP 0 ;

\ The entry for dk-fname, still sitting in dk-buf; 0 and an error if not.
: _dk-find  ( -- entry err )
  12 3 DO
    dk-buf 17 I dk-sector ?DUP IF 0 SWAP EXIT THEN
    _dk-find-in ?DUP IF
      DUP -1 = IF DROP 0 1 ELSE 0 THEN  EXIT
    THEN
  LOOP 0 1 ;

\ Size up the current granule from the FAT.
: _dk-gran-setup  ( -- err )
  0 _dk-sec !
  _dk-gran @ 67 > IF 4 EXIT THEN
  _dk-fat _dk-gran @ + C@
  DUP $C0 < IF
    67 > IF 4 EXIT THEN  9 _dk-nsec !  0 _dk-last !  0 EXIT
  THEN
  $3F AND  DUP 1 10 WITHIN 0= IF DROP 4 EXIT THEN
  _dk-nsec !  1 _dk-last !  0 ;

\ Granule to its track and first sector.
: _dk-gts  ( g -- trk sec )
  DUP 2/  OVER 33 > IF 1 + THEN
  SWAP 1 AND IF 10 ELSE 1 THEN ;

\ dk-entry -- read directory entry 0-71 into dk-buf; its address, or 0
\ past the end of the directory or on an error. A deleted entry is
\ returned too: its first byte is 0.
: dk-entry  ( n -- addr | 0 )
  DUP 71 > IF DROP 0 EXIT THEN
  dk-buf 17  2 PICK 3 RSHIFT 3 +  dk-sector IF DROP 0 EXIT THEN
  7 AND 5 LSHIFT dk-buf +
  DUP C@ $FF = IF DROP 0 THEN ;

\ dk-open-fname -- open the file named in dk-fname (8+3, space padded)
: dk-open-fname  ( -- err )
  dk-buf 17 2 dk-sector ?DUP IF EXIT THEN
  dk-buf _dk-fat 68 CMOVE
  _dk-find ?DUP IF SWAP DROP EXIT THEN
  DUP 14 + @  DUP 0= IF DROP 256 THEN
  DUP 256 > IF 2DROP 4 EXIT THEN  _dk-lastlen !
  13 + C@ _dk-gran !
  0 _dk-pos !  0 _dk-len !
  _dk-gran-setup ;

\ dk-open -- open a file for dk-getc / dk-load-open; 0 or an error
: dk-open  ( addr u -- err )
  dk-name ?DUP IF EXIT THEN  dk-open-fname ;

\ Bring the file's next sector into dk-buf; 0, 12 at the end, or error.
: _dk-fill  ( -- err )
  _dk-sec @ _dk-nsec @ = IF
    _dk-last @ IF 12 EXIT THEN
    _dk-fat _dk-gran @ + C@ _dk-gran !
    _dk-gran-setup ?DUP IF EXIT THEN
  THEN
  dk-buf  _dk-gran @ _dk-gts _dk-sec @ +  dk-sector ?DUP IF EXIT THEN
  1 _dk-sec +!  0 _dk-pos !
  256
  _dk-last @ IF _dk-sec @ _dk-nsec @ = IF DROP _dk-lastlen @ THEN THEN
  _dk-len !  0 ;

\ dk-getc -- next byte of the open file, or -1 (reason in dk-err)
: dk-getc  ( -- c | -1 )
  _dk-pos @ _dk-len @ < 0= IF
    _dk-fill ?DUP IF dk-err ! -1 EXIT THEN
  THEN
  dk-buf _dk-pos @ + C@  1 _dk-pos +! ;

\ dk-load-open -- read the rest of the open file to addr, at most max
\ bytes; its length and 0, or the length so far and an error (9 when the
\ file is bigger than max: nothing past addr+max is written)
: dk-load-open  ( addr max -- len err )
  _dk-max !  _dk-dest !  0 _dk-total !
  BEGIN
    _dk-fill DUP _dk-e !
    0= IF
      _dk-total @ _dk-len @ +  _dk-max @ > IF
        9 _dk-e !
      ELSE
        dk-buf _dk-dest @ _dk-len @ CMOVE
        _dk-len @ DUP _dk-dest +! _dk-total +!
      THEN
    THEN
    _dk-e @
  UNTIL
  _dk-total @  _dk-e @ DUP 12 = IF DROP 0 THEN ;

\ dk-load -- read a whole file to addr, at most max bytes; as dk-load-open
: dk-load  ( addr max c-addr u -- len err )
  dk-open ?DUP IF >R 2DROP 0 R> EXIT THEN
  dk-load-open ;
