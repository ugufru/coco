NAME        = badinerie
EXTRA_DEPS  = build/badinerie.snd ../../lib/screen.fs ../../lib/bye.fs ../../lib/sn76489.fs
XROAR_EXTRA = -cart gmc -kbd-translate

include ../../make/demo.mk

build/badinerie.snd: badinerie.py sn76489.py
	python3 badinerie.py $@
