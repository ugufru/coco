KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

LIBS = ../../forth/lib/vdg.fs \
       ../../forth/lib/screen.fs \
       ../../forth/lib/rg-pixel.fs \
       ../../forth/lib/datawrite.fs \
       ../../forth/lib/font-art.fs \
       ../../forth/lib/trig.fs \
       ../../forth/lib/fujinet.fs

all: clock.bin

clock.bin: clock.fs $(LIBS) $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) clock.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    clock.bin

run: clock.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -cart-rom ~/.xroar/roms/hdbdw3cc2.rom -cart-autorun -kbd-translate -run clock.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

clean:
	rm -rf build clock.bin

.PHONY: all run kernel clean
