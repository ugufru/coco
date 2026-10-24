NAME        = jukebox
EXTRA_DEPS  = ../../lib/screen.fs ../../lib/bye.fs ../../lib/vdg.fs ../../lib/cg-tiny.fs ../../lib/sn76489.fs ../../lib/vgm.fs ../../lib/decb.fs
# A Multi-Pak with the disk controller in slot 0 and the GameMaster in
# slot 1, and the demo disk in drive 0.
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
              -load-fd0 build/jukebox.dsk -kbd-translate

include ../../make/demo.mk

# In coco the jukebox is a demo with one song, the Badinerie (#627).
# The Bach library on four disks (#620) lives in ~/github/coco-fm-bach.
# The player goes first, so a CoCo Zero with autorun on (it runs drive
# 0's first program) boots straight into it (#594). Copy more .SNG
# files onto the disk, or into drives 1 to 3, and it plays those too.
DISK = build/jukebox.dsk
all: $(DISK)
run: $(DISK)

DECB = $(shell command -v decb || echo ~/bin/decb)
SNG  = ../sound-sn76489

$(SNG)/build/badinerie.snd: $(SNG)/badinerie.py $(SNG)/badinerie.perf $(SNG)/sn76489.py $(SNG)/midi2sng.py
	$(MAKE) -C $(SNG) build/badinerie.snd

# BADINERI.VGM is the opening of the same song as a VGM file (#598),
# logged for a 3.58 MHz chip, to test lib/vgm.fs against the .SNG.
$(SNG)/build/badinerie.vgm: $(SNG)/build/badinerie.snd $(SNG)/sng2vgm.py
	$(MAKE) -C $(SNG) build/badinerie.vgm

$(DISK): $(BIN) $(SNG)/build/badinerie.snd $(SNG)/build/badinerie.vgm
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,JUKEBOX.BIN -2 -b
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
	$(DECB) copy $(SNG)/build/badinerie.vgm $@,BADINERI.VGM -2 -b

# The same on a CoCo 3, whose GIME palette the player programs (#603).
# The theme colours are RGB values, so use the RGB monitor input.
run3: $(BIN) $(DISK)
	xroar -machine coco3 -tv-input rgb \
	    -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
	    -load-fd0 $(DISK) -kbd-translate -run $(BIN)
.PHONY: run3
