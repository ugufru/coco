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

# Every MIDI import from $(SNG)/midi (#595), plus the hand-made Badinerie.
SONGS = $(patsubst $(SNG)/midi/%.mid,$(SNG)/build/songs/%.sng,$(wildcard $(SNG)/midi/*.mid))

$(SONGS):
	$(MAKE) -C $(SNG) songs

build/songs.dsk: $(BIN) $(SNG)/build/badinerie.snd $(SONGS)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,JUKEBOX.BIN -2 -b
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
	for f in $(SONGS); do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done
