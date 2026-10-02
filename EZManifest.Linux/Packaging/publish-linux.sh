#!/usr/bin/env bash
# Builds the self-contained Linux publish folder that gets packaged into the AppImage.
# Called from CI; requires the .NET 8 SDK.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish/linux-x64"
CLI_URL="https://github.com/dpadGuy/Steam-auto-crack/releases/download/3.5.0.7/SteamAutoCrack.CLI.zip"

dotnet publish "$ROOT/EZManifest.Linux.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:PublishTrimmed=false \
  -o "$OUT"

# Stage SteamAutoCrack.CLI next to the app binary: PostDownloadService looks for
# "$OUT/SteamAutoCrack.CLI/SteamAutoCrack.CLI.exe" and silently skips the crack
# step when it is missing. Mirrors scripts/stage-steamautocrack.ps1.
STAGE_DIR="$OUT/SteamAutoCrack.CLI"
if [ -f "$STAGE_DIR/SteamAutoCrack.CLI.exe" ]; then
  echo "SteamAutoCrack.CLI already present."
else
  TMP="$(mktemp -d)"
  trap 'rm -rf "$TMP"' EXIT
  echo "Fetching SteamAutoCrack.CLI from $CLI_URL ..."
  curl -fsSL -o "$TMP/sac.zip" "$CLI_URL"
  unzip -q "$TMP/sac.zip" -d "$TMP/sac"
  mkdir -p "$STAGE_DIR"
  cp -a "$TMP/sac/." "$STAGE_DIR/"
  echo "Staged SteamAutoCrack.CLI -> $STAGE_DIR"
fi

echo "Published to $OUT"
