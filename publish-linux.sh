#!/usr/bin/env bash
# publish-linux.sh — AOT self-contained publish for Linux (x64).
#
# Usage:
#   ./publish-linux.sh              # publishes to ./publish/linux-x64/
#   ./publish-linux.sh ./my/output  # publishes to ./my/output/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/DataEntry/DataEntry.vbproj"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish/linux-x64}"

echo "Publishing for Linux x64 to: $OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained true \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  --output "$OUTPUT_DIR"

echo "Done."
echo ""
echo "Published files:"
find "$OUTPUT_DIR" -maxdepth 1 -type f -executable | head -5
