NAME        = badinerie
EXTRA_DEPS  = build/badinerie.snd ../../lib/screen.fs ../../lib/bye.fs ../../lib/sn76489.fs
XROAR_EXTRA = -cart gmc -kbd-translate

include ../../make/demo.mk

build/badinerie.snd: badinerie.py badinerie.perf sn76489.py midi2sng.py
	python3 badinerie.py $@

# MIDI imports (#595): midi/NAME.mid -> build/songs/NAME.sng
SONGS = $(patsubst midi/%.mid,build/songs/%.sng,$(wildcard midi/*.mid))
songs: $(SONGS)
# A song's performance file (midi/NAME.perf, #609) is optional, so every
# song depends on all of them.
PERFS = $(wildcard midi/*.perf)
build/songs/%.sng: midi/%.mid midi/TITLES.txt $(PERFS) midi2sng.py sn76489.py
	python3 midi2sng.py $< $@
.PHONY: songs
