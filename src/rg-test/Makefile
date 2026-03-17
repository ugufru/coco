KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: rg-test.bin

rg-test.bin: rg-test.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) rg-test.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    rg-test.bin

run: rg-test.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -tv-input cmp-br -kbd-translate -run rg-test.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

.PHONY: all run kernel
