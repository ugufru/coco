NAME        = decb-test
EXTRA_DEPS  = build/expect.bin build/test.dsk ../../lib/decb.fs ../../lib/print.fs ../../lib/bye.fs ../../lib/screen.fs
XROAR_EXTRA = -cart rsdos -load-fd0 build/test.dsk -kbd-translate

include ../../make/demo.mk

DECB = $(shell command -v decb || echo ~/bin/decb)

build/expect.bin build/hello.txt build/song.sng build/big.dat: mkfiles.py ../sound-sn76489/build/badinerie.snd
	python3 mkfiles.py

../sound-sn76489/build/badinerie.snd:
	$(MAKE) -C ../sound-sn76489 build/badinerie.snd

build/test.dsk: build/expect.bin
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy build/hello.txt $@,HELLO.TXT -0 -a
	$(DECB) copy build/song.sng  $@,SONG.SNG -2 -b
	$(DECB) copy build/big.dat   $@,BIG.DAT -2 -b
