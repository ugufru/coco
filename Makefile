# Makefile — build all CoCo demos and create .DSK disk images
#
# Targets:
#   make              build kernel + all demo binaries
#   make dsk          build demos DSK (build/demos.dsk)
#   make tutorial-dsk build tutorial examples DSK (build/tutorial.dsk)
#   make dsks         build both DSKs
#   make site         build the static documentation site (site/)
#   make clean        remove all build artifacts

DEMOS        = hello bounce calculator kaleidoscope rain tetris rg-test typewriter vdg-modes clock fujinet-time colored-text \
               sound sound1bit sound-async sound-sn76489 sound-tour jukebox terminal
KERNEL       = kernel
DSK          = build/demos.dsk
SOUND_DSK    = build/sound.dsk
TUTORIAL_DSK = build/tutorial.dsk
FNTIME_DSK   = build/fntime.dsk
CLOCK_DSK    = build/clock.dsk

.PHONY: issues all kernel demos dsk sound-dsk tutorial-dsk dsks fntime-dsk clock-dsk lib-readme site clean

all: lib-readme demos

kernel:
	$(MAKE) -C $(KERNEL)

demos: kernel
	@for d in $(DEMOS); do $(MAKE) -C src/$$d bin || exit 1; done

# Regenerate lib/README.md from lib/*.fs source headers and definitions.
# Auto-runs as part of `make all`. Run by hand with `make lib-readme`.
# build/lib-metrics.json supplies per-word byte sizes and cycle costs.
lib-readme: lib/README.md

lib/README.md: tools/gen-lib-readme.py build/lib-metrics.json reference.html $(wildcard lib/*.fs)
	@python3 tools/gen-lib-readme.py

build/lib-metrics.json: tools/gen-lib-metrics.py tools/fc.py kernel/build/kernel.map $(wildcard lib/*.fs)
	@python3 tools/gen-lib-metrics.py

kernel/build/kernel.map:
	$(MAKE) -C $(KERNEL)

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
	decb copy src/hello/hello.bin                  $(DSK),HELLO.BIN -2
	decb copy src/bounce/bounce.bin               $(DSK),BOUNCE.BIN -2
	decb copy src/calculator/calc.bin             $(DSK),CALC.BIN -2
	decb copy src/kaleidoscope/kaleidoscope.bin    $(DSK),KALEIDSC.BIN -2
	decb copy src/rain/rain.bin                    $(DSK),RAIN.BIN -2
	decb copy src/tetris/tetris.bin                $(DSK),TETRIS.BIN -2
	decb copy src/rg-test/rg-test.bin              $(DSK),RG-TEST.BIN -2
	decb copy src/typewriter/typewriter.bin        $(DSK),TYPEWRTR.BIN -2
	decb copy src/vdg-modes/vdg-modes.bin          $(DSK),VDGMODES.BIN -2
	decb copy src/clock/clock.bin                  $(DSK),CLOCK.BIN -2
	decb copy src/fujinet-time/fujinet-time.bin    $(DSK),FNTIME.BIN -2
	decb copy src/sound/sound.bin                  $(DSK),SOUND.BIN -2
	decb copy src/sound1bit/sound1bit.bin          $(DSK),SOUND1B.BIN -2
	decb copy src/sound-async/sound-async.bin      $(DSK),SNDASYNC.BIN -2
	decb copy src/colored-text/colored-text.bin    $(DSK),COLORTXT.BIN -2
	decb copy src/terminal/terminal.bin            $(DSK),TERMINAL.BIN -2
	@echo ""
	@echo "  $(DSK) created with 16 programs."
	@echo "  Copy to SD card for FujiNet, or load in XRoar."
	@echo "  In DECB:  LOADM\"BOUNCE\":EXEC"

# Create a DECB disk image with the 12 tutorial example programs.
# Uses the pre-built .bin files committed under tutorials/.
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
	decb copy tutorials/01-meet-your-stack/examples/hello.bin    $(TUTORIAL_DSK),HELLO.BIN -2
	decb copy tutorials/02-say-something/examples/title.bin    $(TUTORIAL_DSK),TITLE.BIN -2
	decb copy tutorials/03-make-your-own-words/examples/letter.bin   $(TUTORIAL_DSK),LETTER.BIN -2
	decb copy tutorials/04-stack-is-your-friend/examples/mirror.bin   $(TUTORIAL_DSK),MIRROR.BIN -2
	decb copy tutorials/05-remember-things/examples/hi.bin       $(TUTORIAL_DSK),HI.BIN -2
	decb copy tutorials/06-count-and-loop/examples/alpha.bin    $(TUTORIAL_DSK),ALPHA.BIN -2
	decb copy tutorials/07-decisions/examples/grade.bin    $(TUTORIAL_DSK),GRADE.BIN -2
	decb copy tutorials/08-read-the-keyboard/examples/yorn.bin     $(TUTORIAL_DSK),YORN.BIN -2
	decb copy tutorials/09-numbers/examples/arith.bin    $(TUTORIAL_DSK),ARITH.BIN -2
	decb copy tutorials/10-calculator/examples/calc.bin     $(TUTORIAL_DSK),CALC.BIN -2
	decb copy tutorials/11-anywhere-on-screen/examples/screen.bin   $(TUTORIAL_DSK),SCREEN.BIN -2
	decb copy tutorials/12-the-guessing-game/examples/guess.bin    $(TUTORIAL_DSK),GUESS.BIN -2
	@echo ""
	@echo "  $(TUTORIAL_DSK) created with 12 programs (tutorial chapters 1-12)."
	@echo "  Copy to SD card for FujiNet, or load in XRoar."
	@echo "  In DECB:  LOADM\"HELLO\":EXEC"

# The sound demos on a disk of their own (#640): with the cartridge chips
# and the jukebox's songs they no longer fit on demos.dsk, whose every
# program carries its own kernel. The sound tour goes first, so a CoCo
# Zero with autorun on starts it. The cartridge demos need their chips:
# a GameMaster (SN76489), an Orchestra-90, or a CoCo Zero for the AY.
sound-dsk: demos
	@command -v decb >/dev/null 2>&1 || { echo "  Error: 'decb' not found on PATH (install Toolshed)."; exit 1; }
	$(MAKE) -C src/sound-sn76489 build/badinerie.snd build/badinerie.vgm
	@mkdir -p build
	decb dskini $(SOUND_DSK)
	decb copy src/sound-tour/sound-tour.bin        $(SOUND_DSK),SOUNDTUR.BIN -2
	decb copy src/sound/sound.bin                  $(SOUND_DSK),SOUND.BIN -2
	decb copy src/sound1bit/sound1bit.bin          $(SOUND_DSK),SOUND1B.BIN -2
	decb copy src/sound-async/sound-async.bin      $(SOUND_DSK),SNDASYNC.BIN -2
	decb copy src/sound-sn76489/badinerie.bin      $(SOUND_DSK),BADINERI.BIN -2
	decb copy src/jukebox/jukebox.bin              $(SOUND_DSK),JUKEBOX.BIN -2
	decb copy src/sound-sn76489/build/badinerie.snd $(SOUND_DSK),BADINERI.SNG -2
	decb copy src/sound-sn76489/build/badinerie.vgm $(SOUND_DSK),BADINERI.VGM -2
	@echo ""
	@echo "  $(SOUND_DSK) created with 6 programs and the jukebox's two songs."
	@echo "  In DECB:  LOADM\"SOUNDTUR\":EXEC"

dsks: dsk sound-dsk tutorial-dsk

# Single-program DSK with the FujiNet RTC demo, for the FujiNet SD card.
# Builds the .bin via the demo's own Makefile, then wraps it in a DECB image.
fntime-dsk: kernel
	@command -v decb >/dev/null 2>&1 || { \
	    echo ""; \
	    echo "  Error: 'decb' not found on PATH."; \
	    echo "  Install Toolshed: https://github.com/boisy/toolshed"; \
	    exit 1; \
	}
	$(MAKE) -C src/fujinet-time
	@mkdir -p build
	decb dskini $(FNTIME_DSK)
	decb copy src/fujinet-time/fujinet-time.bin $(FNTIME_DSK),FNTIME.BIN -2
	@echo ""
	@echo "  $(FNTIME_DSK) created."
	@echo "  Copy to your FujiNet SD card."
	@echo "  In DECB:  LOADM\"FNTIME\":EXEC"

# Single-program DSK with the analog+digital clock demo.
clock-dsk: kernel
	@command -v decb >/dev/null 2>&1 || { \
	    echo ""; \
	    echo "  Error: 'decb' not found on PATH."; \
	    echo "  Install Toolshed: https://github.com/boisy/toolshed"; \
	    exit 1; \
	}
	$(MAKE) -C src/clock
	@mkdir -p build
	decb dskini $(CLOCK_DSK)
	decb copy src/clock/clock.bin $(CLOCK_DSK),CLOCK.BIN -2
	@echo ""
	@echo "  $(CLOCK_DSK) created."
	@echo "  Copy to your FujiNet SD card."
	@echo "  In DECB:  LOADM\"CLOCK\":EXEC"

# Build the static documentation site into site/ (issue #539).
# Requires the markdown parser: python3 -m pip install -r tools/requirements.txt
# Browsable view of issues.jsonl and roadmap.jsonl (#639). Commit the
# result: it is small, and it is what a clone opens.
issues: issues.html
issues.html: issues.jsonl roadmap.jsonl tools/issues_html.py
	python3 tools/issues_html.py render issues.jsonl issues.html roadmap.jsonl --title="CoCo Renovation issues"

site:
	@python3 tools/build-site.py

clean:
	@for d in $(DEMOS); do $(MAKE) -C src/$$d clean || exit 1; done
	rm -f $(DSK) $(SOUND_DSK) $(TUTORIAL_DSK)
	$(MAKE) -C $(KERNEL) clean
