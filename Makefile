# Makefile — build all CoCo demos and create a .DSK disk image
#
# Targets:
#   make          build kernel + all demo binaries
#   make dsk      build everything, then package into build/demos.dsk
#   make clean    remove all build artifacts

DEMOS    = bounce calculator kaleidoscope rain tetris rg-test typewriter
KERNEL   = forth/kernel
DSK      = build/demos.dsk

.PHONY: all kernel demos dsk clean

all: demos

kernel:
	$(MAKE) -C $(KERNEL)

demos: kernel
	@for d in $(DEMOS); do $(MAKE) -C src/$$d || exit 1; done

# Create a DECB disk image with all demos.
# Requires Toolshed's 'decb' command.
dsk: demos
	@command -v decb >/dev/null 2>&1 || { \
	    echo ""; \
	    echo "  Error: 'decb' not found on PATH."; \
	    echo ""; \
	    echo "  Install Toolshed: https://github.com/boisy/toolshed"; \
	    echo "    macOS:  brew install toolshed"; \
	    echo "    source: clone repo, make, add bin/ to PATH"; \
	    echo ""; \
	    exit 1; \
	}
	@mkdir -p build
	decb dskini $(DSK)
	decb copy src/bounce/bounce.bin               $(DSK),BOUNCE.BIN -2
	decb copy src/calculator/calc.bin             $(DSK),CALC.BIN -2
	decb copy src/kaleidoscope/kaleidoscope.bin    $(DSK),KALEIDSC.BIN -2
	decb copy src/rain/rain.bin                    $(DSK),RAIN.BIN -2
	decb copy src/tetris/tetris.bin                $(DSK),TETRIS.BIN -2
	decb copy src/rg-test/rg-test.bin              $(DSK),RG-TEST.BIN -2
	decb copy src/typewriter/typewriter.bin        $(DSK),TYPEWRTR.BIN -2
	@echo ""
	@echo "  $(DSK) created with 7 programs."
	@echo "  Copy to SD card for FujiNet, or load in XRoar."
	@echo "  In DECB:  LOADM\"BOUNCE\":EXEC"

clean:
	@for d in $(DEMOS); do rm -f src/$$d/$$d.bin; done
	rm -f $(DSK)
	$(MAKE) -C $(KERNEL) clean
