NAME        = jukebox
EXTRA_DEPS  = ../../lib/screen.fs ../../lib/bye.fs ../../lib/vdg.fs ../../lib/cg-tiny.fs ../../lib/sn76489.fs ../../lib/decb.fs
# A Multi-Pak with the disk controller in slot 0 and the GameMaster in
# slot 1, and the song disk in drive 0.
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
              -load-fd0 build/songs.dsk -load-fd1 build/fugues.dsk -kbd-translate

include ../../make/demo.mk

# The disk carries the player too, first, so a CoCo Zero with autorun on
# (it runs drive 0's first program) boots straight into the menu (#594).
all: build/songs.dsk build/fugues.dsk
run: build/songs.dsk build/fugues.dsk

DECB = $(shell command -v decb || echo ~/bin/decb)
SNG  = ../sound-sn76489

$(SNG)/build/badinerie.snd: $(SNG)/badinerie.py $(SNG)/badinerie.perf $(SNG)/sn76489.py $(SNG)/midi2sng.py
	$(MAKE) -C $(SNG) build/badinerie.snd

# Every MIDI import from $(SNG)/midi (#595), plus the hand-made Badinerie.
# They no longer fit one disk, so the fugues (#611) go on a second disk
# for drive 1 (#612); the jukebox plays every drive that has a disk.
SONGS  = $(patsubst $(SNG)/midi/%.mid,$(SNG)/build/songs/%.sng,$(wildcard $(SNG)/midi/*.mid))
FUGUES = $(filter $(SNG)/build/songs/WTC% $(SNG)/build/songs/FUGUE% $(SNG)/build/songs/AOF%,$(SONGS))
DISK0  = $(filter-out $(FUGUES),$(SONGS))

$(SONGS):
	$(MAKE) -C $(SNG) songs

build/songs.dsk: $(BIN) $(SNG)/build/badinerie.snd $(DISK0)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,JUKEBOX.BIN -2 -b
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
	for f in $(DISK0); do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done

build/fugues.dsk: $(FUGUES)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	for f in $(FUGUES); do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done

# The same on a CoCo 3, whose GIME palette the player programs (#603).
# The theme colours are RGB values, so use the RGB monitor input.
run3: $(BIN) build/songs.dsk build/fugues.dsk
	xroar -machine coco3 -tv-input rgb \
	    -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
	    -load-fd0 build/songs.dsk -load-fd1 build/fugues.dsk -kbd-translate -run $(BIN)
.PHONY: run3
