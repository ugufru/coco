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

\ Artifact pixels are 2x wider than tall on the CRT, so halve the
\ x displacement to render true circles (not ovals).

: ep1  ( angle len -- )
  2DUP angle-dx 2/ CX + tx1 !
  angle-dy CY + ty1 ! ;

: ep2  ( angle len -- )
  2DUP angle-dx 2/ CX + tx2 !
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

: face-ticks  ( -- )
  12 0 DO
    \ inner radius depends on whether I divisible by 3
    I 3 /MOD DROP 0 = IF
      R-TICKMI tick-ri !
    ELSE
      R-TICKMN tick-ri !
    THEN
    I 30 * clk>trig tang !
    tang @ tick-ri @ ep1
    tang @ R-TICKO  ep2
    C-WHITE stroke
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


\ Hand-angle calculations (return clock-degrees 0..359)
: hr-angle  ( -- deg )
  clk-hr @ 12 /MOD DROP 30 *
  clk-mn @ 2 / + ;

\ Hands tick discretely (once per minute / once per second), so the
\ angle calcs don't need sub-second smoothness.

: mn-angle  ( -- deg )  clk-mn @ 6 * ;

\ Smooth-sweep second-hand angle includes sub-second progress so the
\ hand updates 60 Hz instead of jumping each tick.  vs-cnt and vps are
\ both scaled by 16, so their ratio (vs-cnt/vps) is unitless seconds.
\ Sub-second angle = 6 * vs-cnt / vps; the (vs-cnt * 6) intermediate
\ stays under 16-bit signed range (max ~5760).
: sc-angle  ( -- deg )
  clk-sc @ 6 *
  vs-cnt @ 6 *  vps @ /  + ;


\ Single-hand redraw on the BACK buffer.  Used every frame for the
\ smooth-sweep second hand — far cheaper than a full tick-hands pass.
\ Min/hr are assumed unchanged since the last back render.
: redraw-sc-back  ( -- )
  bk-sc-buf @ bk-sc-len @ erase-line
  sc-angle R-SC bk-sc-buf @ trace-line bk-sc-len !
  bk-sc-buf @ bk-sc-len @ C-RED paint-line ;


\ Erase ALL hands top-down then redraw them all bottom-up — applied
\ to whichever buffer is currently the BACK.  Each VRAM has its own
\ trio of beam buffers so the saved-pixel state stays consistent
\ with that buffer's actual contents.
: tick-hands  ( -- )
  bk-sc-buf @ bk-sc-len @ erase-line
  bk-mn-buf @ bk-mn-len @ erase-line
  bk-hr-buf @ bk-hr-len @ erase-line

  hr-angle R-HR bk-hr-buf @ trace-line bk-hr-len !
  bk-hr-buf @ bk-hr-len @ C-WHITE paint-line

  mn-angle R-MN bk-mn-buf @ trace-line bk-mn-len !
  bk-mn-buf @ bk-mn-len @ C-WHITE paint-line

  sc-angle R-SC bk-sc-buf @ trace-line bk-sc-len !
  bk-sc-buf @ bk-sc-len @ C-RED paint-line ;


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

: render-time  ( -- )
  clk-hr @ 12 21 2dig                  \ HH cols 12..13
  CHAR :  14 21 rg-char
  clk-mn @ 15 21 2dig                  \ MM cols 15..16
  CHAR :  17 21 rg-char
  clk-sc @ 18 21 2dig ;                \ SS cols 18..19

: render-datetime  ( -- )
  render-date render-time ;


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
VARIABLE mn-pending         \ frames left to do full tick-hands after mn change
VARIABLE dig-pending        \ frames left to re-render digital + flash

: clock-loop  ( -- )
  clk-sc @ last-sc !
  clk-mn @ last-mn !
  0 mn-pending !
  2 dig-pending !                 \ render digital on first 2 frames
  BEGIN
    \ One vsync wait per iteration → 1 frame per loop = true 60 Hz.
    \ Flip happens IMMEDIATELY after vsync (still in vblank window), so
    \ the back we rendered LAST iteration becomes the front this iteration.
    vsync+
    bk-vram @ 9 RSHIFT set-sam-f-fast
    flip-state
    tick-frame                    \ count this frame; may bump clk-sc

    bk-vram @ KVAR-RGVRAM !       \ target the new back for all draws

    \ Per-second housekeeping (sync, mn/dig pending triggers).
    clk-sc @ last-sc @ <> IF
      clk-sc @ last-sc !
      clk-sc @ 59 = fn-enabled @ AND IF sync-from-fn THEN
      sync-flash @ 0 > IF -1 sync-flash +! THEN
      2 dig-pending !             \ refresh digital on next 2 backs
      clk-mn @ last-mn @ <> IF
        clk-mn @ last-mn !
        2 mn-pending !
      THEN
    THEN

    \ Digital + flash only when pending — saves ~3500 cycles/frame on
    \ the 58/60 frames where sec hasn't changed.
    dig-pending @ 0 > IF
      render-datetime
      render-sync-flash
      -1 dig-pending +!
    THEN

    \ Hands: full tick-hands while mn-pending covers both backs after
    \ a minute change; otherwise just the cheap sec-hand redraw.
    mn-pending @ 0 > IF
      tick-hands
      -1 mn-pending +!
    ELSE
      redraw-sc-back
    THEN
  AGAIN ;

: main  ( -- )
  clock-init
  clock-loop ;

main
