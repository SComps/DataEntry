#!/usr/bin/env bash
# publish-macos.sh — AOT self-contained publish for macOS (arm64).
#
# Usage:
#   ./publish-macos.sh              # publishes to ./publish/osx-arm64/
#   ./publish-macos.sh ./my/output  # publishes to ./my/output/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/DataEntry/DataEntry.vbproj"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish/osx-arm64}"

echo "Publishing for macOS arm64 to: $OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  --output "$OUTPUT_DIR"

echo "Done."
echo ""
echo "Published files:"
find "$OUTPUT_DIR" -maxdepth 1 -type f -perm +111 | head -5
