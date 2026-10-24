\ jukebox.fs - COCO.FM: a laid-back SN76489 radio player (#588, #599-#602)
\
\ Plays every .SNG and .VGM file on the DECB disks in drives 0-3, one
\ after another, radio style, on a 90s MP3-player screen in CG2 (128x64,
\ 4 colours). Songs load into the heap with lib/decb.fs and play with
\ lib/sn76489.fs, or lib/vgm.fs for VGM (#598). A v2 song
\ (src/sound-sn76489/SNG-FORMAT.md) carries a title, composer and length
\ for the display, and a VGM its GD3 tag; a song without shows its file
\ name instead.
\
\ Keys, as in Winamp:
\   Z previous   X play   C pause   V stop   B next
\   left/right   back / forward 5 seconds
\   up/down      volume
\   M            colour set 0 / 1
\   BREAK        quit to BASIC
\
\ Needs a disk controller and the GameMaster (SN76489) at once, which
\ means a Multi-Pak. Both carts answer at $FF40-$FF5F, so the player
\ switches the Multi-Pak's I/O slot ($FF7F bits 1-0) to the disk while
\ loading and to the GameMaster while playing. The ROM slot (bits 5-4)
\ stays on the disk cart so Disk BASIC is there to quit back to. Slots
\ count from 0 here (a Multi-Pak's slot 1 is 0). make run sets up XRoar
\ to match: disk in slot 0, GameMaster in slot 1. On a CoCo Zero $FF7F
\ is open bus and both answer without it.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/vdg.fs
INCLUDE ../../lib/cg-tiny.fs
INCLUDE ../../lib/sn76489.fs
INCLUDE ../../lib/vgm.fs
INCLUDE ../../lib/decb.fs

$00 CONSTANT disk-io    \ $FF7F value: ROM slot 0, I/O slot 0 (disk)
$01 CONSTANT sound-io   \ $FF7F value: ROM slot 0, I/O slot 1 (GameMaster)

$5E00 CONSTANT song-buf \ songs load here, above the program (#618, #598);
$1F00 CONSTANT song-max \ the program must end below it. $5E00-$7CFF, under the stack
$0600 CONSTANT cg-vram  \ CG2 screen, 2048 bytes, in the RG6 reservation


\ ── Colours ────────────────────────────────────────────────────────────
\
\ The screen uses four roles, kept as CG fill bytes:
\   bg  background (and the border on a palette machine)
\   hi  bright: clock, title, lit button, bar fills
\   lo  soft: composer, labels, level bars, unlit buttons
\   fr  frame: title bar, progress and volume tracks
\ On a machine with CoCo 3-style palette registers at $FFB0-$FFBF (a
\ CoCo 3, or a CoCo Zero) pixel value n is role n and the player sets
\ the colours: colour set 0 is a dark Winamp night, colour set 1 a
\ Poolside FM afternoon, and M flips between them (#603). A stock CoCo
\ 1/2 has fixed colours, so the roles move instead: background blue,
\ bright yellow, soft green, frame red (magenta, cyan, buff, orange in
\ colour set 1).

VARIABLE cbg
VARIABLE chi
VARIABLE clo
VARIABLE cfr
: bg  ( -- fill )  cbg @ ;
: hi  ( -- fill )  chi @ ;
: lo  ( -- fill )  clo @ ;
: fr  ( -- fill )  cfr @ ;

: roles  ( bg hi lo fr -- )
  cg-colour cfr !  cg-colour clo !  cg-colour chi !  cg-colour cbg ! ;

$FFB0 CONSTANT pal

\ Palette bytes are GIME RGB: bits 5-0 = R1 G1 B1 R0 G0 B0, each channel
\ 0-3. Registers 0-3 are CG colour set 0, 4-7 colour set 1; a CoCo 3
\ takes the CG border from register 9 (set 0) or 11 (set 1).
DATA[PY themes
def c(r, g, b):
    return (r >> 1) << 5 | (g >> 1) << 4 | (b >> 1) << 3 | (r & 1) << 2 | (g & 1) << 1 | (b & 1)
night = [c(0, 0, 1), c(3, 3, 1), c(0, 3, 1), c(1, 1, 2)]   # navy, warm yellow, LCD green, slate
pool  = [c(2, 3, 3), c(3, 0, 2), c(0, 1, 2), c(3, 2, 3)]   # aqua, hot pink, deep blue, pastel pink
bytes(night + pool)
]DATA

\ What a CoCo Zero shows before anything writes its palette: its stock
\ 6847 colours, as near as GIME RGB gets. Put back on quit when the
\ registers read as all zero (never written, so nothing to restore).
DATA[PY pal-stock
stock = [0x12, 0x36, 0x09, 0x24, 0x3F, 0x1B, 0x2D, 0x34]
stock += [0x00, 0x02, 0x04, 0x26, 0x00, 0x00, 0x00, 0x00]
bytes(stock)
]DATA
DATA[PY pal-save
bytes(16)
]DATA
VARIABLE has-pal

\ Register 12 is a CoCo 3 text colour and unused by a CoCo Zero's 6847:
\ write it and read it back. A stock CoCo 2 has nothing there.
: pal?  ( -- f )
  pal 12 + C@ $3F AND >R
  $15 pal 12 + C!  pal 12 + C@ $3F AND $15 =
  R> pal 12 + C! ;

: colours-on  ( -- )
  16 0 DO  pal I + C@ $3F AND  pal-save I + C!  LOOP
  pal? DUP has-pal !
  IF
    8 0 DO  themes I + C@  pal I + C!  LOOP
    themes C@ pal 9 + C!  themes 4 + C@ pal 11 + C!
    0 1 2 3 roles
  ELSE 2 1 0 3 roles THEN ;

: colours-off  ( -- )
  has-pal @ 0= IF EXIT THEN
  0  16 0 DO pal-save I + C@ OR LOOP
  IF pal-save ELSE pal-stock THEN
  16 0 DO  DUP I + C@  pal I + C!  LOOP  DROP ;

\ The song plays from the field-sync IRQ (#604), which has to be off
\ while the disk is in use.
: >disk   ( -- )  sn-irq-off  disk-io $FF7F C! ;
: >sound  ( -- )  sound-io $FF7F C!  sn-irq-on ;

: ink   ( fill -- )  cg-ink ! ;
: paper ( fill -- )  cg-paper ! ;

\ ── The song list ──────────────────────────────────────────────────────

99 CONSTANT max-songs
\ Each song: its 11-byte directory name and the drive it is on. The
\ table (1188 bytes) sits in the RG6 reservation above the CG2 screen,
\ so it costs the program nothing (#618).
$0E00 CONSTANT names
VARIABLE nsongs
VARIABLE cur            \ the song playing or loaded, 0-based

: name@  ( i -- addr )  12 * names + ;
: drive@  ( i -- d )  name@ 11 + C@ ;

\ Does a directory entry's extension match the three letters a b c?
: ext?  ( entry a b c -- f )
  >R >R >R  DUP 8 + C@ R> =  OVER 9 + C@ R> = AND  SWAP 10 + C@ R> = AND ;

: sng?  ( entry -- f )
  DUP C@ 0= IF DROP 0 EXIT THEN
  DUP $53 $4E $47 ext?  SWAP $56 $47 $4D ext? OR ;

\ Collect the .SNG files on the disk in drive d, if one is turning
\ there (#612).
: scan-drive  ( d -- )
  dk-select  dk-ready? 0= IF EXIT THEN
  72 0 DO
    I dk-entry ?DUP 0= IF EXIT THEN
    nsongs @ max-songs < IF
      DUP sng? IF
        nsongs @ name@ 11 CMOVE  dk-drive @ nsongs @ name@ 11 + C!
        1 nsongs +!
      ELSE DROP THEN
    ELSE DROP THEN
  LOOP ;

\ Every drive, 0 to 3, so the library can span disks.
: scan  ( -- )  0 nsongs !  4 0 DO I scan-drive LOOP  dk-close ;

\ ── Rotation, like a radio station (#623) ──────────────────────────────
\
\ No fixed order. A round plays every song once in a shuffled order, so
\ the popular pieces come round no more often than the rest. The next
\ round keeps the last quarter of this one out of its opening quarter,
\ and no two songs of one set (the same first two letters: INVENT,
\ SINFON, CS1 and CS3...) follow each other where that can be helped.

$1300 CONSTANT order    \ the round's play order, a byte a song, above names
VARIABLE pos            \ where the round has got to, 0-based
VARIABLE nk             \ songs kept out of the start of this round
VARIABLE fk             \ the set a swapped-in song must differ from
VARIABLE fticks         \ frames since start, stirred into the seed

: o@  ( i -- song )  order + C@ ;
: oswap  ( i j -- )  2DUP o@ SWAP o@ ROT order + C! SWAP order + C! ;
: song  ( -- i )  pos @ o@ ;

\ The kernel's rng is a 16-bit LCG: its high byte is the useful part.
: rand  ( n -- 0..n-1 )  rng kvar-seed @ 8 RSHIFT * 8 RSHIFT ;
: mix  ( n -- )  kvar-seed @ XOR kvar-seed ! ;

\ The disk scan takes as long as the drives do; on real hardware that
\ varies, so count how far into a frame it finished.
: stir  ( -- )
  $FF02 C@ DROP  0 BEGIN 1 + $FF03 C@ $80 AND UNTIL  mix ;

\ Fisher-Yates over positions lo to hi-1.
: shuffle  ( lo hi -- )
  OVER - DUP 2 < IF 2DROP EXIT THEN
  1 DO  DUP I +  OVER I 1 + rand +  oswap  LOOP DROP ;

: set@  ( song -- key )  name@ @ ;
: oset  ( i -- key )  o@ set@ ;

\ Swap into place i the first later song whose set is not fk's, without
\ pulling a held-back song into the opening quarter.
: zone  ( i -- end )  nk @ < IF nk @ ELSE nsongs @ THEN ;
: fix  ( i key -- )
  fk !  DUP zone OVER 1 +  2DUP > 0= IF 2DROP DROP EXIT THEN
  DO  I oset fk @ <> IF DUP I oswap DROP EXIT THEN  LOOP DROP ;

\ key: the set of the song just played, so a round's first song does
\ not repeat its set either.
: flow  ( key -- )
  0 oset OVER = IF 0 OVER fix THEN DROP
  nsongs @ 2 < IF EXIT THEN
  nsongs @ 1 DO  I oset I 1 - oset = IF I I 1 - oset fix THEN  LOOP ;

: new-round  ( -- )
  fticks @ mix
  nsongs @ DUP 2 RSHIFT nk !
  0 OVER nk @ - shuffle     \ the songs not played lately open it
  nk @ SWAP shuffle ;       \ and the last ones mix in behind them

: first-round  ( -- )
  nsongs @ 0 DO I I order + C! LOOP
  0 nk !  0 nsongs @ shuffle  0 flow  0 pos ! ;

: advance  ( -- )
  pos @ 1 + DUP nsongs @ < IF pos ! EXIT THEN DROP
  song set@  new-round  flow  0 pos ! ;

: retreat  ( -- )  pos @ 1 - 0 MAX pos ! ;

\ ── Screen layout (byte column, pixel row) ─────────────────────────────
\
\   0-6    title bar: COCO.FM and the station tag
\   8-17   play state icon, big clock, total time, level bars
\   20     song title, scrolling when it is wider than the screen
\   26     composer
\   33-35  progress bar
\   39     track number and volume
\   46-52  transport buttons, key letters under them at 55
\ Rows 61-63 stay empty: XRoar's window and a TV's overscan crop them.

8  CONSTANT y-clock
20 CONSTANT y-title
26 CONSTANT y-comp
33 CONSTANT y-prog
39 CONSTANT y-stat
46 CONSTANT y-btn
17 CONSTANT y-bars      \ bottom row of the level bars

: cg2-on  ( -- )
  cg-vram cg-base !
  2 set-sam-v  cg-vram 9 RSHIFT set-sam-f  $A0 set-pia ;

: cls  ( -- )  cg-vram 2048 bg FILL ;

: title-bar  ( -- )
  0 0 32 7 fr cg-fill
  hi ink  fr paper
  1 1 cg-at  S" COCO.FM" cg-type
  18 1 cg-at S" ALL BACH RADIO" cg-type ;

\ Centred text in a cleared 6-row band.
: centre  ( addr u y -- )
  >R  0 R@ 32 6 bg cg-fill
  32 OVER - 2/ 0 MAX  R> cg-at  cg-type ;

\ ── Play state ─────────────────────────────────────────────────────────

0 CONSTANT stopped
1 CONSTANT playing
2 CONSTANT paused
VARIABLE mode
VARIABLE loaded         \ true when song-buf holds the current song

VARIABLE secs           \ elapsed whole seconds, as last drawn
VARIABLE tsecs          \ song length in seconds, 0 if unknown
VARIABLE dirty          \ redraw: 1 clock, 2 progress, 4 icon and buttons

: dirty!  ( bits -- )  dirty @ OR dirty ! ;
: clean   ( bit -- )   INVERT dirty @ AND dirty ! ;

\ ── Buttons ────────────────────────────────────────────────────────────

VARIABLE flash          \ frames left to light prev/next after a press
VARIABLE flash-btn      \ which: 0 prev, 4 next

\ The mode that lights each button (9: none, prev and next flash).
DATA[PY btn-modes
bytes([9, 1, 2, 0, 9])
]DATA

\ Button n (0 prev, 1 play, 2 pause, 3 stop, 4 next) lit?
: lit?  ( n -- f )
  DUP 0=  OVER 4 = OR IF
    flash @ IF flash-btn @ = ELSE DROP 0 THEN  EXIT
  THEN
  btn-modes + C@  mode @ = ;

: btn-ink  ( n -- )  lit? IF hi ELSE lo THEN ink ;

\ Transport buttons: 16x7 pixels each, wider than tall like Winamp's.
\ A '#' is cut out of the button in the background colour (the icon
\ and the rounded corners); the rest is the button colour. Stored as CG
\ masks, 4 bytes a row.
DATA[PY btn-art
ART = [
 # previous: bar and left-pointing triangle
 ["#..............#",
  ".....##.....#...",
  ".....##...###...",
  ".....##.#####...",
  ".....##...###...",
  ".....##.....#...",
  "#..............#"],
 # play
 ["#..............#",
  ".......#........",
  ".......###......",
  ".......#####....",
  ".......###......",
  ".......#........",
  "#..............#"],
 # pause
 ["#..............#",
  "......##..##....",
  "......##..##....",
  "......##..##....",
  "......##..##....",
  "......##..##....",
  "#..............#"],
 # stop
 ["#..............#",
  "......#####.....",
  "......#####.....",
  "......#####.....",
  "......#####.....",
  "......#####.....",
  "#..............#"],
 # next: right-pointing triangle and bar
 ["#..............#",
  "......#.....##..",
  "......###...##..",
  "......#####.##..",
  "......###...##..",
  "......#.....##..",
  "#..............#"],
]
out = bytearray()
for icon in ART:
    for row in icon:
        for b in range(4):
            out.append(sum(0xC0 >> 2 * i for i in range(4) if row[4 * b + i] == '#'))
bytes(out)
]DATA

\ Button n at byte column 6n+2: icon and corners cut out in the
\ background colour, on bright when lit, soft when not.
: button  ( n -- )
  bg ink  DUP lit? IF hi ELSE lo THEN paper
  DUP 28 * btn-art +  SWAP 6 * 2 + y-btn 32 * + cg-vram +   ( art addr )
  7 0 DO
    4 0 DO  OVER I + C@ cg-mix  OVER I + C!  LOOP
    SWAP 4 + SWAP 32 +
  LOOP 2DROP ;

: buttons  ( -- )  5 0 DO I button LOOP ;

: key-letters  ( -- )
  lo ink  bg paper
  4 55 cg-at   CHAR Z cg-emit   10 55 cg-at  CHAR X cg-emit
  16 55 cg-at  CHAR C cg-emit   22 55 cg-at  CHAR V cg-emit
  28 55 cg-at  CHAR B cg-emit ;

\ The state icon left of the clock.
: state-icon  ( -- )
  lo ink  bg paper  1 y-clock cg-at
  mode @ playing = IF $5C ELSE mode @ paused = IF $5E ELSE $5D THEN THEN
  cg-big ;

\ ── Clock ──────────────────────────────────────────────────────────────

DATA[PY clock-now
bytes(5)
]DATA
DATA[PY clock-new
bytes(5)
]DATA

: digit  ( n -- c )  $30 + ;

\ Write secs as MM:SS into a 5-byte buffer.
: mmss  ( secs buf -- )
  >R  60 /MOD  10 /MOD digit R@ C!  digit R@ 1 + C!
  CHAR : R@ 2 + C!  10 /MOD digit R@ 3 + C!  digit R> 4 + C! ;

\ Redraw only the clock digits that changed.
: clock  ( -- )
  secs @ clock-new mmss
  hi ink  bg paper
  5 0 DO
    clock-new I + C@  clock-now I + C@  <> IF
      I 2 * 4 +  y-clock cg-at
      clock-new I + C@  DUP cg-big  clock-now I + C!
    THEN
  LOOP ;

: clock-reset  ( -- )  clock-now 5 0 FILL ;

\ The song length, small, right of the clock, as M:SS.
: total  ( -- )
  lo ink  bg paper  15 13 cg-at
  tsecs @ IF
    tsecs @ clock-new mmss
    clock-new C@ CHAR 0 = IF clock-new 1 + 4 ELSE clock-new 4 THEN cg-type
  ELSE 15 13 4 6 bg cg-fill THEN ;

\ ── Progress bar ───────────────────────────────────────────────────────

DATA[PY part-masks
bytes([0x00, 0xC0, 0xF0, 0xFC])
]DATA

\ Bar length in pixels, 0-120. Both times are halved until the length
\ is under 256 so the multiply stays inside 16 bits.
: progress-px  ( -- px )
  tsecs @ 0= IF 0 EXIT THEN
  secs @ tsecs @
  BEGIN
    DUP 256 < IF 1 ELSE  1 RSHIFT SWAP 1 RSHIFT SWAP  0 THEN
  UNTIL
  >R 120 * R> /MOD SWAP DROP  120 MIN ;

: progress  ( -- )
  1 y-prog 30 3 fr cg-fill
  progress-px 4 /MOD                               ( rem bytes )
  DUP IF  1 y-prog 2 PICK 3 hi cg-fill  THEN
  1 + y-prog 32 * + cg-vram +                      ( rem addr )
  SWAP DUP IF
    part-masks + C@  DUP hi AND  SWAP INVERT fr AND  OR
    2DUP SWAP C!  2DUP SWAP 32 + C!  SWAP 64 + C!
  ELSE 2DROP THEN ;

\ ── Level bars, one per channel, from the envelope levels ──────────────

DATA[PY bar-h
bytes(4)
]DATA
VARIABLE bc
VARIABLE bh
VARIABLE bo

\ Height 0-10 for channel ch: louder is taller.
: bar-target  ( ch -- h )
  sn-lvl + C@  sn-atten @ +  15 MIN  15 SWAP -  2 * 3 /MOD SWAP DROP ;

\ Grow or shrink one bar to its new height, touching only the rows
\ that change.
: bar  ( ch -- )
  DUP bc !  mode @ playing = IF bar-target ELSE DROP 0 THEN  bh !
  bc @ bar-h + C@ bo !
  bh @ bo @ = IF EXIT THEN
  bc @ 3 * 20 +
  bh @ bo @ > IF  y-bars 1 + bh @ -  bh @ bo @ -  lo
  ELSE            y-bars 1 + bo @ -  bo @ bh @ -  bg  THEN
  >R >R 2 R> R> cg-fill
  bh @ bc @ bar-h + C! ;

\ ── Title, scrolling when it does not fit ──────────────────────────────

VARIABLE tptr
VARIABLE tlen
VARIABLE mq-pos
VARIABLE mq-tick

: mq-len  ( -- n )  tlen @ 4 + ;
: mq-wrap  ( i -- i )  DUP mq-len < 0= IF mq-len - THEN ;
: mq-char  ( i -- c )  DUP tlen @ < IF tptr @ + C@ ELSE DROP $20 THEN ;

: title-draw  ( -- )
  hi ink  bg paper
  tlen @ 32 > IF
    0 y-title cg-at  32 0 DO I mq-char cg-emit LOOP
    0 mq-pos !  0 mq-tick !
  ELSE tptr @ tlen @ y-title centre THEN ;

\ Every 10 frames slide the title one character left: move the six
\ rows over by a byte, then draw the next character at the right edge.
: marquee  ( -- )
  tlen @ 32 > 0= IF EXIT THEN
  1 mq-tick +!  mq-tick @ 10 < IF EXIT THEN  0 mq-tick !
  6 0 DO  y-title I + 32 * cg-vram +  DUP 1 + SWAP 31 CMOVE  LOOP
  mq-pos @ 1 + mq-wrap  DUP mq-pos !  31 + mq-wrap mq-char
  hi ink  bg paper  31 y-title cg-at  cg-emit ;

\ ── Status line: track number and volume ───────────────────────────────

: 2digits  ( n -- )  10 /MOD digit cg-emit digit cg-emit ;

: track  ( -- )
  lo ink  bg paper  1 y-stat cg-at
  S" TRACK" cg-type  $20 cg-emit
  pos @ 1 + 2digits  CHAR / cg-emit  nsongs @ 2digits ;

\ Volume bar: 8 bytes of two steps each, bright on the frame colour.
: vol-byte  ( b -- fill )
  2 *  15 sn-atten @ -  SWAP -
  DUP 1 > IF DROP hi ELSE 1 = IF hi $F0 AND fr $0F AND OR ELSE fr THEN THEN ;

: vol  ( -- )
  lo ink  bg paper  17 y-stat cg-at  S" VOL" cg-type
  8 0 DO  I 21 + y-stat 1 5 I vol-byte cg-fill  LOOP ;

: louder  ( -- )  sn-atten @ 1 - 0 MAX sn-atten !  vol ;
: softer  ( -- )  sn-atten @ 1 + 15 MIN sn-atten !  vol ;

\ ── Loading ────────────────────────────────────────────────────────────

: msg  ( addr u -- )  hi ink bg paper  y-title centre ;

: wait-frames  ( n -- )  0 DO sn-vsync LOOP ;

: disk-error  ( err -- )
  0 y-title 32 6 bg cg-fill  hi ink bg paper
  9 y-title cg-at  S" DISK ERROR" cg-type $20 cg-emit 2digits
  120 wait-frames ;

\ Load song i into song-buf. True when it is ready to play.
: load  ( i -- f )
  S" TUNING IN..." msg  0 y-comp 32 6 bg cg-fill
  >disk
  DUP drive@ dk-select
  name@ dk-fname 11 CMOVE
  dk-open-fname ?DUP IF dk-close >sound disk-error 0 EXIT THEN
  song-buf song-max dk-load-open  dk-close  >sound
  SWAP DROP ?DUP IF disk-error 0 EXIT THEN
  1 ;

\ A song with no title shows its 8-character file name, spaces trimmed.
: trim  ( addr u -- addr u' )
  BEGIN
    DUP 0= IF 1 ELSE
      2DUP + 1 - C@ $20 <> IF 1 ELSE 1 - 0 THEN
    THEN
  UNTIL ;

\ The loaded song is a VGM file (#598), not a .SNG stream.
: vgm-song?  ( -- f )  song-buf vgm? ;

: song-info  ( -- )
  vgm-song? IF song-buf vgm-title ELSE song-buf sng-title THEN
  ?DUP 0= IF DROP cur @ name@ 8 trim THEN  tlen ! tptr !
  title-draw
  lo ink  bg paper
  vgm-song? IF song-buf vgm-author ELSE song-buf sng-composer THEN
  y-comp centre
  vgm-song? IF song-buf vgm-frames ELSE song-buf sng-frames THEN
  1 RSHIFT 30 /MOD SWAP DROP  tsecs !
  total ;

\ ── Transport ──────────────────────────────────────────────────────────

: rewound  ( -- )
  song-buf  vgm-song? IF vgm-start ELSE sn-start THEN  3 dirty! ;

\ Run n frames of the song without playing them.
: skip  ( n -- )  vgm-song? IF vgm-skip ELSE sn-skip THEN ;

\ The IRQ steps the song only while it is playing. Every change to the
\ song's state happens with it held, then this lets it go again.
: release  ( -- )  mode @ playing <> sn-hold ;

: tune  ( i -- )
  DUP 0 < IF DROP nsongs @ 1 - THEN
  DUP nsongs @ < 0= IF DROP 0 THEN
  cur !
  1 sn-hold  sn-silence  stopped mode !  0 loaded !  0 tsecs !
  track  cur @ load IF
    1 loaded !  song-info  rewound  playing mode !
  THEN
  release  clock-reset  7 dirty! ;

: play  ( -- )
  loaded @ 0= IF EXIT THEN
  1 sn-hold  mode @ paused = 0= IF rewound THEN
  playing mode !  release  4 dirty! ;

: pause  ( -- )
  mode @ playing = IF 1 sn-hold sn-silence paused mode ! 4 dirty! EXIT THEN
  mode @ paused = IF playing mode ! release 4 dirty! THEN ;

: stop  ( -- )
  1 sn-hold  sn-silence  stopped mode !  loaded @ IF rewound THEN  4 dirty! ;

: prev  ( -- )  0 flash-btn !  12 flash !  retreat song tune ;
: next  ( -- )  4 flash-btn !  12 flash !  advance song tune ;

\ Seeking runs the stream without playing it. Forward goes on from
\ here; back restarts the song and runs up to the new time.
: fwd  ( -- )
  mode @ stopped = IF EXIT THEN
  1 sn-hold  sn-silence  300 skip  release  3 dirty! ;

: back  ( -- )
  mode @ stopped = IF EXIT THEN
  1 sn-hold  sn-silence  sn-frames 300 - 0 MAX  rewound  skip
  release  3 dirty! ;

\ M swaps colour set 0 and colour set 1: $FF22 bit 3 (#603). With a
\ palette that is night and Poolside; without, the VDG's two sets.
: colours  ( -- )  $FF22 C@ 8 XOR $FF22 C! ;

: quit  ( -- )  sn-silence  colours-off  reset-text  >disk  exit-basic ;

\ ── Keys ───────────────────────────────────────────────────────────────

VARIABLE lastk
VARIABLE held

: act  ( k -- )
  DUP $1C = IF DROP louder EXIT THEN
  DUP $1D = IF DROP softer EXIT THEN
  DUP $1E = IF DROP back EXIT THEN
  DUP $1F = IF DROP fwd EXIT THEN
  DUP CHAR Z = IF DROP prev EXIT THEN
  DUP CHAR X = IF DROP play EXIT THEN
  DUP CHAR C = IF DROP pause EXIT THEN
  DUP CHAR V = IF DROP stop EXIT THEN
  DUP CHAR B = IF DROP next EXIT THEN
  DUP CHAR M = IF DROP colours EXIT THEN
  3 = IF quit THEN ;

\ A new key acts at once. The arrows repeat while held: after 24
\ frames, then every 6.
: keys  ( -- )
  KEY?  DUP lastk @ = IF
    1 held +!
    held @ 24 = IF  18 held !  DUP $1C $20 WITHIN IF act EXIT THEN  THEN
    DROP EXIT
  THEN
  DUP lastk !  0 held !  ?DUP IF  fticks @ mix  act  THEN ;

\ ── Main loop ──────────────────────────────────────────────────────────

\ One heavy redraw per frame at most, so playback keeps time.
: ui  ( -- )
  dirty @ 1 AND IF clock  1 clean EXIT THEN
  dirty @ 2 AND IF progress  2 clean EXIT THEN
  dirty @ 4 AND IF state-icon buttons  4 clean EXIT THEN
  marquee ;

: frame  ( -- )
  1 fticks +!
  mode @ playing = IF sn-over? IF next EXIT THEN THEN
  sn-frames 1 RSHIFT 30 /MOD SWAP DROP
  DUP secs @ = IF DROP ELSE secs !  3 dirty! THEN
  flash @ IF  -1 flash +!  flash @ 0= IF 4 dirty! THEN  THEN
  keys
  4 0 DO I bar LOOP
  ui ;

: main  ( -- )
  cart-audio  sound-io $FF7F C!  sn-silence  1 sn-hold
  colours-on  cg2-on  cls  title-bar  key-letters  vol
  S" SCANNING DISK..." msg
  >disk  scan  stir  >sound
  nsongs @ 0= IF
    S" NO SONGS ON ANY DRIVE" msg
    BEGIN sn-vsync KEY? UNTIL  quit
  THEN
  first-round  song tune
  BEGIN sn-vsync frame 0 UNTIL ;

main
