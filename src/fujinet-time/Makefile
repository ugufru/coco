KERNEL_DIR  = ../../forth/kernel
KERNEL_MAP  = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN  = $(KERNEL_DIR)/build/kernel.bin
FC          = python3 ../../forth/tools/fc.py
XROAR_ROMS  = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

all: fujinet-time.bin

fujinet-time.bin: fujinet-time.fs ../../forth/lib/fujinet.fs $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) fujinet-time.fs \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    fujinet-time.bin

run: fujinet-time.bin
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) -kbd-translate -run fujinet-time.bin

kernel:
	$(MAKE) -C $(KERNEL_DIR)

clean:
	rm -rf build fujinet-time.bin

.PHONY: all run kernel clean
