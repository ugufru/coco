\ console-test.fs - visual check for lib/console.fs (#554, #555)
\
\ Expected on coco2bus (MC6847T1):
\   - rows "line i" .. "line t" visible (a-h scrolled off the top)
\   - "Hello, World! 0123 [abc] {|}~" in real lowercase
\     (the T1 draws { | } much like ( ! ), that is its font)
\   - "backspace: ok" (the X was erased by BS)
\   - blank row, then "done."
\
\ Build: python3 tools/fc.py src/terminal/console-test.fs \
\          --kernel kernel/build/kernel.map --kernel-bin kernel/build/kernel.bin \
\          --output src/terminal/console-test.bin

INCLUDE ../../lib/console.fs

: nl  $0D con-emit ;

: main
  lower-on
  con-cls
  20 0 DO  S" line " con-type  I CHAR a + con-emit  nl  LOOP
  S" Hello, World! 0123 [abc] {|}~" con-type nl
  S" backspace: X" con-type $08 con-emit S" ok" con-type nl
  nl
  S" done." con-type
  BEGIN AGAIN ;

main
