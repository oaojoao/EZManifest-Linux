#!/usr/bin/env bash
# Builds the self-contained Linux publish folder that gets packaged into the AppImage.
# Called from CI; requires the .NET 8 SDK.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish/linux-x64"
CLI_URL="https://github.com/dpadGuy/Steam-auto-crack/releases/download/3.5.0.7/SteamAutoCrack.CLI.zip"
CLI_SHA256="b2338e1d97405bac49cca121a969b57ffb094aecbda9b18bc6093b333a591e73"

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
  echo "$CLI_SHA256  $TMP/sac.zip" | sha256sum -c -
  unzip -q "$TMP/sac.zip" -d "$TMP/sac"
  mkdir -p "$STAGE_DIR"
  cp -a "$TMP/sac/." "$STAGE_DIR/"
  echo "Staged SteamAutoCrack.CLI -> $STAGE_DIR"
fi

echo "Published to $OUT"
