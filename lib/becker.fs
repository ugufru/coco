\ becker.fs - Byte I/O over XRoar's Becker port
\
\ The Becker port is a two-register pseudo device that XRoar attaches to a
\ disk cartridge and forwards to a TCP connection (xroar/src/becker.c):
\
\   $FF41  status   bit 1 set = a received byte is waiting
\   $FF42  data     read = next received byte, write = send a byte
\
\ XRoar is the TCP client: it connects once, when the cartridge is created,
\ to 127.0.0.1:65504 (override with -becker-ip / -becker-port), so the
\ server must already be listening.  Enable it without HDB-DOS with
\   xroar -machine coco2bus -cart rsdos -cart-becker ...
\ Raw bytes pass straight through; no DriveWire protocol is involved.
\ Writes are dropped by XRoar if its buffer is full, so the far end paces.
\
\ Unlike lib/fujinet.fs (HDB-DOS DriveWire vectors), this needs no ROM
\ calls, works the same in ROM and all-RAM builds, and is plain Forth.
\
\ Provides: bk?, bk@, bk!, bk-type
\ Requires: kernel C@ C! AND DO/LOOP
\
\ bk?     ( -- flag )        nonzero if a byte is waiting
\ bk@     ( -- c )           read the next byte (check bk? first)
\ bk!     ( c -- )           send one byte
\ bk-type ( addr len -- )    send a buffer

: bk?  ( -- flag )  $FF41 C@ 2 AND ;
: bk@  ( -- c )     $FF42 C@ ;
: bk!  ( c -- )     $FF42 C! ;

: bk-type  ( addr len -- )
  DUP IF
    OVER + SWAP DO  I C@ bk!  LOOP
  ELSE
    2DROP
  THEN ;
