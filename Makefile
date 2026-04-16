# Makefile — build all CoCo demos and create .DSK disk images
#
# Targets:
#   make              build kernel + all demo binaries
#   make dsk          build demos DSK (build/demos.dsk)
#   make tutorial-dsk build tutorial examples DSK (build/tutorial.dsk)
#   make dsks         build both DSKs
#   make clean        remove all build artifacts

DEMOS        = bounce calculator kaleidoscope rain tetris rg-test typewriter
KERNEL       = forth/kernel
DSK          = build/demos.dsk
TUTORIAL_DSK = build/tutorial.dsk

.PHONY: all kernel demos dsk tutorial-dsk dsks clean

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

# Create a DECB disk image with the 12 tutorial example programs.
# Uses the pre-built .bin files committed under docs/examples/.
# Requires Toolshed's 'decb' command.
tutorial-dsk:
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
	decb dskini $(TUTORIAL_DSK)
	decb copy docs/examples/01/hello.bin    $(TUTORIAL_DSK),HELLO.BIN -2
	decb copy docs/examples/02/title.bin    $(TUTORIAL_DSK),TITLE.BIN -2
	decb copy docs/examples/03/letter.bin   $(TUTORIAL_DSK),LETTER.BIN -2
	decb copy docs/examples/04/mirror.bin   $(TUTORIAL_DSK),MIRROR.BIN -2
	decb copy docs/examples/05/hi.bin       $(TUTORIAL_DSK),HI.BIN -2
	decb copy docs/examples/06/alpha.bin    $(TUTORIAL_DSK),ALPHA.BIN -2
	decb copy docs/examples/07/grade.bin    $(TUTORIAL_DSK),GRADE.BIN -2
	decb copy docs/examples/08/yorn.bin     $(TUTORIAL_DSK),YORN.BIN -2
	decb copy docs/examples/09/arith.bin    $(TUTORIAL_DSK),ARITH.BIN -2
	decb copy docs/examples/10/calc.bin     $(TUTORIAL_DSK),CALC.BIN -2
	decb copy docs/examples/11/screen.bin   $(TUTORIAL_DSK),SCREEN.BIN -2
	decb copy docs/examples/12/guess.bin    $(TUTORIAL_DSK),GUESS.BIN -2
	@echo ""
	@echo "  $(TUTORIAL_DSK) created with 12 programs (tutorial chapters 1-12)."
	@echo "  Copy to SD card for FujiNet, or load in XRoar."
	@echo "  In DECB:  LOADM\"HELLO\":EXEC"

dsks: dsk tutorial-dsk

clean:
	@for d in $(DEMOS); do rm -f src/$$d/$$d.bin; done
	rm -f $(DSK) $(TUTORIAL_DSK)
	$(MAKE) -C $(KERNEL) clean
