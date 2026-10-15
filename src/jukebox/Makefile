NAME        = jukebox
EXTRA_DEPS  = ../../lib/screen.fs ../../lib/bye.fs ../../lib/print.fs ../../lib/sn76489.fs ../../lib/decb.fs
# A Multi-Pak with the disk controller in slot 0 and the GameMaster in
# slot 1, and the song disk in drive 0.
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
              -load-fd0 build/songs.dsk -kbd-translate

include ../../make/demo.mk

# The disk carries the player too, first, so a CoCo Zero with autorun on
# (it runs drive 0's first program) boots straight into the menu (#594).
all: build/songs.dsk
run: build/songs.dsk

DECB = $(shell command -v decb || echo ~/bin/decb)
SNG  = ../sound-sn76489

$(SNG)/build/badinerie.snd: $(SNG)/badinerie.py $(SNG)/sn76489.py
	$(MAKE) -C $(SNG) build/badinerie.snd

build/songs.dsk: $(BIN) $(SNG)/build/badinerie.snd
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,JUKEBOX.BIN -2 -b
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
