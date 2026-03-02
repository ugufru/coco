#!/usr/bin/env bash
#
# run.sh — compile a Forth source file and launch it in XRoar
#
# Usage:
#   ./run.sh <source.fs>
#
# The compiled binary is written alongside the source file (.bin).
# The kernel is rebuilt automatically whenever kernel.asm changes.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
KERNEL_MAP="$SCRIPT_DIR/kernel/build/kernel.map"
KERNEL_BIN="$SCRIPT_DIR/kernel/build/kernel.bin"

if [ $# -ne 1 ]; then
    echo "Usage: $0 <source.fs>"
    exit 1
fi

SRC="$1"
OUT="${SRC%.fs}.bin"

# Build kernel (make skips if already up to date)
(cd "$SCRIPT_DIR/kernel" && make)

# Compile Forth source → DECB binary
python3 "$SCRIPT_DIR/tools/fc.py" "$SRC" \
    --kernel     "$KERNEL_MAP" \
    --kernel-bin "$KERNEL_BIN" \
    --output     "$OUT"

# Launch in XRoar
xroar \
    -machine coco2bus \
    -bas     ~/.xroar/roms/bas12.rom \
    -extbas  ~/.xroar/roms/extbas11.rom \
    -run     "$OUT"
