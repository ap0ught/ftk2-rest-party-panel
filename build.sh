#!/usr/bin/env bash
# Builds the plugins in this repo and copies them into the game's BepInEx/plugins
# folder. Each plugin is an independent BepInEx assembly; both are built together
# so a single run leaves the game folder consistent.
set -euo pipefail

GAME_DIR="${FTK2_DIR:-$HOME/.local/share/Steam/steamapps/common/For The King II}"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$GAME_DIR/For The King II_Data/Managed" ]; then
	echo "error: game not found at '$GAME_DIR' (override with FTK2_DIR=...)" >&2
	exit 1
fi

export FTK2ManagedDir="$GAME_DIR/For The King II_Data/Managed"

# Plugin directory name -> csproj directory name.
PLUGINS=(
	"RestPartyPanel"
	"FlockMemory"
)

for plugin in "${PLUGINS[@]}"; do
	echo "==> building $plugin"
	dotnet build "$PROJECT_DIR/$plugin/$plugin.csproj" \
		-c Release -v minimal --nologo
	install -Dm644 "$PROJECT_DIR/$plugin/bin/Release/$plugin.dll" \
		"$GAME_DIR/BepInEx/plugins/$plugin.dll"
	echo "    installed: $GAME_DIR/BepInEx/plugins/$plugin.dll"
done

echo
echo "all plugins installed. In co-op, share every one of them with your partner:"
for plugin in "${PLUGINS[@]}"; do
	echo "    $GAME_DIR/BepInEx/plugins/$plugin.dll"
done