NAME        = jukebox
EXTRA_DEPS  = ../../lib/screen.fs ../../lib/bye.fs ../../lib/vdg.fs ../../lib/cg-tiny.fs ../../lib/sn76489.fs ../../lib/decb.fs
# A Multi-Pak with the disk controller in slot 0 and the GameMaster in
# slot 1, and the four song disks in drives 0 to 3.
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
              -load-fd0 build/bach0.dsk -load-fd1 build/bach1.dsk \
              -load-fd2 build/bach2.dsk -load-fd3 build/bach3.dsk -kbd-translate

include ../../make/demo.mk

# Each disk is named for its drive (#625). BACH0 carries the player
# too, first, so a CoCo Zero with autorun on (it runs drive 0's first
# program) boots straight into the menu (#594).
DISKS = build/bach0.dsk build/bach1.dsk build/bach2.dsk build/bach3.dsk
all: $(DISKS)
run: $(DISKS)

DECB = $(shell command -v decb || echo ~/bin/decb)
SNG  = ../sound-sn76489

$(SNG)/build/badinerie.snd: $(SNG)/badinerie.py $(SNG)/badinerie.perf $(SNG)/sn76489.py $(SNG)/midi2sng.py
	$(MAKE) -C $(SNG) build/badinerie.snd

# Every MIDI import from $(SNG)/midi (#595), plus the hand-made Badinerie.
# They no longer fit one disk, so the fugues (#611) go on a second disk
# for drive 1 (#612); the jukebox plays every drive that has a disk.
SONGS  = $(patsubst $(SNG)/midi/%.mid,$(SNG)/build/songs/%.sng,$(wildcard $(SNG)/midi/*.mid))
# BACH2 and BACH3, for drives 2 and 3 (#620), in disk order.
# The WTC preludes and the organ trio sonata join the fugues on drive 1,
# and the Duetti join the Inventions on drive 0, so every disk fits.
BACH2  = TOCCATA SHEEP MINUET1 MINUET2 MUSETTE POLONAIS PRELU999 \
         BOURE996 SICILIAN INDULCI LPRELUD2 INVENT02 INVENT03 \
         INVENT05 INVENT07 INVENT11 INVENT14 INVENT15 FS5COURA \
         FS5SARAB FS5GAVOT FS5BOURR FS5GIGUE ES1BOURR ES2BOURR \
         ES2GIGUE
BACH3  = CS1PREL CS1COUR CS1MENU1 CS1MENU2 CS1GIGUE CS3PREL CS3BOURR \
         CS3GIGUE FLUTEALL FLUTEBOU VPBOREA LUTEPREL LUTEGIGU \
         LUTEDOUB P1PRELUD P3FANTAS P5MINUET P5PASSEP P5GIGUE BWV646 \
         BWV454 BWV462 ARIA515 ARIA516
DISK2  = $(patsubst %,$(SNG)/build/songs/%.sng,$(BACH2))
DISK3  = $(patsubst %,$(SNG)/build/songs/%.sng,$(BACH3))
DISK1  = $(filter-out $(DISK2) $(DISK3),$(filter $(SNG)/build/songs/WTC% $(SNG)/build/songs/FUGUE% $(SNG)/build/songs/AOF% $(SNG)/build/songs/TRIO%,$(SONGS)))
DISK0  = $(filter-out $(DISK1) $(DISK2) $(DISK3),$(SONGS))

$(SONGS):
	$(MAKE) -C $(SNG) songs

build/bach0.dsk: $(BIN) $(SNG)/build/badinerie.snd $(DISK0)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,JUKEBOX.BIN -2 -b
	$(DECB) copy $(SNG)/build/badinerie.snd $@,BADINERI.SNG -2 -b
	for f in $(DISK0); do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done

build/bach1.dsk: $(DISK1)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	for f in $(DISK1); do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done

build/bach2.dsk: $(DISK2)
build/bach3.dsk: $(DISK3)
build/bach2.dsk build/bach3.dsk:
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	for f in $^; do \
	    $(DECB) copy $$f $@,$$(basename $$f | tr a-z A-Z) -2 -b || exit 1; \
	done

# The same on a CoCo 3, whose GIME palette the player programs (#603).
# The theme colours are RGB values, so use the RGB monitor input.
run3: $(BIN) $(DISKS)
	xroar -machine coco3 -tv-input rgb \
	    -cart mpi -mpi-load-cart 0=rsdos -mpi-load-cart 1=gmc \
	    -load-fd0 build/bach0.dsk -load-fd1 build/bach1.dsk \
	    -load-fd2 build/bach2.dsk -load-fd3 build/bach3.dsk -kbd-translate -run $(BIN)
.PHONY: run3
