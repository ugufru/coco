NAME        = badinerie
EXTRA_DEPS  = build/badinerie.snd ../../lib/screen.fs ../../lib/bye.fs ../../lib/sn76489.fs
XROAR_EXTRA = -cart gmc -kbd-translate

include ../../make/demo.mk

build/badinerie.snd: badinerie.py badinerie.perf sn76489.py midi2sng.py
	python3 badinerie.py $@

# The Badinerie's first 20 seconds as a VGM file, test material for
# lib/vgm.fs (#598). It loops once, so the jukebox plays 40 seconds.
build/badinerie.vgm: build/badinerie.snd sng2vgm.py
	python3 sng2vgm.py build/badinerie.snd $@ --seconds 20

# The MIDI imports and the Bach library that grew from them (#595,
# #620) live in their own project now, ~/github/coco-fm-bach (#627).
# midi2sng.py stays here: the Badinerie uses its performance layer, and
# it still converts a single file (python3 midi2sng.py in.mid out.sng).
