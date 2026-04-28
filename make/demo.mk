# Shared rules for src/<demo>/Makefile.
#
# A demo Makefile sets:
#   NAME         (required)  binary stem, e.g. bounce -> bounce.bin
#   SRC          (optional)  source .fs file, defaults to $(NAME).fs
#   EXTRA_DEPS   (optional)  extra prerequisites for the .bin (libs, etc.)
#   XROAR_EXTRA  (optional)  extra xroar args, defaults to -kbd-translate
# then includes this file.

KERNEL_DIR   = ../../forth/kernel
FC           = python3 ../../forth/tools/fc.py
XROAR_ROMS   = -bas ~/.xroar/roms/bas12.rom -extbas ~/.xroar/roms/extbas11.rom

# KERNEL_VARIANT selects which kernel build to link against:
#   (unset)  = ROM-mode kernel at $1000 (default, BASIC ROMs alive, fits 32K)
#   allram   = all-RAM kernel at $E000 (requires 64K, BASIC ROMs paged out)
KERNEL_VARIANT ?=
ifeq ($(KERNEL_VARIANT),allram)
KERNEL_STEM   = kernel-allram
KERNEL_TARGET = allram
XROAR_RAM    ?= 64
else
KERNEL_STEM   = kernel
KERNEL_TARGET =
XROAR_RAM    ?= 32
endif
KERNEL_MAP   = $(KERNEL_DIR)/build/$(KERNEL_STEM).map
KERNEL_BIN   = $(KERNEL_DIR)/build/$(KERNEL_STEM).bin

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

$(KERNEL_MAP) $(KERNEL_BIN):
	$(MAKE) -C $(KERNEL_DIR) $(KERNEL_TARGET)

run: $(BIN)
	xroar -machine coco2bus -ram $(XROAR_RAM) $(XROAR_ROMS) $(XROAR_EXTRA) -run $(BIN)

kernel:
	$(MAKE) -C $(KERNEL_DIR) $(KERNEL_TARGET)

clean:
	rm -rf build $(BIN)

.PHONY: all run kernel clean
