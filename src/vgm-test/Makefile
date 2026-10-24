NAME        = vgm-test
EXTRA_DEPS  = ../sound-sn76489/build/badinerie.vgm ../../lib/vgm.fs ../../lib/sn76489.fs ../../lib/screen.fs ../../lib/bye.fs
XROAR_EXTRA = -cart gmc -kbd-translate

include ../../make/demo.mk

../sound-sn76489/build/badinerie.vgm:
	$(MAKE) -C ../sound-sn76489 build/badinerie.vgm

# What the screen should show: a Python model of lib/vgm.fs run on the
# same file (#598).
expect: ../sound-sn76489/build/badinerie.vgm
	python3 expect.py $< 600
.PHONY: expect
