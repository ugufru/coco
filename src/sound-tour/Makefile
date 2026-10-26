NAME        = sound-tour
EXTRA_DEPS  = tour.py ../../lib/screen.fs ../../lib/bye.fs ../../lib/sound.fs \
              ../../lib/sn76489.fs ../../lib/ay8910.fs ../../lib/wavetable.fs \
              ../../lib/orch90.fs
# XRoar has the GameMaster and the Orchestra-90, not the CoCo PSG or the
# Symphony-12 (those play on the CoCo Zero: make dsk).
XROAR_EXTRA = -cart mpi -mpi-load-cart 0=gmc -mpi-load-cart 1=orch90 \
              -mpi-slot 0 -kbd-translate

include ../../make/demo.mk

# A disk for the CoCo Zero: the tour alone, first, so autorun starts it.
DECB = $(shell command -v decb || echo ~/bin/decb)
DISK = build/sound-tour.dsk
dsk: $(DISK)
$(DISK): $(BIN)
	mkdir -p build
	rm -f $@
	$(DECB) dskini $@
	$(DECB) copy $(BIN) $@,SOUNDTUR.BIN -2 -b
.PHONY: dsk
