#!/usr/bin/env bash
# Builds the self-contained Linux publish folder that gets packaged into the AppImage.
# Called from CI; requires the .NET 8 SDK.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/publish/linux-x64"

dotnet publish "$ROOT/EZManifest.Linux.csproj" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:PublishTrimmed=false \
  -o "$OUT"

echo "Published to $OUT"
