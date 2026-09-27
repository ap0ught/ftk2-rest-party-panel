#!/usr/bin/env bash
# Builds RestPartyPanel.dll and copies it into the game's BepInEx/plugins folder.
set -euo pipefail

GAME_DIR="${FTK2_DIR:-$HOME/.local/share/Steam/steamapps/common/For The King II}"
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ ! -d "$GAME_DIR/For The King II_Data/Managed" ]; then
	echo "error: game not found at '$GAME_DIR' (override with FTK2_DIR=...)" >&2
	exit 1
fi

export FTK2ManagedDir="$GAME_DIR/For The King II_Data/Managed"

dotnet build "$PROJECT_DIR/RestPartyPanel/RestPartyPanel.csproj" \
	-c Release -v minimal --nologo

install -Dm644 "$PROJECT_DIR/RestPartyPanel/bin/Release/RestPartyPanel.dll" \
	"$GAME_DIR/BepInEx/plugins/RestPartyPanel.dll"

echo
echo "installed: $GAME_DIR/BepInEx/plugins/RestPartyPanel.dll"
