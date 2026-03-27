\ sound.fs — Minimal sound test

INCLUDE ../../forth/lib/screen.fs

\ Bare minimum: call SND_PLAY from a CODE word
CODE test-sound
        PSHS    X
        LDA     #10
        LDB     #200
        JSR     SND_PLAY
        PULS    X
        ;NEXT
;CODE

: main  ( -- )
  CHAR G EMIT CHAR O EMIT CR
  test-sound
  CHAR O EMIT CHAR K EMIT
  HALT ;

main
