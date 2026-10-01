#!/usr/bin/env bash
# Packages the published Linux app into an AppImage using linuxdeploy.
# Called from CI after publish-linux.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/EZManifest.Linux/publish/linux-x64"
APPDIR="$ROOT/EZManifest.Linux/publish/AppDir"
OUTPUT="$ROOT/EZManifest.Linux/publish"
VERSION="${1:-1.2.2}"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/share/applications" "$APPDIR/usr/share/icons/hicolor/256x256/apps"

cp "$PUBLISH/EZManifest" "$APPDIR/usr/bin/EZManifest"
chmod +x "$APPDIR/usr/bin/EZManifest"

cp "$ROOT/EZManifest.Linux/Packaging/EZManifest.desktop" "$APPDIR/usr/share/applications/EZManifest.desktop"
cp "$ROOT/EZManifest.Linux/Assets/EZManifestLogo.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png"

# linuxdeploy AppRun (self-contained executable from CI download)
LINUXDEPLOY="${LINUXDEPLOY:-$OUTPUT/linuxdeploy-x86_64.AppImage}"
if [ ! -x "$LINUXDEPLOY" ]; then
  echo "linuxdeploy not found at $LINUXDEPLOY" >&2
  exit 1
fi

ARCH=x86_64 "$LINUXDEPLOY" \
  --appdir "$APPDIR" \
  --desktop-file "$APPDIR/usr/share/applications/EZManifest.desktop" \
  --icon-file "$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png" \
  --output appimage || true

# appimagetool fallback / rename
mkdir -p "$OUTPUT"
if compgen -G "$OUTPUT/EZManifest-*.AppImage" > /dev/null; then
  mv "$OUTPUT"/EZManifest-*.AppImage "$OUTPUT/EZManifest-$VERSION-x86_64.AppImage" 2>/dev/null || true
fi

echo "AppImage packaging attempted in $OUTPUT"
ls -la "$OUTPUT"
