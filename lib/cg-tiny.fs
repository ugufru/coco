\ cg-tiny.fs - 3x5 font for 4-colour CG screens, one byte per glyph row
\
\ Provides: cg-base, cg-ink, cg-paper, cg-font, cg-at, cg-emit, cg-type,
\           cg-big, cg-fill, cg-colour, cg-mix
\ Requires: kernel primitives only
\
\ In the CG modes a byte is four 2-bit pixels, so a 3x5 glyph plus one
\ pixel of spacing is exactly one byte wide. A character cell is 4x6
\ pixels: five glyph rows and a blank one. On a 128-pixel screen that is
\ 32 columns; CG2 (128x64) gives 10 rows of text, CG6 (128x192) 32.
\ cg-big draws the same glyphs doubled, 8x12, for clock digits.
\
\ Colours are fill bytes, one colour in all four pixels: with colour set
\ 0, $00 green, $55 yellow, $AA blue, $FF red (cg-colour makes them).
\
\ Characters $20-$60 are in the font; lowercase prints as uppercase.
\ The rarely used [ \ ] ^ _ ` are media icons instead:
\   [ rewind-arrow   \ play   ] stop   ^ pause   _ bar right   ` bar left
\ so "_[" draws |<  (previous) and "\`" draws >|  (next).
\
\ Usage (CG2 at $0600, every row 32 bytes):
\   $0600 cg-base !  1 cg-colour cg-ink !  2 cg-colour cg-paper !
\   0 10 cg-at  S" HELLO" cg-type

VARIABLE cg-base     \ VRAM address of the top-left byte
VARIABLE cg-ink      \ fill byte for glyph pixels
VARIABLE cg-paper    \ fill byte for the background
VARIABLE _cg-p       \ the cursor: address of the next cell's top byte

\ Each glyph is five row masks: pixel 0 is $C0, pixel 1 $30, pixel 2 $0C.
DATA[PY cg-font
G = {
' ': '... ... ... ... ...', '!': '.#. .#. .#. ... .#.', '"': '#.# #.# ... ... ...',
'#': '#.# ### #.# ### #.#', '$': '.## ##. .#. .## ##.', '%': '#.. ..# .#. #.. ..#',
'&': '.#. #.# .#. #.# .##', "'": '.#. .#. ... ... ...', '(': '..# .#. .#. .#. ..#',
')': '#.. .#. .#. .#. #..', '*': '... #.# .#. #.# ...', '+': '... .#. ### .#. ...',
',': '... ... ... .#. #..', '-': '... ... ### ... ...', '.': '... ... ... ... .#.',
'/': '..# ..# .#. #.. #..', '0': '### #.# #.# #.# ###', '1': '.#. ##. .#. .#. ###',
'2': '### ..# ### #.. ###', '3': '### ..# .## ..# ###', '4': '#.# #.# ### ..# ..#',
'5': '### #.. ### ..# ###', '6': '### #.. ### #.# ###', '7': '### ..# ..# .#. .#.',
'8': '### #.# ### #.# ###', '9': '### #.# ### ..# ###', ':': '... .#. ... .#. ...',
';': '... .#. ... .#. #..', '<': '..# .#. #.. .#. ..#', '=': '... ### ... ### ...',
'>': '#.. .#. ..# .#. #..', '?': '### ..# .#. ... .#.', '@': '### #.# ### #.. .##',
'A': '.#. #.# ### #.# #.#', 'B': '##. #.# ##. #.# ##.', 'C': '.## #.. #.. #.. .##',
'D': '##. #.# #.# #.# ##.', 'E': '### #.. ##. #.. ###', 'F': '### #.. ##. #.. #..',
'G': '.## #.. #.# #.# .##', 'H': '#.# #.# ### #.# #.#', 'I': '### .#. .#. .#. ###',
'J': '..# ..# ..# #.# .#.', 'K': '#.# #.# ##. #.# #.#', 'L': '#.. #.. #.. #.. ###',
'M': '#.# ### #.# #.# #.#', 'N': '##. #.# #.# #.# #.#', 'O': '.#. #.# #.# #.# .#.',
'P': '##. #.# ##. #.. #..', 'Q': '.#. #.# #.# ### .##', 'R': '##. #.# ##. #.# #.#',
'S': '.## #.. .#. ..# ##.', 'T': '### .#. .#. .#. .#.', 'U': '#.# #.# #.# #.# ###',
'V': '#.# #.# #.# #.# .#.', 'W': '#.# #.# ### ### #.#', 'X': '#.# #.# .#. #.# #.#',
'Y': '#.# #.# .#. .#. .#.', 'Z': '### ..# .#. #.. ###',
'[': '..# .## ### .## ..#', '\\': '#.. ##. ### ##. #..', ']': '... ### ### ### ...',
'^': '#.# #.# #.# #.# #.#', '_': '..# ..# ..# ..# ..#', '`': '#.. #.. #.. #.. #..',
}
out = bytearray()
for c in range(0x20, 0x61):
    for row in G[chr(c)].split():
        out.append(sum((0xC0 >> 2 * i) for i, p in enumerate(row) if p == '#'))
bytes(out)
]DATA

\ cg-colour -- the fill byte for colour 0-3
: cg-colour  ( n -- fill )  3 AND  $55 * ;

\ cg-at -- put the cursor at byte column 0-31, pixel row y
: cg-at  ( col y -- )  32 * +  cg-base @ +  _cg-p ! ;

: _cg-glyph  ( c -- addr )
  DUP $60 > IF $20 - THEN  $20 - 0 MAX 64 MIN  5 * cg-font + ;

\ cg-mix -- a byte with ink where the mask is set, paper elsewhere
: cg-mix  ( mask -- byte )  DUP cg-ink @ AND  SWAP INVERT cg-paper @ AND  OR ;

\ cg-emit -- draw one character at the cursor and move right one cell
: cg-emit  ( c -- )
  _cg-glyph  _cg-p @
  5 0 DO  OVER I + C@ cg-mix  OVER C!  32 +  LOOP
  cg-paper @ SWAP C!  DROP
  1 _cg-p +! ;

\ cg-type -- draw a string at the cursor
: cg-type  ( addr u -- )
  DUP IF 0 DO  DUP I + C@ cg-emit  LOOP ELSE DROP THEN DROP ;

\ Doubled pixels: mask bits 7-6 and 5-4 fill the left byte, 3-2 the
\ right byte's left half.
: _cg-left   ( mask -- m )
  DUP $C0 AND IF $F0 ELSE 0 THEN  SWAP $30 AND IF $0F OR THEN ;
: _cg-right  ( mask -- m )  $0C AND IF $F0 ELSE 0 THEN ;

VARIABLE _cg-l
VARIABLE _cg-r

\ cg-big -- draw one character doubled (8x12) and move right two bytes
: cg-big  ( c -- )
  _cg-glyph  _cg-p @
  5 0 DO
    OVER I + C@  DUP _cg-left cg-mix _cg-l !  _cg-right cg-mix _cg-r !
    _cg-l @ OVER C!       _cg-r @ OVER 1 + C!
    _cg-l @ OVER 32 + C!  _cg-r @ OVER 33 + C!
    64 +
  LOOP
  DUP 2 cg-paper @ FILL  32 + 2 cg-paper @ FILL
  DROP  2 _cg-p +! ;

VARIABLE _cg-f

\ cg-fill -- fill a box with a fill byte: byte column, pixel row, width
\ in bytes, height in rows (at least 1)
: cg-fill  ( col y w h fill -- )
  _cg-f !  >R >R  32 * +  cg-base @ +  R> R>
  0 DO  2DUP _cg-f @ FILL  SWAP 32 + SWAP  LOOP  2DROP ;
