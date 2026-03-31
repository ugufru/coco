\ keyboard.fs — CoCo keyboard matrix constants and utilities
\
\ INCLUDE this file in any application that needs direct key scanning.
\ Requires the kernel primitives: KBD-SCAN, AND, SWAP.
\
\ Usage example:
\   INCLUDE lib/keyboard.fs
\   KB-C3 KB-R3 KEY-HELD?   ( -- flag  true if UP arrow is held )
\
\ The CoCo keyboard matrix has 8 columns x 7 rows.  Each key has its
\ own column — keys within a "group" share a ROW bit, not a column.
\ Source of truth: kernel.asm KEY_TABLE.
\
\ Matrix layout (column strobes across, row bits down):
\ Source: kernel.asm KEY_TABLE.  7 rows per column (PA7 is joystick).
\
\         C0($FE) C1($FD) C2($FB) C3($F7) C4($EF) C5($DF) C6($BF) C7($7F)
\ R0($01)   @       A       B       C       D       E       F       G
\ R1($02)   H       I       J       K       L       M       N       O
\ R2($04)   P       Q       R       S       T       U       V       W
\ R3($08)   X       Y       Z       UP      DN      LT      RT      SP
\ R4($10)   0       1       2       3       4       5       6       7
\ R5($20)   8       9       :       ;       ,       -       .       /
\ R6($40)  ENT     CLR     BRK     ALT     CTL     SHF     F1      F2

\ ── Column strobe masks ───────────────────────────────────────────────────────
\ Pass one of these to KBD-SCAN. The bit that is LOW selects that column.
\ KBD-SCAN writes the mask to PIA0-B ($FF02) and returns the active row bits.

$FE CONSTANT KB-C0   \ @  H  P  X  0  8  ENT
$FD CONSTANT KB-C1   \ A  I  Q  Y  1  9  CLR
$FB CONSTANT KB-C2   \ B  J  R  Z  2  :  BRK
$F7 CONSTANT KB-C3   \ C  K  S  UP 3  ;  ALT
$EF CONSTANT KB-C4   \ D  L  T  DN 4  ,  CTL
$DF CONSTANT KB-C5   \ E  M  U  LT 5  -  SHF
$BF CONSTANT KB-C6   \ F  N  V  RT 6  .  F1
$7F CONSTANT KB-C7   \ G  O  W  SP 7  /  F2

\ ── Row bit masks ─────────────────────────────────────────────────────────────
\ AND one of these with the result of KBD-SCAN to test a specific key.

$01 CONSTANT KB-R0   \ @  A  B  C  D  E  F  G
$02 CONSTANT KB-R1   \ H  I  J  K  L  M  N  O
$04 CONSTANT KB-R2   \ P  Q  R  S  T  U  V  W
$08 CONSTANT KB-R3   \ X  Y  Z  UP DN LT RT SP  — all arrows share this row
$10 CONSTANT KB-R4   \ 0  1  2  3  4  5  6  7   — all digits share this row
$20 CONSTANT KB-R5   \ 8  9  :  ;  ,  -  .  /
$40 CONSTANT KB-R6   \ ENT CLR BRK ALT CTL SHF F1 F2
$80 CONSTANT KB-R7   \ (not a keyboard row — joystick DAC comparator)

\ ── KEY-HELD? ( col_mask row_bit -- flag ) ───────────────────────────────────
\ Non-blocking key test. Returns non-zero if the key is currently pressed.
\ Does NOT affect the KEY debounce state.
\ Note: KB-R7 keys (G O W SPC 7 / F2) require no joystick connected.

: KEY-HELD?   SWAP KBD-SCAN AND ;
