KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: sound.bin

sound.bin: sound.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) sound.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    sound.bin

run: sound.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -kbd-translate -run sound.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

.PHONY: all run kernel
