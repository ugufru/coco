NAME = terminal
EXTRA_DEPS = ../../lib/console.fs ../../lib/becker.fs
# RS-DOS cart with the Becker port enabled (hdbdw3bck.rom not needed).
XROAR_EXTRA = -kbd-translate -cart rsdos -cart-becker

include ../../make/demo.mk

# Start this first: XRoar connects to the bridge once, at start-up.
bridge:
	python3 ../../tools/cocoterm_bridge.py --cwd $(abspath ../..)

.PHONY: bridge
