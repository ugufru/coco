KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: spacewarp.bin

spacewarp.bin: spacewarp.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) spacewarp.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --base      0x1F00 \
	    --output    spacewarp.bin

run: spacewarp.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -tv-type ntsc -tv-input cmp-br -kbd-translate -kbd-bind KP_Enter=pre:enter -run spacewarp.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

sprtest.bin: sprtest.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) sprtest.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    sprtest.bin

sprtest: sprtest.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -tv-type ntsc -tv-input cmp-br -kbd-translate -kbd-bind KP_Enter=pre:enter -run sprtest.bin

.PHONY: all run kernel sprtest
