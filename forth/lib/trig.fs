\ trig.fs — Sine/cosine lookup table for angle-based operations
\
\ Provides: init-sin, sin, cos, angle-dx, angle-dy
\ Requires: kernel primitives C@, *, +, -, /MOD, NEGATE, RSHIFT,
\           DUP, DROP, SWAP, OVER, @, !, <, >, 0=, IF, ELSE, THEN
\           datawrite.fs (tp, tb)
\
\ 91-entry sine table covering 0-90 degrees.  Values are 7-bit
\ fixed-point: 0 = 0.000, 127 = 1.000 (actually 0.992).
\ Cosine and full 0-360 range derived by quadrant mirroring.
\
\ Usage:
\   init-sin                   \ build the lookup table once
\   45 sin                     \ ( -- 90 )  ~0.707 * 127
\   30 cos                     \ ( -- 110 ) ~0.866 * 127
\   45 64 angle-dx             \ ( -- 45 )  x displacement for 64-pixel line
\   45 64 angle-dy             \ ( -- -45 ) y displacement (negative = up)

\ ── Sine table (91 bytes at $86CC) ──────────────────────────────────────
\ sin(0)=0 through sin(90)=127, in 1-degree steps.
\ Values = round(sin(deg) * 127).

$86CC CONSTANT SINTAB

: init-sin  ( -- )
  SINTAB tp !
  \  0- 9: sin(0)..sin(9)
    0 tb   2 tb   4 tb   7 tb   9 tb  11 tb  13 tb  15 tb  18 tb  20 tb
  \ 10-19
   22 tb  24 tb  26 tb  28 tb  30 tb  33 tb  35 tb  37 tb  39 tb  41 tb
  \ 20-29
   43 tb  45 tb  47 tb  49 tb  51 tb  53 tb  55 tb  57 tb  58 tb  60 tb
  \ 30-39
   64 tb  65 tb  67 tb  69 tb  71 tb  72 tb  74 tb  76 tb  77 tb  79 tb
  \ 40-49
   82 tb  83 tb  85 tb  86 tb  88 tb  90 tb  91 tb  92 tb  94 tb  95 tb
  \ 50-59
   97 tb  98 tb 100 tb 101 tb 102 tb 104 tb 105 tb 106 tb 107 tb 108 tb
  \ 60-69
  110 tb 111 tb 112 tb 113 tb 114 tb 115 tb 116 tb 117 tb 117 tb 118 tb
  \ 70-79
  119 tb 120 tb 120 tb 121 tb 122 tb 122 tb 123 tb 123 tb 124 tb 124 tb
  \ 80-89
  125 tb 125 tb 126 tb 126 tb 126 tb 126 tb 127 tb 127 tb 127 tb 127 tb
  \ 90
  127 tb ;

\ ── Signed divide by 128 ────────────────────────────────────────────────
\ RSHIFT is logical (unsigned) on the 6809, so we handle sign manually.

: s/128  ( n -- n/128 )
  DUP 0 < IF
    NEGATE 7 RSHIFT NEGATE
  ELSE
    7 RSHIFT
  THEN ;

\ ── sin ( angle -- value ) ───────────────────────────────────────────────
\ Return sine of angle (0-360) as signed fixed-point (-127..+127).

VARIABLE sa-tmp

: sin  ( angle -- value )
  \ Normalize to 0-359 (handles any positive angle)
  360 /MOD DROP
  sa-tmp !

  sa-tmp @ 180 < IF
    \ Quadrant 1 or 2: sin is positive
    sa-tmp @ 90 > IF
      180 sa-tmp @ - SINTAB + C@
    ELSE
      sa-tmp @ SINTAB + C@
    THEN
  ELSE
    \ Quadrant 3 or 4: sin is negative
    sa-tmp @ 270 > IF
      360 sa-tmp @ - SINTAB + C@ NEGATE
    ELSE
      sa-tmp @ 180 - SINTAB + C@ NEGATE
    THEN
  THEN ;

\ ── cos ( angle -- value ) ───────────────────────────────────────────────

: cos  ( angle -- value )  90 + sin ;

\ ── angle-dx ( angle length -- dx ) ─────────────────────────────────────
\ X displacement: dx = length * cos(angle) / 128
\ Angle 0 = right, 90 = up, 180 = left, 270 = down.

VARIABLE ad-len

: angle-dx  ( angle length -- dx )
  ad-len !
  cos ad-len @ * s/128 ;

\ ── angle-dy ( angle length -- dy ) ─────────────────────────────────────
\ Y displacement: dy = -length * sin(angle) / 128
\ Negative because screen Y increases downward but angle 90 = up.

: angle-dy  ( angle length -- dy )
  ad-len !
  sin NEGATE ad-len @ * s/128 ;
