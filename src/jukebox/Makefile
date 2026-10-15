NAME        = jukebox
EXTRA_DEPS  = build/songs.dsk ../../lib/screen.fs ../../lib/bye.fs ../../lib/print.fs ../../lib/sn76489.fs ../../lib/decb.fs
# A Multi-Pak with the disk controller in slot 0 and the GameMaster in
# slot 1, and the song disk in drive 0.
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
              -load-fd0 build/songs.dsk -kbd-translate

include ../../make/demo.mk

DECB = $(shell command -v decb || echo ~/bin/decb)
SNG  = ../sound-sn76489

$(SNG)/build/badinerie.snd: $(SNG)/badinerie.py $(SNG)/sn76489.py
	$(MAKE) -C $(SNG) build/badinerie.snd

build/songs.dsk: $(SNG)/build/badinerie.snd
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
