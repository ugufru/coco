\ sound-tour.fs - one phrase on every sound option we have (#634)
\
\ The opening of the Ode to Joy, played in turn on:
\   1  the 1-bit output ($FF22 bit 1)           lib/sound.fs snd-tone1
\   2  the 6-bit DAC ($FF20), then its effects  lib/sound.fs snd-saw ...
\   3  the SN76489 (GameMaster)                 lib/sn76489.fs
\   4  the CoCo PSG (YM2149)                    lib/ay8910.fs
\   5  the Symphony-12 (four AY-3-8910s)        lib/ay8910.fs
\   6  the Orchestra-90CC (two 8-bit DACs)      lib/orch90.fs
\ Melody, bass and hi-hat where the device has the voices; the
\ Symphony-12 adds chords and plays in stereo, and the Orchestra-90 puts
\ the melody left and the bass right. The tables of pitches and lengths
\ are worked out on the host by tour.py.
\
\ XRoar has the GameMaster and the Orchestra-90 (make run puts both in a
\ Multi-Pak); the CoCo PSG and the Symphony-12 need the CoCo Zero, so
\ make dsk builds a disk for it too.

INCLUDE ../../lib/screen.fs
INCLUDE ../../lib/bye.fs
INCLUDE ../../lib/sound.fs
INCLUDE ../../lib/sn76489.fs
INCLUDE ../../lib/ay8910.fs
INCLUDE ../../lib/wavetable.fs
INCLUDE ../../lib/orch90.fs

3    CONSTANT gap-frames   \ tour.py GAP
64   CONSTANT saw-step     \ tour.py SAW_STEP
4688 CONSTANT psg-ep       \ envelope period: a 0.6 s decay at 2 MHz
2097 CONSTANT sym-ep       \ and at 894886 Hz
$7000 CONSTANT wave        \ 256-byte wavetable for the Orchestra-90

\ ── The phrase, per device (tour.py) ───────────────────────────────────

DATA[PY t-count
import sys; sys.path.insert(0, '.'); import tour
tour.count()
]DATA
DATA[PY t-frames
import sys; sys.path.insert(0, '.'); import tour
tour.frame_table()
]DATA
DATA[PY t-1bit
import sys; sys.path.insert(0, '.'); import tour
tour.one_bit()
]DATA
DATA[PY t-saw
import sys; sys.path.insert(0, '.'); import tour
tour.dac_saw()
]DATA
DATA[PY t-sn
import sys; sys.path.insert(0, '.'); import tour
tour.sn()
]DATA
DATA[PY t-psg
import sys; sys.path.insert(0, '.'); import tour
tour.psg()
]DATA
DATA[PY t-sym
import sys; sys.path.insert(0, '.'); import tour
tour.sym()
]DATA
DATA[PY t-orch
import sys; sys.path.insert(0, '.'); import tour
tour.orch()
]DATA

: notes  ( -- n )  t-count C@ ;

\ ── Shared ─────────────────────────────────────────────────────────────

VARIABLE stop        \ BREAK was pressed: leave the tour
VARIABLE ft          \ frames since the section began
VARIABLE ml          \ melody, bass and hi-hat (left, right) as
VARIABLE bl          \ attenuations: 0 loudest, 15 silent
VARIABLE hl
VARIABLE hr

: brk?  ( -- f )  key? 3 = DUP IF 1 stop ! THEN ;

: gap  ( -- )  gap-frames 0 DO vsync LOOP ;

\ Step an attenuation one quieter, no further than limit.
: fade  ( var limit -- )
  OVER @ OVER < IF DROP 1 SWAP +! ELSE 2DROP THEN ;

\ Hi-hats on the eighths: the beat on the left, the off-beat on the
\ right (the same thing in mono), each dying away in a few frames.
: hats  ( -- )
  ft @ 24 /MOD DROP
  DUP 0= IF 5 hl ! THEN  12 = IF 8 hr ! THEN
  hl @ 3 + 15 MIN hl !  hr @ 3 + 15 MIN hr ! ;

\ One frame of a note n frames long, at frame f: the melody falls
\ quiet for the gap, melody and bass decay, the hats tick.
: tick  ( f n -- )
  gap-frames - = IF 15 ml ! THEN
  ft @ 3 AND 0= IF ml 9 fade THEN
  ft @ 7 AND 0= IF bl 10 fade THEN
  hats  1 ft +! ;

: start  ( -- )  0 ft !  15 ml !  15 bl !  15 hl !  15 hr ! ;

: mono-hat  ( -- att )  hl @ hr @ MIN ;

\ ── The screen ─────────────────────────────────────────────────────────

: menu  ( -- )
  cls-black
  0 0 AT  ." SOUND TOUR: ODE TO JOY"
  0 2 AT  ." 1  1-BIT OUTPUT"
  0 3 AT  ." 2  6-BIT DAC"
  0 4 AT  ." 3  SN76489: GAMEMASTER"
  0 5 AT  ." 4  AY: COCO PSG"
  0 6 AT  ." 5  AY: SYMPHONY-12, STEREO"
  0 7 AT  ." 6  ORCHESTRA-90CC, STEREO"
  0 9 AT  ." A  ALL OF THEM IN TURN"
  0 10 AT ." BREAK  STOP / QUIT" ;

: now  ( addr u -- )
  0 13 AT  32 0 DO $20 EMIT LOOP  0 13 AT  TYPE ;

\ A cartridge that did not answer: say so long enough to read.
: missing  ( addr u -- )  now  90 0 DO vsync LOOP ;

\ ── 1: the 1-bit output ────────────────────────────────────────────────

: one-bit  ( -- )
  S" 1-BIT: $FF22 BIT 1" now  snd-init
  notes 0 DO
    brk? IF EXIT THEN
    I 4 * t-1bit +  DUP @ SWAP 2 + @  snd-tone1  gap
  LOOP ;

\ ── 2: the 6-bit DAC ───────────────────────────────────────────────────

: dac  ( -- )
  S" 6-BIT DAC: SAWTOOTH" now  snd-init
  notes 0 DO
    brk? IF EXIT THEN
    saw-step  I 4 * t-saw +  DUP @ SWAP 2 + @  snd-saw  gap
  LOOP
  S" 6-BIT DAC: EFFECTS" now
  30 0 DO vsync LOOP  snd-zap  snd-chirp  snd-boom ;

\ ── 3: the SN76489 ─────────────────────────────────────────────────────

: sn-note  ( i -- )
  4 * t-sn +  DUP @ 0 sn-tone
  2 + @ ?DUP IF 1 sn-tone  3 bl ! THEN  2 ml ! ;

: sn-levels  ( -- )
  ml @ 0 sn-vol  bl @ 1 sn-vol  15 2 sn-vol  mono-hat 3 sn-vol ;

: sn76489  ( -- )
  S" SN76489: GAMEMASTER" now
  cart-audio sn-silence  4 sn-noise  start
  notes 0 DO
    brk? IF sn-silence EXIT THEN
    I sn-note
    I t-frames + C@  DUP 0 DO  vsync  I OVER tick  sn-levels  LOOP DROP
  LOOP  sn-silence ;

\ ── 4: the CoCo PSG ────────────────────────────────────────────────────
\ Tone A melody on the hardware envelope (restarted every note), tone B
\ bass, channel C noise for the hats.

: melody-level  ( -- level )  ml @ 15 = IF 0 ELSE 16 THEN ;

: psg-note  ( i -- )
  4 * t-psg +  DUP @ 0 ay-tone
  2 + @ ?DUP IF 1 ay-tone  3 bl ! THEN
  psg-ep 0 ay-env  2 ml ! ;

: psg-levels  ( -- )
  melody-level 0 ay-vol  15 bl @ - 1 ay-vol  15 mono-hat - 2 ay-vol ;

: psg  ( -- )
  S" AY: COCO PSG, YM2149" now
  cart-audio ay-init  ay-psg
  ay-found? 0= IF S" NO COCO PSG FOUND" missing EXIT THEN
  $1C ay-mix  1 ay-noise  start
  notes 0 DO
    brk? IF ay-silence EXIT THEN
    I psg-note
    I t-frames + C@  DUP 0 DO  vsync  I OVER tick  psg-levels  LOOP DROP
  LOOP  ay-silence ;

\ ── 5: the Symphony-12 ─────────────────────────────────────────────────
\ Left: chip 0 melody, bass and the beat's hats; chip 1 the chord.
\ Right: chip 2 the chord an octave up; chip 3 the melody an octave up,
\ the bass and the off-beat's hats.

: sym-note  ( i -- )
  10 * t-sym + >R
  0 ay-chip  R@ @ 0 ay-tone
  3 ay-chip  R@ @ 2/ 0 ay-tone
  R@ 2 + @ ?DUP IF $09 ay-chips 1 ay-tone  3 bl ! THEN
  R@ 4 + @ ?DUP IF
    1 ay-chip  0 ay-tone  R@ 6 + @ 1 ay-tone  R@ 8 + @ 2 ay-tone
    2 ay-chip  R@ 4 + @ 2/ 0 ay-tone  R@ 6 + @ 2/ 1 ay-tone
               R@ 8 + @ 2/ 2 ay-tone
  THEN
  R> DROP
  $09 ay-chips  sym-ep 0 ay-env  2 ml ! ;

\ The chords follow the bass's decay. Each side of the Symphony-12 is
\ two chips mixed down, so they play as loud as the bass to keep up.
: chord-level  ( -- level )  15 bl @ - ;

: sym-levels  ( -- )
  $09 ay-chips  melody-level 0 ay-vol  15 bl @ - 1 ay-vol
  0 ay-chip  15 hl @ - 2 ay-vol
  3 ay-chip  15 hr @ - 2 ay-vol
  $06 ay-chips  chord-level  DUP 0 ay-vol  DUP 1 ay-vol  2 ay-vol ;

: sym12  ( -- )
  S" AY: SYMPHONY-12, 4 CHIPS" now
  cart-audio ay-init  $0F ay-chips
  ay-found? 0= IF S" NO SYMPHONY-12 FOUND" missing EXIT THEN
  $09 ay-chips  $1C ay-mix  1 ay-noise
  $06 ay-chips  $38 ay-mix
  start
  notes 0 DO
    brk? IF $0F ay-chips ay-silence EXIT THEN
    I sym-note
    I t-frames + C@  DUP 0 DO  vsync  I OVER tick  sym-levels  LOOP DROP
  LOOP  $0F ay-chips ay-silence ;

\ ── 6: the Orchestra-90CC ──────────────────────────────────────────────

: orch90  ( -- )
  S" ORCHESTRA-90CC: L MELODY, R BASS" now
  cart-audio  wave gen-tri  wave orch-wave !  orch-silence
  notes 0 DO
    brk? IF orch-silence EXIT THEN
    I 8 * t-orch + >R
    R@ @  R@ 2 + @  R@ 4 + @  orch-play
    0     R@ 2 + @  R@ 6 + @  orch-play
    R> DROP
  LOOP  orch-silence ;

\ ── Main ───────────────────────────────────────────────────────────────

: all  ( -- )
  one-bit  stop @ IF EXIT THEN  dac     stop @ IF EXIT THEN
  sn76489  stop @ IF EXIT THEN  psg     stop @ IF EXIT THEN
  sym12    stop @ IF EXIT THEN  orch90 ;

: play  ( k -- )
  0 stop !
  DUP CHAR 1 = IF one-bit THEN
  DUP CHAR 2 = IF dac THEN
  DUP CHAR 3 = IF sn76489 THEN
  DUP CHAR 4 = IF psg THEN
  DUP CHAR 5 = IF sym12 THEN
  DUP CHAR 6 = IF orch90 THEN
  CHAR A = IF all THEN
  S" PICK ANOTHER" now ;

\ Wait for every key to come up, then for a fresh press.
VARIABLE k
: fresh-key  ( -- c )
  BEGIN vsync key? 0= UNTIL
  BEGIN vsync key? DUP k ! UNTIL
  k @ ;

: main  ( -- )
  menu  S" PICK ONE" now
  BEGIN
    fresh-key  DUP 3 = IF DROP sn-silence exit-basic THEN
    play
  0 UNTIL ;

main
