\ clock.fs — FujiNet-synced analog + digital clock
\
\ Future-retro single-screen clock for the CoCo, RG6 mode (256x192,
\ 3-color artifact palette).  Composition: large centered analog
\ face, white outline, 12 white tick marks (longer at 12/3/6/9), no
\ numerals; thick white hour hand, thin white minute hand, thin
\ red second hand sweeping at vsync rate; small filled white center
\ pin.  Below the face: YYYY.MM.DD on one line, HH:MM:SS on the next;
\ small "FUJI" badge in the corner that flashes red on each successful
\ FN sync.
\
\ Time source: query fn-time once at boot, then advance locally via
\ a 60Hz vsync counter.  Resync every 5 minutes to absorb the 59.94Hz
\ drift.

INCLUDE ../../forth/lib/vdg.fs
INCLUDE ../../forth/lib/screen.fs
INCLUDE ../../forth/lib/rg-pixel.fs
INCLUDE ../../forth/lib/datawrite.fs
INCLUDE ../../forth/lib/font-art.fs
INCLUDE ../../forth/lib/trig.fs
INCLUDE ../../forth/lib/fujinet.fs


\ ── Forth helpers ────────────────────────────────────────────────────

: /  ( a b -- a/b )  /MOD SWAP DROP ;


\ ── Layout (artifact pixels: 128 wide x 192 tall) ────────────────────

64 CONSTANT CX
72 CONSTANT CY
56 CONSTANT R-FACE
50 CONSTANT R-TICKO        \ outer end of all ticks
46 CONSTANT R-TICKMI       \ inner end of major ticks (longer)
48 CONSTANT R-TICKMN       \ inner end of minor ticks
24 CONSTANT R-HR
40 CONSTANT R-MN
46 CONSTANT R-SC

3 CONSTANT C-WHITE
2 CONSTANT C-RED


\ ── Two RG6 VRAM buffers for double-buffering ───────────────────────
\ A at $0600 (rg-init's default), B at $A000 (clear of font at $9000
\ and kernel at $E000+).  SAM-F flips between them at vsync.

$0600 CONSTANT VRAM-A
$A000 CONSTANT VRAM-B

\ ── Beam path buffers — one set per VRAM ────────────────────────────
\ Each hand stroke is ~50 px × 3 bytes = 150 bytes; reserve 256.

$4000 CONSTANT BUF-A-HR
$4100 CONSTANT BUF-A-MN
$4200 CONSTANT BUF-A-SC
$4300 CONSTANT BUF-B-HR
$4400 CONSTANT BUF-B-MN
$4500 CONSTANT BUF-B-SC


\ ── Fast SAM-F write (~110 cycles, fits inside the ~680-cycle vblank) ─
\ The library set-sam-f loops 7 times in ITC Forth (~2000 cycles) which
\ blows past vblank and the display tears mid-scan as bits update.
\ Unrolled 6809 version writes all 7 bit-toggles in <120 cycles.

CODE set-sam-f-fast
        ;;; ( offset -- )
        PSHS    X
        LDB     1,U                 ; B = offset low byte
        LEAU    2,U
        LDA     #$FF
        LDX     #$FFC6              ; SAM-F bit-clear base; +1 = bit-set

        LSRB
        BCC     @c0
        STA     1,X
        BRA     @n0
@c0     STA     ,X
@n0     LSRB
        BCC     @c1
        STA     3,X
        BRA     @n1
@c1     STA     2,X
@n1     LSRB
        BCC     @c2
        STA     5,X
        BRA     @n2
@c2     STA     4,X
@n2     LSRB
        BCC     @c3
        STA     7,X
        BRA     @n3
@c3     STA     6,X
@n3     LSRB
        BCC     @c4
        STA     9,X
        BRA     @n4
@c4     STA     8,X
@n4     LSRB
        BCC     @c5
        STA     11,X
        BRA     @n5
@c5     STA     10,X
@n5     LSRB
        BCC     @c6
        STA     13,X
        BRA     @n6
@c6     STA     12,X
@n6     PULS    X
        ;NEXT
;CODE


\ ── "Back" / "front" buffer state ──────────────────────────────────
\ Each side holds the current vram base, beam-buffer addresses, and
\ saved beam-path lengths.  flip-buffers swaps all of them.

VARIABLE bk-vram    VARIABLE fr-vram
VARIABLE bk-hr-buf  VARIABLE fr-hr-buf
VARIABLE bk-mn-buf  VARIABLE fr-mn-buf
VARIABLE bk-sc-buf  VARIABLE fr-sc-buf
VARIABLE bk-hr-len  VARIABLE fr-hr-len
VARIABLE bk-mn-len  VARIABLE fr-mn-len
VARIABLE bk-sc-len  VARIABLE fr-sc-len

\ Swap fr/bk pointer pairs in pure 6809 — ~200 cycles vs ~1000 for the
\ Forth-level vswap version.  Saves ~5% CPU at 60 Hz frame rate.
CODE flip-state
        PSHS    X
        LDD     FVAR_bk_vram
        LDX     FVAR_fr_vram
        STX     FVAR_bk_vram
        STD     FVAR_fr_vram

        LDD     FVAR_bk_hr_buf
        LDX     FVAR_fr_hr_buf
        STX     FVAR_bk_hr_buf
        STD     FVAR_fr_hr_buf

        LDD     FVAR_bk_mn_buf
        LDX     FVAR_fr_mn_buf
        STX     FVAR_bk_mn_buf
        STD     FVAR_fr_mn_buf

        LDD     FVAR_bk_sc_buf
        LDX     FVAR_fr_sc_buf
        STX     FVAR_bk_sc_buf
        STD     FVAR_fr_sc_buf

        LDD     FVAR_bk_hr_len
        LDX     FVAR_fr_hr_len
        STX     FVAR_bk_hr_len
        STD     FVAR_fr_hr_len

        LDD     FVAR_bk_mn_len
        LDX     FVAR_fr_mn_len
        STX     FVAR_bk_mn_len
        STD     FVAR_fr_mn_len

        LDD     FVAR_bk_sc_len
        LDX     FVAR_fr_sc_len
        STX     FVAR_bk_sc_len
        STD     FVAR_fr_sc_len

        LDD     FVAR_bk_sc_ltx
        LDX     FVAR_fr_sc_ltx
        STX     FVAR_bk_sc_ltx
        STD     FVAR_fr_sc_ltx

        LDD     FVAR_bk_sc_lty
        LDX     FVAR_fr_sc_lty
        STX     FVAR_bk_sc_lty
        STD     FVAR_fr_sc_lty

        LDD     FVAR_bk_mn_ltx
        LDX     FVAR_fr_mn_ltx
        STX     FVAR_bk_mn_ltx
        STD     FVAR_fr_mn_ltx

        LDD     FVAR_bk_mn_lty
        LDX     FVAR_fr_mn_lty
        STX     FVAR_bk_mn_lty
        STD     FVAR_fr_mn_lty

        LDD     FVAR_bk_hr_ltx
        LDX     FVAR_fr_hr_ltx
        STX     FVAR_bk_hr_ltx
        STD     FVAR_fr_hr_ltx

        LDD     FVAR_bk_hr_lty
        LDX     FVAR_fr_hr_lty
        STX     FVAR_bk_hr_lty
        STD     FVAR_fr_hr_lty

        PULS    X
        ;NEXT
;CODE


\ ── Time state ──────────────────────────────────────────────────────

VARIABLE clk-yr
VARIABLE clk-mo
VARIABLE clk-dy
VARIABLE clk-hr
VARIABLE clk-mn
VARIABLE clk-sc
VARIABLE vs-cnt
VARIABLE sync-flash         \ seconds remaining to display "RTC SYNC"

2 CONSTANT SYNC-FLASH-COUNT


\ ── Adaptive vsync calibration ─────────────────────────────────────
\ The clock-loop misses some hardware vsyncs during the per-second
\ heavy work (tick-hands + render-* + flip).  Our 60-vsync rollover
\ undercounts and the local clock runs ~4 sec slow per minute.
\ Compensation: count actual vsync calls between FN syncs, divide
\ by FN's real-time delta, and use an EWMA-smoothed result as the
\ rollover threshold (vps = vsyncs per local-second).

VARIABLE vps                \ calibrated vsyncs per local-second
VARIABLE vps-cnt            \ vsync calls accumulated since last sync
VARIABLE last-fn-min        \ FN minute at previous sync
VARIABLE last-fn-sec        \ FN second at previous sync
VARIABLE synced-once        \ 0 = never synced, -1 = have a baseline
VARIABLE fn-enabled         \ -1 = FN sync allowed; 0 = skip (e.g. fake-time dev)

\ vps and vs-cnt are kept in 16ths-of-a-vsync (4 fractional bits).
\ This drops the per-second rounding error from ~0.5 vsync to ~0.03,
\ which is invisible drift even between hourly resyncs.
960 CONSTANT INITIAL-VPS    \ 60 vsyncs/sec * 16

\ scaled-div: compute (a * 16) / b without intermediate 16-bit overflow.
\ Splits a into quotient + remainder w.r.t. b first, then scales each.
\ For our use (a < 4000, b in [30, 120]), all products stay safely
\ inside signed 16-bit range.
: scaled-div  ( a b -- scaled )
  >R                              \ R: b
  R@ /MOD                         \ ( rem quot )
  16 *                            \ ( rem quot*16 )
  SWAP                            \ ( quot*16 rem )
  16 *                            \ ( quot*16 rem*16 )
  R> /                            \ ( quot*16 rem*16/b )
  + ;


\ ── 6-byte buffer for fn-time response ──────────────────────────────

CODE time-buf
        LEAY    @buf,PCR
        STY     ,--U
        ;NEXT
@buf    RMB     6
;CODE


\ ── Days in month (no leap-year handling — close enough) ───────────

$8500 CONSTANT MO-DAYS

: init-mo-days  ( -- )
  MO-DAYS tp !
  0 tb  31 tb  28 tb  31 tb  30 tb  31 tb  30 tb
  31 tb  31 tb  30 tb  31 tb  30 tb  31 tb ;

: days-in-mo  ( mo -- n )  MO-DAYS + C@ ;


\ ── Convert clock degrees (0=12, clockwise) to trig degrees ────────
\ trig: 0=right, 90=up, 180=left, 270=down

: clk>trig  ( clock-deg -- trig-deg )
  90 SWAP - 360 + 360 /MOD DROP ;


\ ── Endpoint helpers (write into temp variables) ───────────────────

VARIABLE tang
VARIABLE tx1  VARIABLE ty1
VARIABLE tx2  VARIABLE ty2

\ Artifact pixels are 2x wider than tall on the CRT, so halve the X
\ displacement.  We use 11/20 (= 0.55) instead of /2 (= 0.5) so the
\ resulting oval is 10% wider than tall, matching the screen's aspect
\ ratio (#454).  Hand endpoint tables use the same 0.55 factor.
\ Kernel * and / are unsigned, so split sign out before the multiply.
: x-scale  ( n -- n*11/20 )
  DUP 0 < IF NEGATE 11 * 20 / NEGATE
  ELSE      11 * 20 /
  THEN ;

: ep1  ( angle len -- )
  2DUP angle-dx x-scale  CX + tx1 !
  angle-dy CY + ty1 ! ;

: ep2  ( angle len -- )
  2DUP angle-dx x-scale  CX + tx2 !
  angle-dy CY + ty2 ! ;

: stroke  ( color -- )
  >R  tx1 @ ty1 @ tx2 @ ty2 @ R> rg-line ;


\ ── Static face (one-shot) ─────────────────────────────────────────

VARIABLE face-a

: face-circle  ( -- )
  0 face-a !
  24 0 DO
    face-a @ R-FACE ep1
    face-a @ 15 + DUP face-a !
                  R-FACE ep2
    C-WHITE stroke
  LOOP ;

VARIABLE tick-ri

\ Quarter ticks (12, 3, 6, 9) draw as longer line segments.  The other
\ eight tick positions render as 2x2 pip squares at the outer radius —
\ at 128x192 artifact resolution, diagonal 2-pixel lines never look
\ right at non-quarter angles, so a clean dot reads better (#454).
: face-ticks  ( -- )
  12 0 DO
    I 30 * clk>trig tang !
    I 3 /MOD DROP 0 = IF
      R-TICKMI tick-ri !
      tang @ tick-ri @ ep1
      tang @ R-TICKO  ep2
      C-WHITE stroke
    ELSE
      tang @ R-TICKO  ep2
      tx2 @     ty2 @     C-WHITE rg-pset
      tx2 @ 1 + ty2 @     C-WHITE rg-pset
      tx2 @     ty2 @ 1 + C-WHITE rg-pset
      tx2 @ 1 + ty2 @ 1 + C-WHITE rg-pset
    THEN
  LOOP ;

: face-pin  ( -- )
  CX     CY     C-WHITE rg-pset
  CX 1 - CY     C-WHITE rg-pset
  CX 1 + CY     C-WHITE rg-pset
  CX     CY 1 - C-WHITE rg-pset
  CX     CY 1 + C-WHITE rg-pset ;

: draw-face  ( -- )
  face-circle face-ticks face-pin ;


\ ── Hand rendering via beam trace/draw/restore ──────────────────────
\ Each hand is a single line from center to (angle, length).
\ trace-draw saves background pixels into buf, then paints the line.
\ restore puts the saved background back.  Per-hand stored count
\ tells restore how many pixels to put back.

VARIABLE sc-len
VARIABLE mn-len
VARIABLE hr-len

\ ( angle len buf -- count )
\ Trace a center-to-endpoint line, return the path length.
VARIABLE _hb
: trace-line  ( clk-angle len buf -- count )
  _hb !
  SWAP clk>trig SWAP                   \ convert clk-deg to trig-deg for ep2
  ep2                                  \ endpoint into tx2,ty2
  CX CY tx2 @ ty2 @ _hb @ beam-trace ;

: paint-line  ( buf count color -- )
  >R                                   \ R: color   stack: ( buf count )
  0 SWAP R>                            \ ( buf 0 count color )
  beam-draw-slice ;

: erase-line  ( buf count -- )
  ?DUP IF                              \ ( buf count )
    0 SWAP                             \ ( buf 0 count )
    beam-restore-slice
  ELSE
    DROP                               \ ( ) — no prior trace
  THEN ;


\ Smooth-sweep second-hand angle includes sub-second progress so the
\ hand updates 60 Hz instead of jumping each tick.  vs-cnt and vps are
\ both scaled by 16, so their ratio (vs-cnt/vps) is unitless seconds.
\ Sub-second angle = 6 * vs-cnt / vps; the (vs-cnt * 6) intermediate
\ stays under 16-bit signed range (max ~5760).
: sc-angle  ( -- deg )
  clk-sc @ 6 *
  vs-cnt @ 6 *  vps @ /  + ;

\ Smooth-sweep minute/hour angles (return clock-degrees 0..359).
\ mn advances 1° every ~10 real-sec; hr every ~2 real-min.  All three
\ hands share the same time base so motion stays proportional.
: mn-angle  ( -- deg )  clk-mn @ 6 *  sc-angle 60 / + ;

: hr-angle  ( -- deg )
  clk-hr @ 12 /MOD DROP 30 *
  mn-angle 12 / + ;


\ Precomputed sec-hand endpoint tables.  Indexed by clock-angle 0..359.
\ Each entry holds the absolute endpoint pixel coord for the sec hand at
\ that angle, given fixed radius R-SC=46 and center (CX=64, CY=72).
\ Eliminates ep2/angle-dx/angle-dy calls from the per-frame path
\ (saves ~5,700cy/frame, which is what lets the loop fit in one vblank).

CODE sec-tx-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     64,64,65,65,66,66,67,67,68,68,68,69,69,70,70,71
        FCB     71,71,72,72,73,73,73,74,74,75,75,75,76,76,77,77
        FCB     77,78,78,79,79,79,80,80,80,81,81,81,82,82,82,83
        FCB     83,83,83,84,84,84,84,85,85,85,85,86,86,86,86,87
        FCB     87,87,87,87,87,88,88,88,88,88,88,88,89,89,89,89
        FCB     89,89,89,89,89,89,89,89,89,89,89,89,89,89,89,89
        FCB     89,89,89,89,89,89,89,89,89,88,88,88,88,88,88,88
        FCB     87,87,87,87,87,87,86,86,86,86,85,85,85,85,84,84
        FCB     84,84,83,83,83,83,82,82,82,81,81,81,80,80,80,79
        FCB     79,79,78,78,77,77,77,76,76,75,75,75,74,74,73,73
        FCB     73,72,72,71,71,71,70,70,69,69,68,68,68,67,67,66
        FCB     66,65,65,64,64,64,63,63,62,62,61,61,60,60,60,59
        FCB     59,58,58,57,57,57,56,56,55,55,55,54,54,53,53,53
        FCB     52,52,51,51,51,50,50,49,49,49,48,48,48,47,47,47
        FCB     46,46,46,45,45,45,45,44,44,44,44,43,43,43,43,42
        FCB     42,42,42,41,41,41,41,41,41,40,40,40,40,40,40,40
        FCB     39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39
        FCB     39,39,39,39,39,39,39,39,39,39,39,39,39,40,40,40
        FCB     40,40,40,40,41,41,41,41,41,41,42,42,42,42,43,43
        FCB     43,43,44,44,44,44,45,45,45,45,46,46,46,47,47,47
        FCB     48,48,48,49,49,49,50,50,51,51,51,52,52,53,53,53
        FCB     54,54,55,55,55,56,56,57,57,57,58,58,59,59,60,60
        FCB     60,61,61,62,62,63,63,64
;CODE

CODE sec-ty-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     26,26,26,26,26,26,26,26,26,27,27,27,27,27,27,28
        FCB     28,28,28,29,29,29,29,30,30,30,31,31,31,32,32,33
        FCB     33,33,34,34,35,35,36,36,37,37,38,38,39,39,40,41
        FCB     41,42,42,43,44,44,45,46,46,47,48,48,49,50,50,51
        FCB     52,53,53,54,55,56,56,57,58,59,59,60,61,62,62,63
        FCB     64,65,66,66,67,68,69,70,70,71,72,73,74,74,75,76
        FCB     77,78,78,79,80,81,82,82,83,84,85,85,86,87,88,88
        FCB     89,90,91,91,92,93,94,94,95,96,96,97,98,98,99,100
        FCB     100,101,102,102,103,103,104,105,105,106,106,107,107,108,108,109
        FCB     109,110,110,111,111,111,112,112,113,113,113,114,114,114,115,115
        FCB     115,115,116,116,116,116,117,117,117,117,117,117,118,118,118,118
        FCB     118,118,118,118,118,118,118,118,118,118,118,118,118,117,117,117
        FCB     117,117,117,116,116,116,116,115,115,115,115,114,114,114,113,113
        FCB     113,112,112,111,111,111,110,110,109,109,108,108,107,107,106,106
        FCB     105,105,104,103,103,102,102,101,100,100,99,98,98,97,96,96
        FCB     95,94,94,93,92,91,91,90,89,88,88,87,86,85,85,84
        FCB     83,82,82,81,80,79,78,78,77,76,75,74,74,73,72,71
        FCB     70,70,69,68,67,66,66,65,64,63,62,62,61,60,59,59
        FCB     58,57,56,56,55,54,53,53,52,51,50,50,49,48,48,47
        FCB     46,46,45,44,44,43,42,42,41,41,40,39,39,38,38,37
        FCB     37,36,36,35,35,34,34,33,33,33,32,32,31,31,31,30
        FCB     30,30,29,29,29,29,28,28,28,28,27,27,27,27,27,27
        FCB     26,26,26,26,26,26,26,26
;CODE


\ Precomputed hr-hand endpoint tables.  R-HR=24.
CODE hr-tx-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     64,64,64,65,65,65,65,66,66,66,66,67,67,67,67,67
        FCB     68,68,68,68,69,69,69,69,69,70,70,70,70,70,71,71
        FCB     71,71,71,72,72,72,72,72,72,73,73,73,73,73,73,74
        FCB     74,74,74,74,74,75,75,75,75,75,75,75,75,76,76,76
        FCB     76,76,76,76,76,76,76,76,77,77,77,77,77,77,77,77
        FCB     77,77,77,77,77,77,77,77,77,77,77,77,77,77,77,77
        FCB     77,77,77,77,77,77,77,77,77,77,77,77,77,76,76,76
        FCB     76,76,76,76,76,76,76,76,75,75,75,75,75,75,75,75
        FCB     74,74,74,74,74,74,73,73,73,73,73,73,72,72,72,72
        FCB     72,72,71,71,71,71,71,70,70,70,70,70,69,69,69,69
        FCB     69,68,68,68,68,67,67,67,67,67,66,66,66,66,65,65
        FCB     65,65,64,64,64,64,64,63,63,63,63,62,62,62,62,61
        FCB     61,61,61,61,60,60,60,60,59,59,59,59,59,58,58,58
        FCB     58,58,57,57,57,57,57,56,56,56,56,56,56,55,55,55
        FCB     55,55,55,54,54,54,54,54,54,53,53,53,53,53,53,53
        FCB     53,52,52,52,52,52,52,52,52,52,52,52,51,51,51,51
        FCB     51,51,51,51,51,51,51,51,51,51,51,51,51,51,51,51
        FCB     51,51,51,51,51,51,51,51,51,51,51,51,51,51,51,51
        FCB     51,52,52,52,52,52,52,52,52,52,52,52,53,53,53,53
        FCB     53,53,53,53,54,54,54,54,54,54,55,55,55,55,55,55
        FCB     56,56,56,56,56,56,57,57,57,57,57,58,58,58,58,58
        FCB     59,59,59,59,59,60,60,60,60,61,61,61,61,61,62,62
        FCB     62,62,63,63,63,63,64,64
;CODE

CODE hr-ty-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     48,48,48,48,48,48,48,48,48,48,48,48,49,49,49,49
        FCB     49,49,49,49,49,50,50,50,50,50,50,51,51,51,51,51
        FCB     52,52,52,52,53,53,53,53,54,54,54,54,55,55,55,56
        FCB     56,56,57,57,57,58,58,58,59,59,59,60,60,60,61,61
        FCB     61,62,62,63,63,63,64,64,65,65,65,66,66,67,67,67
        FCB     68,68,69,69,69,70,70,71,71,72,72,72,73,73,74,74
        FCB     75,75,75,76,76,77,77,77,78,78,79,79,79,80,80,81
        FCB     81,81,82,82,83,83,83,84,84,84,85,85,85,86,86,86
        FCB     87,87,87,88,88,88,89,89,89,90,90,90,90,91,91,91
        FCB     91,92,92,92,92,93,93,93,93,93,94,94,94,94,94,94
        FCB     95,95,95,95,95,95,95,95,95,96,96,96,96,96,96,96
        FCB     96,96,96,96,96,96,96,96,96,96,96,96,96,96,96,96
        FCB     95,95,95,95,95,95,95,95,95,94,94,94,94,94,94,93
        FCB     93,93,93,93,92,92,92,92,91,91,91,91,90,90,90,90
        FCB     89,89,89,88,88,88,87,87,87,86,86,86,85,85,85,84
        FCB     84,84,83,83,83,82,82,81,81,81,80,80,79,79,79,78
        FCB     78,77,77,77,76,76,75,75,75,74,74,73,73,72,72,72
        FCB     71,71,70,70,69,69,69,68,68,67,67,67,66,66,65,65
        FCB     65,64,64,63,63,63,62,62,61,61,61,60,60,60,59,59
        FCB     59,58,58,58,57,57,57,56,56,56,55,55,55,54,54,54
        FCB     54,53,53,53,53,52,52,52,52,51,51,51,51,51,50,50
        FCB     50,50,50,50,49,49,49,49,49,49,49,49,49,48,48,48
        FCB     48,48,48,48,48,48,48,48
;CODE

\ Precomputed mn-hand endpoint tables.  R-MN=40.
CODE mn-tx-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     64,64,65,65,66,66,66,67,67,67,68,68,69,69,69,70
        FCB     70,70,71,71,72,72,72,73,73,73,74,74,74,75,75,75
        FCB     76,76,76,77,77,77,78,78,78,78,79,79,79,80,80,80
        FCB     80,81,81,81,81,82,82,82,82,82,83,83,83,83,83,84
        FCB     84,84,84,84,84,85,85,85,85,85,85,85,85,85,86,86
        FCB     86,86,86,86,86,86,86,86,86,86,86,86,86,86,86,86
        FCB     86,86,86,86,86,86,86,85,85,85,85,85,85,85,85,85
        FCB     84,84,84,84,84,84,83,83,83,83,83,82,82,82,82,82
        FCB     81,81,81,81,80,80,80,80,79,79,79,78,78,78,78,77
        FCB     77,77,76,76,76,75,75,75,74,74,74,73,73,73,72,72
        FCB     72,71,71,70,70,70,69,69,69,68,68,67,67,67,66,66
        FCB     66,65,65,64,64,64,63,63,62,62,62,61,61,61,60,60
        FCB     59,59,59,58,58,58,57,57,56,56,56,55,55,55,54,54
        FCB     54,53,53,53,52,52,52,51,51,51,50,50,50,50,49,49
        FCB     49,48,48,48,48,47,47,47,47,46,46,46,46,46,45,45
        FCB     45,45,45,44,44,44,44,44,44,43,43,43,43,43,43,43
        FCB     43,43,42,42,42,42,42,42,42,42,42,42,42,42,42,42
        FCB     42,42,42,42,42,42,42,42,42,42,42,43,43,43,43,43
        FCB     43,43,43,43,44,44,44,44,44,44,45,45,45,45,45,46
        FCB     46,46,46,46,47,47,47,47,48,48,48,48,49,49,49,50
        FCB     50,50,50,51,51,51,52,52,52,53,53,53,54,54,54,55
        FCB     55,55,56,56,56,57,57,58,58,58,59,59,59,60,60,61
        FCB     61,61,62,62,62,63,63,64
;CODE

CODE mn-ty-tab
        LEAY    @tab,PCR
        STY     ,--U
        ;NEXT
@tab    FCB     32,32,32,32,32,32,32,32,32,32,33,33,33,33,33,33
        FCB     34,34,34,34,34,35,35,35,35,36,36,36,37,37,37,38
        FCB     38,38,39,39,40,40,40,41,41,42,42,43,43,44,44,45
        FCB     45,46,46,47,47,48,48,49,50,50,51,51,52,53,53,54
        FCB     54,55,56,56,57,58,58,59,60,60,61,62,62,63,64,64
        FCB     65,66,66,67,68,69,69,70,71,71,72,73,73,74,75,75
        FCB     76,77,78,78,79,80,80,81,82,82,83,84,84,85,86,86
        FCB     87,88,88,89,90,90,91,91,92,93,93,94,94,95,96,96
        FCB     97,97,98,98,99,99,100,100,101,101,102,102,103,103,104,104
        FCB     104,105,105,106,106,106,107,107,107,108,108,108,109,109,109,109
        FCB     110,110,110,110,110,111,111,111,111,111,111,112,112,112,112,112
        FCB     112,112,112,112,112,112,112,112,112,112,112,112,112,112,111,111
        FCB     111,111,111,111,110,110,110,110,110,109,109,109,109,108,108,108
        FCB     107,107,107,106,106,106,105,105,104,104,104,103,103,102,102,101
        FCB     101,100,100,99,99,98,98,97,97,96,96,95,94,94,93,93
        FCB     92,91,91,90,90,89,88,88,87,86,86,85,84,84,83,82
        FCB     82,81,80,80,79,78,78,77,76,75,75,74,73,73,72,71
        FCB     71,70,69,69,68,67,66,66,65,64,64,63,62,62,61,60
        FCB     60,59,58,58,57,56,56,55,54,54,53,53,52,51,51,50
        FCB     50,49,48,48,47,47,46,46,45,45,44,44,43,43,42,42
        FCB     41,41,40,40,40,39,39,38,38,38,37,37,37,36,36,36
        FCB     35,35,35,35,34,34,34,34,34,33,33,33,33,33,33,32
        FCB     32,32,32,32,32,32,32,32
;CODE


\ Last rendered endpoint per back buffer for each hand.  Lets us skip
\ the beam trace/draw/restore pipeline on frames where the integer pixel
\ endpoint hasn't moved.  Cascade redraw in z-order when any hand moves.
VARIABLE bk-sc-ltx   VARIABLE bk-sc-lty
VARIABLE fr-sc-ltx   VARIABLE fr-sc-lty
VARIABLE bk-mn-ltx   VARIABLE bk-mn-lty
VARIABLE fr-mn-ltx   VARIABLE fr-mn-lty
VARIABLE bk-hr-ltx   VARIABLE bk-hr-lty
VARIABLE fr-hr-ltx   VARIABLE fr-hr-lty

\ Scratch for this frame's computed endpoints.
VARIABLE new-sc-tx   VARIABLE new-sc-ty
VARIABLE new-mn-tx   VARIABLE new-mn-ty
VARIABLE new-hr-tx   VARIABLE new-hr-ty
VARIABLE movelvl             \ 0=none 1=sc 2=mn 3=hr (lowest moved hand)

\ Unified smooth-sweep redraw for all three hands.  Every frame:
\   • compute current endpoints via table lookup (no trig math)
\   • determine lowest hand whose integer endpoint changed
\   • erase top-down to that hand, redraw bottom-up from that hand
\ Nothing-moved frames skip the beam pipeline entirely.  Each back
\ buffer tracks its own lasts, so after flip the new back catches up.
: redraw-hands  ( -- )
  sc-angle DUP sec-tx-tab + C@ new-sc-tx !  sec-ty-tab + C@ new-sc-ty !
  mn-angle DUP mn-tx-tab  + C@ new-mn-tx !  mn-ty-tab  + C@ new-mn-ty !
  hr-angle DUP hr-tx-tab  + C@ new-hr-tx !  hr-ty-tab  + C@ new-hr-ty !

  0 movelvl !
  new-hr-tx @ bk-hr-ltx @ <>  new-hr-ty @ bk-hr-lty @ <>  OR IF
    3 movelvl !
  ELSE new-mn-tx @ bk-mn-ltx @ <>  new-mn-ty @ bk-mn-lty @ <>  OR IF
    2 movelvl !
  ELSE new-sc-tx @ bk-sc-ltx @ <>  new-sc-ty @ bk-sc-lty @ <>  OR IF
    1 movelvl !
  THEN THEN THEN

  movelvl @ 0= 0= IF
    \ Erase top-down to the lowest moved hand.
    bk-sc-buf @ bk-sc-len @ erase-line
    movelvl @ 1 > IF bk-mn-buf @ bk-mn-len @ erase-line THEN
    movelvl @ 2 > IF bk-hr-buf @ bk-hr-len @ erase-line THEN

    \ Redraw bottom-up from the lowest moved hand.
    movelvl @ 2 > IF
      CX CY new-hr-tx @ new-hr-ty @ bk-hr-buf @ beam-trace bk-hr-len !
      bk-hr-buf @ bk-hr-len @ C-WHITE paint-line
      new-hr-tx @ bk-hr-ltx !  new-hr-ty @ bk-hr-lty !
    THEN
    movelvl @ 1 > IF
      CX CY new-mn-tx @ new-mn-ty @ bk-mn-buf @ beam-trace bk-mn-len !
      bk-mn-buf @ bk-mn-len @ C-WHITE paint-line
      new-mn-tx @ bk-mn-ltx !  new-mn-ty @ bk-mn-lty !
    THEN
    CX CY new-sc-tx @ new-sc-ty @ bk-sc-buf @ beam-trace bk-sc-len !
    bk-sc-buf @ bk-sc-len @ C-RED paint-line
    new-sc-tx @ bk-sc-ltx !  new-sc-ty @ bk-sc-lty !
  THEN ;


\ Full-force repaint used by paint-back-full during init.  Draws all
\ three hands unconditionally via the endpoint tables and refreshes the
\ lasts so redraw-hands enters the loop with a correct baseline.
: tick-hands  ( -- )
  sc-angle DUP sec-tx-tab + C@ new-sc-tx !  sec-ty-tab + C@ new-sc-ty !
  mn-angle DUP mn-tx-tab  + C@ new-mn-tx !  mn-ty-tab  + C@ new-mn-ty !
  hr-angle DUP hr-tx-tab  + C@ new-hr-tx !  hr-ty-tab  + C@ new-hr-ty !

  bk-sc-buf @ bk-sc-len @ erase-line
  bk-mn-buf @ bk-mn-len @ erase-line
  bk-hr-buf @ bk-hr-len @ erase-line

  CX CY new-hr-tx @ new-hr-ty @ bk-hr-buf @ beam-trace bk-hr-len !
  bk-hr-buf @ bk-hr-len @ C-WHITE paint-line
  new-hr-tx @ bk-hr-ltx !  new-hr-ty @ bk-hr-lty !

  CX CY new-mn-tx @ new-mn-ty @ bk-mn-buf @ beam-trace bk-mn-len !
  bk-mn-buf @ bk-mn-len @ C-WHITE paint-line
  new-mn-tx @ bk-mn-ltx !  new-mn-ty @ bk-mn-lty !

  CX CY new-sc-tx @ new-sc-ty @ bk-sc-buf @ beam-trace bk-sc-len !
  bk-sc-buf @ bk-sc-len @ C-RED paint-line
  new-sc-tx @ bk-sc-ltx !  new-sc-ty @ bk-sc-lty ! ;


\ ── Digital readout via rg-char ────────────────────────────────────

VARIABLE _dc  VARIABLE _dr
: 2dig  ( n col row -- )
  _dr ! _dc !
  10 /MOD                              \ ( ones tens )
  CHAR 0 + _dc @     _dr @ rg-char
  CHAR 0 + _dc @ 1 + _dr @ rg-char ;

\ Year display: "19xx" or "20xx" depending on yr-1900
: render-year  ( col row -- )
  _dr ! _dc !
  clk-yr @ 100 < IF
    CHAR 1  _dc @     _dr @ rg-char
    CHAR 9  _dc @ 1 + _dr @ rg-char
    clk-yr @
  ELSE
    CHAR 2  _dc @     _dr @ rg-char
    CHAR 0  _dc @ 1 + _dr @ rg-char
    clk-yr @ 100 -
  THEN
  _dc @ 2 +  _dr @  2dig ;

: render-date  ( -- )
  11 18 render-year                    \ "20YY" cols 11..14
  CHAR .  15 18 rg-char
  clk-mo @ 16 18 2dig                  \ MM cols 16..17
  CHAR .  18 18 rg-char
  clk-dy @ 19 18 2dig ;                \ DD cols 19..20

\ Split into the three natural update cadences so the loop only repaints
\ what actually changed (see issue #448).  render-date touches ~8 chars,
\ render-hm ~5, render-ss ~2 — total cost scales accordingly.

: render-hm  ( -- )
  clk-hr @ 12 21 2dig                  \ HH cols 12..13
  CHAR :  14 21 rg-char
  clk-mn @ 15 21 2dig                  \ MM cols 15..16
  CHAR :  17 21 rg-char ;

: render-ss  ( -- )
  clk-sc @ 18 21 2dig ;                \ SS cols 18..19

\ Full repaint, used at boot via paint-back-full.
: render-datetime  ( -- )
  render-date render-hm render-ss ;


\ ── "RTC SYNC" flash in the upper-right corner ──────────────────────
\ Drawn for SYNC-FLASH-COUNT consecutive ticks after each sync, then
\ overwritten with spaces.  Position: char row 1, columns 23..30.

: render-sync-flash  ( -- )
  sync-flash @ 0 > IF
    CHAR R 23 1 rg-char
    CHAR T 24 1 rg-char
    CHAR C 25 1 rg-char
    32     26 1 rg-char
    CHAR S 27 1 rg-char
    CHAR Y 28 1 rg-char
    CHAR N 29 1 rg-char
    CHAR C 30 1 rg-char
  ELSE
    32 23 1 rg-char  32 24 1 rg-char  32 25 1 rg-char  32 26 1 rg-char
    32 27 1 rg-char  32 28 1 rg-char  32 29 1 rg-char  32 30 1 rg-char
  THEN ;


\ ── Time advancement (called every vsync) ──────────────────────────
\ Note: nested IFs instead of EXIT — fc.py miscompiles EXIT inside IF.

: tick-second  ( -- )
  1 clk-sc +!
  clk-sc @ 60 = IF
    0 clk-sc !
    1 clk-mn +!
    clk-mn @ 60 = IF
      0 clk-mn !
      1 clk-hr +!
      clk-hr @ 24 = IF
        0 clk-hr !
        1 clk-dy +!
        clk-dy @ clk-mo @ days-in-mo > IF
          1 clk-dy !
          1 clk-mo +!
          clk-mo @ 12 > IF
            1 clk-mo !
            1 clk-yr +!
          THEN
        THEN
      THEN
    THEN
  THEN ;

: tick-frame  ( -- )
  16 vs-cnt +!                   \ scaled by 16 (matches vps scale)
  vs-cnt @ vps @ < IF
    \ not yet at threshold
  ELSE
    0 vs-cnt !
    tick-second
  THEN ;

\ Counting wrapper around vsync — accumulates vps-cnt for calibration.
: vsync+  ( -- )  vsync 1 vps-cnt +! ;

\ Real seconds elapsed since the previous sync, derived from FN's
\ minute/second values (assumes < 1 hour between syncs).
: real-elapsed  ( -- sec )
  clk-mn @ last-fn-min @ - 60 *
  clk-sc @ last-fn-sec @ - + ;

\ Recalibrate vps using a 50/50 EWMA: new_vps = (old_vps + observed) / 2.
\ Skip if elapsed time is implausible (clock skew, day wrap, etc).
: calibrate-vps  ( -- )
  real-elapsed
  DUP 29 > IF                  \ delta >= 30
    DUP 121 < IF               \ delta <= 120
      vps-cnt @ SWAP scaled-div   \ observed, scaled by 16
      vps @ + 2/  vps !           \ EWMA: (old + observed) / 2
    ELSE DROP
    THEN
  ELSE DROP
  THEN ;


\ ── FN sync ────────────────────────────────────────────────────────

: sync-from-fn  ( -- )
  -1 fn-enabled !            \ if we got here, FN is reachable
  time-buf fn-time
  time-buf       C@ clk-yr !
  time-buf 1 + C@ clk-mo !
  time-buf 2 + C@ clk-dy !
  time-buf 3 + C@ clk-hr !
  time-buf 4 + C@ clk-mn !
  time-buf 5 + C@ clk-sc !

  \ Calibrate (only after the first sync gave us a baseline).
  synced-once @ IF
    calibrate-vps
  ELSE
    -1 synced-once !
  THEN

  \ Remember this sync's FN time and reset counters for next interval.
  clk-mn @ last-fn-min !
  clk-sc @ last-fn-sec !
  0 vps-cnt !
  0 vs-cnt !
  SYNC-FLASH-COUNT sync-flash ! ;


\ ── Init RG6 mode + font ───────────────────────────────────────────

: init-rg-font  ( -- )
  init-font
  $9000 KVAR-RGFONT !
  $20   KVAR-RGCHARMIN C!
  8     KVAR-RGGLYPHSZ C!
  7     KVAR-RGNROWS   C!
  32    KVAR-RGBPR     C!
  8     KVAR-RGROWH    C! ;


\ ── Main loop ──────────────────────────────────────────────────────

\ DEV-ONLY: hardcode a known time so we can iterate on visuals under
\ XRoar without a live FujiNet.  Swap back to sync-from-fn before
\ shipping to real hardware.
: fake-time  ( -- )
  126 clk-yr !  4 clk-mo !  22 clk-dy !
  10  clk-hr !  10 clk-mn !  0  clk-sc !
  0   vs-cnt !
  0   fn-enabled !           \ skip the :59 sync (no FN in dev)
  SYNC-FLASH-COUNT sync-flash ! ;

\ Render the full clock (face + ticks + pin + digital + flash + hands)
\ into whichever buffer is currently the BACK.  Used twice during init
\ so both VRAMs start with identical content.
: paint-back-full  ( -- )
  bk-vram @ KVAR-RGVRAM !
  draw-face
  render-datetime
  render-sync-flash
  tick-hands ;

: clock-init  ( -- )
  rg-init
  init-mo-days
  init-sin
  init-rg-font

  \ Adaptive vsync calibration starts at the nominal 60 Hz.
  INITIAL-VPS vps !
  0 vps-cnt !  0 synced-once !

  \ Initial buffer state: A is the front (rg-init's default), B is back.
  VRAM-A   fr-vram !  VRAM-B   bk-vram !
  BUF-A-HR fr-hr-buf !  BUF-B-HR bk-hr-buf !
  BUF-A-MN fr-mn-buf !  BUF-B-MN bk-mn-buf !
  BUF-A-SC fr-sc-buf !  BUF-B-SC bk-sc-buf !
  0 fr-hr-len !  0 fr-mn-len !  0 fr-sc-len !
  0 bk-hr-len !  0 bk-mn-len !  0 bk-sc-len !

  fake-time                  \ DEV: replace with sync-from-fn for hardware

  \ Render full clock into VRAM-B (current back), clearing it first.
  VRAM-B 6144 0 FILL
  paint-back-full

  \ Flip so A becomes back; render into A (already cleared by rg-init).
  flip-state
  paint-back-full

  \ Flip back so original A is front again, B is back for next tick.
  flip-state ;

VARIABLE last-sc
VARIABLE last-mn
VARIABLE last-dy
VARIABLE ss-pending         \ frames left to re-render :SS + flash
VARIABLE hm-pending         \ frames left to re-render HH:MM
VARIABLE date-pending       \ frames left to re-render YYYY.MM.DD

: clock-loop  ( -- )
  clk-sc @ last-sc !
  clk-mn @ last-mn !
  clk-dy @ last-dy !
  \ Paint all three digital groups on the first 2 backs so both VRAMs
  \ match before we start relying on split renders.
  2 ss-pending !
  2 hm-pending !
  2 date-pending !
  BEGIN
    \ One vsync wait per iteration → 1 frame per loop = true 60 Hz.
    \ Flip happens IMMEDIATELY after vsync (still in vblank window), so
    \ the back we rendered LAST iteration becomes the front this iteration.
    vsync+
    bk-vram @ 9 RSHIFT set-sam-f-fast
    flip-state
    tick-frame                    \ count this frame; may bump clk-sc

    bk-vram @ KVAR-RGVRAM !       \ target the new back for all draws

    \ Per-second housekeeping (sync + digital pending counters).
    clk-sc @ last-sc @ <> IF
      clk-sc @ last-sc !
      clk-sc @ 59 = fn-enabled @ AND IF sync-from-fn THEN
      sync-flash @ 0 > IF -1 sync-flash +! THEN
      2 ss-pending !              \ :SS always changes every second
      clk-mn @ last-mn @ <> IF
        clk-mn @ last-mn !
        2 hm-pending !
        clk-dy @ last-dy @ <> IF
          clk-dy @ last-dy !
          2 date-pending !
        THEN
      THEN
    THEN

    \ Split digital render — each group only repaints when its source
    \ changed.  Cuts the sec-change frame from ~7,174 → ~1,350cy.
    date-pending @ 0 > IF
      render-date
      -1 date-pending +!
    THEN
    hm-pending @ 0 > IF
      render-hm
      -1 hm-pending +!
    THEN
    ss-pending @ 0 > IF
      render-ss
      render-sync-flash
      -1 ss-pending +!
    THEN

    \ Smooth-sweep all three hands.  Buffer-private lasts drive the
    \ dedup + cascade logic, so no pending counter is needed — each back
    \ catches up on its own next visit.
    redraw-hands
  AGAIN ;

: main  ( -- )
  clock-init
  clock-loop ;

main
