NAME        = badinerie
EXTRA_DEPS  = build/badinerie.snd ../../lib/screen.fs ../../lib/bye.fs ../../lib/sn76489.fs
XROAR_EXTRA = -cart gmc -kbd-translate

include ../../make/demo.mk

build/badinerie.snd: badinerie.py badinerie.perf sn76489.py midi2sng.py
	python3 badinerie.py $@

# The MIDI imports and the Bach library that grew from them (#595,
# #620) live in their own project now, ~/github/coco-fm-bach (#627).
# midi2sng.py stays here: the Badinerie uses its performance layer, and
# it still converts a single file (python3 midi2sng.py in.mid out.sng).
