BUILD  = build
TOOLS  = ../tools
HELLO  = ../hello

.PHONY: all clean run rom

all: $(BUILD)/combined.bin

$(BUILD)/kernel.bin: kernel.asm | $(BUILD)
	lwasm --format=decb --output=$(BUILD)/kernel.bin --map=$(BUILD)/kernel.map --list=$(BUILD)/kernel.lst kernel.asm

# ROM-mode kernel: kernel ORGs at $1000, no all-RAM dance, BASIC stays alive.
# Defaults target a 32K machine (RSP_INIT=$8000, DSP_INIT=$7E00).
# Override on the command line for 16K builds.
rom: $(BUILD)/kernel-rom.bin

$(BUILD)/kernel-rom.bin: kernel.asm | $(BUILD)
	lwasm --format=decb --output=$(BUILD)/kernel-rom.bin --map=$(BUILD)/kernel-rom.map --list=$(BUILD)/kernel-rom.lst -DROM_MODE=1 kernel.asm

$(BUILD)/combined.bin: $(HELLO)/hello.fs $(BUILD)/kernel.bin
	python3 $(TOOLS)/fc.py $(HELLO)/hello.fs \
	    --kernel    $(BUILD)/kernel.map \
	    --kernel-bin $(BUILD)/kernel.bin \
	    --output    $(BUILD)/combined.bin

$(BUILD):
	mkdir -p $(BUILD)

run: $(BUILD)/combined.bin
	xroar -machine coco2bus -ram 64 \
	    -bas ~/.xroar/roms/bas12.rom \
	    -extbas ~/.xroar/roms/extbas11.rom \
	    -run $(BUILD)/combined.bin

clean:
	rm -rf $(BUILD)
