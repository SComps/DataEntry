#!/usr/bin/env bash
# publish-macos-x64.sh — Build a self-contained single-file release for macOS x64.
# Run this on a macOS x64 (Intel) machine.
#
# Usage:
#   ./publish-macos-x64.sh                # publishes to ./publish/osx-x64/
#   ./publish-macos-x64.sh ./my/output    # publishes to ./my/output/
#
# Package contents:
#   dataentry                       - the compiler (no .NET install required on target)
#   libonigwrap.dylib               - native regex helper (must live beside dataentry)
#   Samples/*.def                   - sample form definitions
#   MANUAL.md                       - user manual
#   sample.def                      - quick-start example
#   DataEntry-osx-x64.tar.gz        - tarball of the above, ready to distribute

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/DataEntry/DataEntry.vbproj"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish/osx-x64}"

echo ""
echo "========================================================"
echo "  DataEntry  --  macOS x64 publish"
echo "  Output : $OUTPUT_DIR"
echo "========================================================"
echo ""

echo "Cleaning previous output..."
rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

echo "Publishing..."
dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime osx-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=none \
  -p:DebugSymbols=false \
  --output "$OUTPUT_DIR"

echo "Copying docs..."
[ -f "$SCRIPT_DIR/MANUAL.md"  ] && cp "$SCRIPT_DIR/MANUAL.md"  "$OUTPUT_DIR/MANUAL.md"
[ -f "$SCRIPT_DIR/INSTALL.md" ] && cp "$SCRIPT_DIR/INSTALL.md" "$OUTPUT_DIR/INSTALL.md"

chmod +x "$OUTPUT_DIR/dataentry" 2>/dev/null || chmod +x "$OUTPUT_DIR/DataEntry" 2>/dev/null || true

ARCHIVE="$SCRIPT_DIR/publish/DataEntry-osx-x64.tar.gz"
echo "Creating archive: $ARCHIVE"
mkdir -p "$SCRIPT_DIR/publish"
tar -czf "$ARCHIVE" -C "$(dirname "$OUTPUT_DIR")" "$(basename "$OUTPUT_DIR")"

echo ""
echo "Done."
echo "  Executable  : $OUTPUT_DIR/dataentry"
echo "  Runtime dep : $OUTPUT_DIR/libonigwrap.dylib"
echo "  Archive     : $ARCHIVE"
echo ""
