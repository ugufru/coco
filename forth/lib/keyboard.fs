\ keyboard.fs — CoCo keyboard matrix constants and utilities
\
\ INCLUDE this file in any application that needs direct key scanning.
\ Requires the kernel primitives: KBD-SCAN, AND, SWAP.
\
\ Usage example:
\   INCLUDE lib/keyboard.fs
\   KB-C4 KB-R2 KEY-HELD?   ( -- flag  true if '2' is currently held )

\ ── Column strobe masks ───────────────────────────────────────────────────────
\ Pass one of these to KBD-SCAN. The bit that is LOW selects that column group.
\ KBD-SCAN writes the mask to PIA0-B ($FF02) and returns the active row bits.

$FE CONSTANT KB-C0   \ @ A B C D E F G
$FD CONSTANT KB-C1   \ H I J K L M N O
$FB CONSTANT KB-C2   \ P Q R S T U V W
$F7 CONSTANT KB-C3   \ X Y Z UP DN LT RT SPC
$EF CONSTANT KB-C4   \ 0 1 2 3 4 5 6 7
$DF CONSTANT KB-C5   \ 8 9 : ; , - . /
$BF CONSTANT KB-C6   \ ENTER CLEAR BREAK ALT CTRL SHIFT F1 F2

\ ── Row bit masks ─────────────────────────────────────────────────────────────
\ AND one of these with the result of KBD-SCAN to test a specific key.
\ Row positions correspond to the KEY_TABLE column layout in kernel.asm.

$01 CONSTANT KB-R0   \ row 0: @  H  P  X  0  8  ENTER
$02 CONSTANT KB-R1   \ row 1: A  I  Q  Y  1  9  CLEAR
$04 CONSTANT KB-R2   \ row 2: B  J  R  Z  2  :  BREAK
$08 CONSTANT KB-R3   \ row 3: C  K  S  UP 3  ;  ALT
$10 CONSTANT KB-R4   \ row 4: D  L  T  DN 4  ,  CTRL
$20 CONSTANT KB-R5   \ row 5: E  M  U  LT 5  -  SHIFT
$40 CONSTANT KB-R6   \ row 6: F  N  V  RT 6  .  F1
$80 CONSTANT KB-R7   \ row 7: G  O  W  SP 7  /  F2  (joystick comparator line)

\ ── KEY-HELD? ( col_mask row_bit -- flag ) ───────────────────────────────────
\ Non-blocking key test. Returns non-zero if the key is currently pressed.
\ Does NOT affect the KEY debounce state.
\ Note: KB-R7 keys (G O W SPC 7 / F2) require no joystick connected.

: KEY-HELD?   SWAP KBD-SCAN AND ;
