\ dac.fs — Joystick reading via DAC successive approximation
\
\ Provides: joy-sel-rx, joy-sel-ry, joy-sel-lx, joy-sel-ly,
\           joy-bit, joy-sample, joy-x, joy-y, joy-fire?
\ Requires: kernel primitives C@, C!, AND, OR, 2DUP, DROP, LSHIFT, KBD-SCAN
\
\ The CoCo reads joystick axes via a 6-bit DAC ($FF20) and a
\ comparator on PIA0-A bit 7 ($FF00).  A 6-iteration successive
\ approximation binary search converges on the axis value (0-63).
\
\ The analog multiplexer selects which axis to read via SEL1/SEL2
\ on PIA0 control registers $FF01 (CA2) and $FF03 (CB2).
\ PIA control register bits 5:4:3 control CA2/CB2 output.
\ Manual output mode: bits 5:4 = 11, bit 3 = output value.
\ Base value with bit2=1 (data reg), bit1=0, bit0=0 = $34.
\ SEL=0: $34 (bit3=0),  SEL=1: $3C (bit3=1)
\
\ Axis select mapping (SEL1, SEL2):
\   Right X: 0, 0    Right Y: 1, 0
\   Left  X: 0, 1    Left  Y: 1, 1
\
\ Fire buttons are on the keyboard matrix:
\   Right fire: column 0 ($FE), row bit 0
\   Left  fire: column 0 ($FE), row bit 1

\ ── Axis selection ───────────────────────────────────────────────

: joy-sel-rx  ( -- )  $34 $FF01 C!  $34 $FF03 C! ;
: joy-sel-ry  ( -- )  $3C $FF01 C!  $34 $FF03 C! ;
: joy-sel-lx  ( -- )  $34 $FF01 C!  $3C $FF03 C! ;
: joy-sel-ly  ( -- )  $3C $FF01 C!  $3C $FF03 C! ;

\ ── Successive approximation ─────────────────────────────────────

: joy-bit  ( result bit -- result' )
  2DUP OR 2 LSHIFT $FF20 C!   \ write (result|bit)<<2 to DAC
  $FF00 C@ $80 AND IF         \ comparator: joystick > DAC?
    OR                         \ keep the bit
  ELSE
    DROP                       \ discard the bit
  THEN ;

: joy-sample  ( -- 0..63 )
  0  32 joy-bit  16 joy-bit  8 joy-bit
     4 joy-bit   2 joy-bit   1 joy-bit ;

\ ── Convenience words ────────────────────────────────────────────

: joy-x  ( -- 0..63 )  joy-sel-rx joy-sample ;
: joy-y  ( -- 0..63 )  joy-sel-ry joy-sample ;

\ ── Fire buttons ─────────────────────────────────────────────────

: joy-fire?  ( -- flag )
  $FE KBD-SCAN 1 AND 0 <> ;

: joy-fire-l?  ( -- flag )
  $FE KBD-SCAN 2 AND 0 <> ;
