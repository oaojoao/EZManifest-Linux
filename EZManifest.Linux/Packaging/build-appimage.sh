#!/usr/bin/env bash
# Packages the published Linux app into an AppImage using linuxdeploy.
# Called from CI after publish-linux.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/publish/linux-x64"
APPDIR="$ROOT/publish/AppDir"
TOOLS="$ROOT/publish/tools"
OUTDIR="$ROOT/publish/out"
VERSION="${1:-1.2.2}"

rm -rf "$APPDIR" "$OUTDIR"
mkdir -p "$OUTDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
         "$TOOLS"

cp "$PUBLISH/EZManifest" "$APPDIR/usr/bin/EZManifest"
chmod +x "$APPDIR/usr/bin/EZManifest"

cp "$ROOT/Packaging/EZManifest.desktop" "$APPDIR/usr/share/applications/EZManifest.desktop"

# linuxdeploy requires standard icon resolutions; the repo ships a 256x256 variant.
cp "$ROOT/Assets/EZManifestLogo-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png"

LINUXDEPLOY="${LINUXDEPLOY:-$TOOLS/linuxdeploy-x86_64.AppImage}"
if [ ! -x "$LINUXDEPLOY" ]; then
  echo "linuxdeploy not found at $LINUXDEPLOY" >&2
  exit 1
fi

export OUTPUT="$OUTDIR"
export ARCH=x86_64
export VERSION

"$LINUXDEPLOY" \
  --appdir="$APPDIR" \
  --executable="$APPDIR/usr/bin/EZManifest" \
  --desktop-file="$APPDIR/usr/share/applications/EZManifest.desktop" \
  --icon-file="$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png" \
  --output=appimage

mv "$OUTDIR/EZManifest-x86_64.AppImage" "$OUTDIR/EZManifest-$VERSION-x86_64.AppImage"

echo "AppImage built:"
ls -la "$OUTDIR"/*.AppImage
