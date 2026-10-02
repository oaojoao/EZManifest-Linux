#!/usr/bin/env bash
# Packages the published Linux app into an AppImage using linuxdeploy.
# Called from CI after publish-linux.sh.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH="$ROOT/publish/linux-x64"
APPDIR="$ROOT/publish/AppDir"
TOOLS="$ROOT/publish/tools"
VERSION="${1:-1.2.2}"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" \
         "$APPDIR/usr/share/applications" \
         "$APPDIR/usr/share/icons/hicolor/256x256/apps" \
         "$TOOLS"

cp "$PUBLISH/EZManifest" "$APPDIR/usr/bin/EZManifest"
chmod +x "$APPDIR/usr/bin/EZManifest"

cp "$ROOT/Packaging/EZManifest.desktop" "$APPDIR/usr/share/applications/EZManifest.desktop"

# linuxdeploy requires standard icon resolutions; the source logo is 1024x1024.
ICON="$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png"
if command -v convert >/dev/null 2>&1; then
  convert "$ROOT/Assets/EZManifestLogo.png" -resize 256x256 "$ICON"
elif command -v ffmpeg >/dev/null 2>&1; then
  ffmpeg -y -loglevel error -i "$ROOT/Assets/EZManifestLogo.png" -vf scale=256:256 "$ICON"
else
  echo "No image tool available to resize the icon to 256x256" >&2
  exit 1
fi

LINUXDEPLOY="${LINUXDEPLOY:-$TOOLS/linuxdeploy-x86_64.AppImage}"
if [ ! -x "$LINUXDEPLOY" ]; then
  echo "linuxdeploy not found at $LINUXDEPLOY" >&2
  exit 1
fi

export OUTPUT="$PUBLISH"
export ARCH=x86_64
export VERSION

"$LINUXDEPLOY" \
  --appdir="$APPDIR" \
  --executable="$APPDIR/usr/bin/EZManifest" \
  --desktop-file="$APPDIR/usr/share/applications/EZManifest.desktop" \
  --icon-file="$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png" \
  --output=appimage

mv "$PUBLISH/EZManifest-x86_64.AppImage" "$PUBLISH/EZManifest-$VERSION-x86_64.AppImage"

echo "AppImage built:"
ls -la "$PUBLISH"/*.AppImage
