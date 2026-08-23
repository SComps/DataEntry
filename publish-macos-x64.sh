#!/usr/bin/env bash
# publish-macos-x64.sh — AOT self-contained publish for macOS (Intel x64).
#
# Usage:
#   ./publish-macos-x64.sh              # publishes to ./publish/osx-x64/
#   ./publish-macos-x64.sh ./my/output  # publishes to ./my/output/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/DataEntry/DataEntry.vbproj"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish/osx-x64}"

echo "Publishing for macOS x64 to: $OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime osx-x64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  --output "$OUTPUT_DIR"

echo "Done."
echo ""
echo "Published files:"
find "$OUTPUT_DIR" -maxdepth 1 -type f -perm +111 | head -5
