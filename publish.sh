#!/usr/bin/env bash
# publish.sh — AOT self-contained publish for the current machine.
#
# Usage:
#   ./publish.sh              # publishes to ./publish/
#   ./publish.sh ./my/output  # publishes to ./my/output/

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$SCRIPT_DIR/src/DataEntry/DataEntry.vbproj"
OUTPUT_DIR="${1:-$SCRIPT_DIR/publish}"

echo "Publishing to: $OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --self-contained true \
  -p:PublishAot=true \
  -p:StripSymbols=true \
  --output "$OUTPUT_DIR"

echo "Done."
find "$OUTPUT_DIR" -maxdepth 1 -type f -executable | head -5
