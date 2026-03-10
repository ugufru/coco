KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: kaleidoscope.bin

kaleidoscope.bin: kaleidoscope.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) kaleidoscope.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    kaleidoscope.bin

run: kaleidoscope.bin
	xroar -machine coco2bus $(XROAR_ROMS) -kbd-translate -run kaleidoscope.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

.PHONY: all run kernel
