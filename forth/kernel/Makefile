BUILD  = build
TOOLS  = ../tools
HELLO  = ../hello

.PHONY: all clean run allram

all: $(BUILD)/combined.bin

# Default kernel build: ROM mode (kernel at $1000, BASIC ROMs alive).
# Targets a 32K machine (RSP_INIT=$8000, DSP_INIT=$7E00). Override on
# the command line for 16K builds.
$(BUILD)/kernel.bin: kernel.asm | $(BUILD)
	lwasm --format=decb --output=$(BUILD)/kernel.bin --map=$(BUILD)/kernel.map --list=$(BUILD)/kernel.lst kernel.asm

# All-RAM kernel: kernel ORGs at $E000, requires 64K and pages out
# the BASIC ROMs. Used by apps that need the full 64K (e.g. spacewarp).
allram: $(BUILD)/kernel-allram.bin

$(BUILD)/kernel-allram.bin: kernel.asm | $(BUILD)
	lwasm --format=decb --output=$(BUILD)/kernel-allram.bin --map=$(BUILD)/kernel-allram.map --list=$(BUILD)/kernel-allram.lst -DALL_RAM=1 kernel.asm

$(BUILD)/combined.bin: $(HELLO)/hello.fs $(BUILD)/kernel.bin
	python3 $(TOOLS)/fc.py $(HELLO)/hello.fs \
	    --kernel    $(BUILD)/kernel.map \
	    --kernel-bin $(BUILD)/kernel.bin \
	    --output    $(BUILD)/combined.bin

$(BUILD):
	mkdir -p $(BUILD)

run: $(BUILD)/combined.bin
	xroar -machine coco2bus -ram 32 \
	    -bas ~/.xroar/roms/bas12.rom \
	    -extbas ~/.xroar/roms/extbas11.rom \
	    -run $(BUILD)/combined.bin

clean:
	rm -rf $(BUILD)
