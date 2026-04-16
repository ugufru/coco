KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: calc.bin

calc.bin: calc.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) calc.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    calc.bin

run: calc.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -tv-type ntsc -tv-input cmp-br -run calc.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

.PHONY: all run kernel
