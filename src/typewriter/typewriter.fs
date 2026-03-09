\ src/typewriter/typewriter.fs — bare-metal keyboard echo test
\
\ Reads keys via the new PIA0-based KEY (no ROM dependency) and echoes
\ them to the VDG screen.  Tests CODE_KEY, EMIT, CR, and the full
\ MATRIX2ASCII keyboard scan path.
\
\ Key behaviour:
\   Printable keys  → emit character at cursor, advance
\   ENTER  ($0D)    → move to next row (CR)
\   CLEAR  ($0C)    → clear screen and home cursor
\   BREAK  ($03)    → halt

\ ── Screen utilities ──────────────────────────────────────────────────────────

\ cls: fill video RAM with VDG spaces ($60) and home the cursor.
\ $0400 = video RAM base, 512 bytes (32 cols x 16 rows).
: cls
  512 0 DO  $60 $0400 I + C!  LOOP
  0 0 AT ;

\ ── Main typewriter loop ──────────────────────────────────────────────────────

: typewriter
  cls
  BEGIN
    KEY
    DUP $03 = IF  DROP  halt           THEN
    DUP $0D = IF  DROP  cr             ELSE
    DUP $0C = IF  DROP  cls            ELSE
    emit
    THEN THEN
  AGAIN ;

typewriter
