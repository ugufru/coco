BUILD  = build
TOOLS  = ../tools
HELLO  = ../hello

.PHONY: all clean run

all: $(BUILD)/combined.bin

$(BUILD)/kernel.bin: kernel.asm | $(BUILD)
	lwasm --format=decb --output=$(BUILD)/kernel.bin --map=$(BUILD)/kernel.map --list=$(BUILD)/kernel.lst kernel.asm

$(BUILD)/combined.bin: $(HELLO)/hello.fs $(BUILD)/kernel.bin
	python3 $(TOOLS)/fc.py $(HELLO)/hello.fs \
	    --kernel    $(BUILD)/kernel.map \
	    --kernel-bin $(BUILD)/kernel.bin \
	    --output    $(BUILD)/combined.bin

$(BUILD):
	mkdir -p $(BUILD)

run: $(BUILD)/combined.bin
	xroar -machine coco2bus -bas bas12.rom -extbas extbas11.rom \
	    -run $(BUILD)/combined.bin

clean:
	rm -rf $(BUILD)
