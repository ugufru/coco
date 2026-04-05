\ rg-text.fs — RG mode text rendering (1 bit/pixel)
\
\ Provides: cv (variable), cb (variable), rg-char (kernel primitive)
\ Requires: font library (font5x7.fs or font-art.fs)
\
\ rg-char uses kernel config variables at fixed RAM addresses:
\   $75 = font base (FDB)    $77 = min char (FCB)
\   $78 = glyph size (FCB)   $79 = nrows (FCB)
\   $7A = bpr (FCB)          $7B = row height (FCB)

VARIABLE cv                       \ cached VRAM base address
VARIABLE cb                       \ cached bytes per row

\ rg-char ( char cx cy -- )  — kernel primitive

: rg-type  ( addr len -- )  0 DO  DUP I + C@ rg-emit  LOOP  DROP ;
