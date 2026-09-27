#!/usr/bin/env bash
# Builds RestPartyPanel and packages it as a drop-in BepInEx zip.
# Unzip into the For The King II folder: it lands in BepInEx/plugins/ on its own.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="$(grep -oP '(?<=<Version>)[^<]+' "$PROJECT_DIR/RestPartyPanel/RestPartyPanel.csproj" | head -1)"
STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT

"$PROJECT_DIR/build.sh"

mkdir -p "$STAGE/BepInEx/plugins/RestPartyPanel"
cp "$PROJECT_DIR/RestPartyPanel/bin/Release/RestPartyPanel.dll" \
	"$STAGE/BepInEx/plugins/RestPartyPanel/RestPartyPanel.dll"

OUT_DIR="$PROJECT_DIR/outputs"
mkdir -p "$OUT_DIR"
ZIP="$OUT_DIR/RestPartyPanel-$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -qr9 "$ZIP" BepInEx)

echo
unzip -l "$ZIP"
echo
echo "packaged: $ZIP"
