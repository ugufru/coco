# Shared rules for src/<demo>/Makefile.
#
# A demo Makefile sets:
#   NAME         (required)  binary stem, e.g. bounce -> bounce.bin
#   SRC          (optional)  source .fs file, defaults to $(NAME).fs
#   EXTRA_DEPS   (optional)  extra prerequisites for the .bin (libs, etc.)
#   XROAR_EXTRA  (optional)  extra xroar args, defaults to -kbd-translate
# then includes this file.

KERNEL_DIR   = ../../forth/kernel
KERNEL_MAP   = $(KERNEL_DIR)/build/kernel.map
KERNEL_BIN   = $(KERNEL_DIR)/build/kernel.bin
FC           = python3 ../../forth/tools/fc.py
XROAR_ROMS   = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

SRC         ?= $(NAME).fs
XROAR_EXTRA ?= -kbd-translate

BIN = $(NAME).bin

all: $(BIN)

$(BIN): $(SRC) $(EXTRA_DEPS) $(KERNEL_MAP) $(KERNEL_BIN)
	mkdir -p build
	$(FC) $(SRC) \
	    --kernel    $(KERNEL_MAP) \
	    --kernel-bin $(KERNEL_BIN) \
	    --output    $(BIN)

run: $(BIN)
	xroar -machine coco2bus -ram 64 $(XROAR_ROMS) $(XROAR_EXTRA) -run $(BIN)

kernel:
	$(MAKE) -C $(KERNEL_DIR)

clean:
	rm -rf build $(BIN)

.PHONY: all run kernel clean
