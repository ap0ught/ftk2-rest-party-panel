#!/usr/bin/env bash
# Builds every plugin and packages each as its own drop-in BepInEx zip.
# Unzip into the For The King II folder: it lands in BepInEx/plugins/ on its own.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="$PROJECT_DIR/outputs"

PLUGINS=(
	"RestPartyPanel"
	"FlockMemory"
)

"$PROJECT_DIR/build.sh"

mkdir -p "$OUT_DIR"
for plugin in "${PLUGINS[@]}"; do
	version="$(grep -oP '(?<=<Version>)[^<]+' "$PROJECT_DIR/$plugin/$plugin.csproj" | head -1)"
	stage="$(mktemp -d)"
	trap 'rm -rf "$stage"' EXIT

	mkdir -p "$stage/BepInEx/plugins/$plugin"
	cp "$PROJECT_DIR/$plugin/bin/Release/$plugin.dll" \
		"$stage/BepInEx/plugins/$plugin/$plugin.dll"

	zip_file="$OUT_DIR/$plugin-$version.zip"
	rm -f "$zip_file"
	(cd "$stage" && zip -qr9 "$zip_file" BepInEx)

	rm -rf "$stage"
	trap - EXIT

	echo
	echo "packaged: $zip_file"
	unzip -l "$zip_file" | sed 's/^/    /'
done

echo
echo "Every plugin must be installed on every peer in an online co-op session."