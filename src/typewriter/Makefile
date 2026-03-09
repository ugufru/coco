KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: typewriter.bin

typewriter.bin: typewriter.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) typewriter.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    typewriter.bin

run: typewriter.bin
	xroar -machine coco2bus $(XROAR_ROMS) -kbd-translate -run typewriter.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

.PHONY: all run kernel
