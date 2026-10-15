\ jukebox.fs - pick an SN76489 song off a DECB disk and play it (#588)
\
\ Lists every .SNG file on the disk in drive 0, loads the one you pick
\ into the heap with lib/decb.fs, and plays it with lib/sn76489.fs. A
\ .SNG file is a raw song stream, as written by
\ src/sound-sn76489/sn76489.py (no DECB header).
\
\ Needs a disk controller and the GameMaster (SN76489) at once, which
\ means a Multi-Pak. Both carts answer at $FF40-$FF5F, so the player
\ switches the Multi-Pak's I/O slot ($FF7F bits 1-0) to the disk while
\ loading and to the GameMaster while playing. The ROM slot (bits 5-4)
\ stays on the disk cart so Disk BASIC is there to quit back to. Slots
\ count from 0 here (a Multi-Pak's slot 1 is 0). make run sets up XRoar
\ to match: disk in slot 0, GameMaster in slot 1.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/print.fs
INCLUDE ../../lib/sn76489.fs
INCLUDE ../../lib/decb.fs

$00 CONSTANT disk-io    \ $FF7F value: ROM slot 0, I/O slot 0 (disk)
$01 CONSTANT sound-io   \ $FF7F value: ROM slot 0, I/O slot 1 (GameMaster)

$5A00 CONSTANT song-buf \ songs load here: free heap up to TRIG_BASE
$1E00 CONSTANT song-max \ $5A00-$77FF

12 CONSTANT max-songs
DATA[PY names
bytes(12 * 11)
]DATA
VARIABLE nsongs
VARIABLE k

: >disk   ( -- )  disk-io $FF7F C! ;
: >sound  ( -- )  sound-io $FF7F C! ;

: name@  ( i -- addr )  11 * names + ;

: sng?  ( entry -- f )
  DUP C@ 0= IF DROP 0 EXIT THEN
  DUP 8 + C@ $53 =  OVER 9 + C@ $4E = AND  SWAP 10 + C@ $47 = AND ;

\ Collect the names of the .SNG files on the disk.
: scan  ( -- )
  0 nsongs !
  72 0 DO
    I dk-entry ?DUP 0= IF EXIT THEN
    nsongs @ max-songs < IF
      DUP sng? IF  nsongs @ name@ 11 CMOVE  1 nsongs +!
      ELSE DROP THEN
    ELSE DROP THEN
  LOOP ;

\ Wait for every key to come up, then for a fresh press.
: fresh-key  ( -- c )
  BEGIN vsync key? 0= UNTIL
  BEGIN vsync key? DUP k ! UNTIL
  k @ ;

: upper  ( c -- c )  DUP $61 $7B WITHIN IF $20 - THEN ;

: title  ( -- )
  cls-black
  2 1 AT ." SN76489 JUKEBOX"
  2 2 AT ." ---------------" ;

: menu  ( -- )
  title
  nsongs @ 0 DO
    2 I 4 + AT  I $41 + EMIT ."   " I name@ 8 TYPE
  LOOP
  2 14 AT ." LETTER PLAY   BREAK QUIT" ;

\ A song number, or -1 for BREAK.
: choose  ( -- i | -1 )
  BEGIN
    fresh-key  DUP 3 = IF DROP -1 EXIT THEN
    upper $41 -  DUP 0 nsongs @ WITHIN IF EXIT THEN  DROP
  0 UNTIL ;

: status  ( -- )  2 14 AT ."                          " 2 14 AT ;

: disk-error  ( err -- )
  status ." DISK ERROR " .  fresh-key DROP ;

\ Load song i into song-buf. True when it is ready to play.
: load  ( i -- f )
  >disk  status ." LOADING..."
  name@ dk-fname 11 CMOVE
  dk-open-fname ?DUP IF dk-close disk-error 0 EXIT THEN
  song-buf song-max dk-load-open  dk-close
  SWAP DROP ?DUP IF disk-error 0 EXIT THEN
  1 ;

\ Play the loaded song until BREAK, offering a replay after each pass.
: play  ( i -- )
  title  2 5 AT ." NOW PLAYING"  2 7 AT name@ 8 TYPE
  >sound
  BEGIN
    status ." BREAK MENU"
    song-buf sn-play IF 0 ELSE
      status ." ANY KEY AGAIN  BREAK MENU"
      fresh-key 3 <>
    THEN
  0= UNTIL
  sn-silence ;

: main  ( -- )
  >sound  sn-silence  cart-audio
  >disk  title  2 4 AT ." READING DISK..."
  scan  dk-close
  nsongs @ 0= IF
    2 6 AT ." NO .SNG FILES ON DRIVE 0"  fresh-key DROP  exit-basic
  THEN
  BEGIN
    menu  choose
    DUP 0 < IF DROP  >disk  exit-basic THEN
    DUP load IF play ELSE DROP THEN
  0 UNTIL ;

main
