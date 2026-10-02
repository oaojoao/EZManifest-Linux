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

# CEF ships many native libraries next to the executable: copy the whole
# publish output into the AppDir and launch through a shell wrapper.
APPFOLDER="$APPDIR/usr/share/EZManifest"
mkdir -p "$APPFOLDER"
cp -a "$PUBLISH/." "$APPFOLDER/"
chmod +x "$APPFOLDER/EZManifest" 2>/dev/null || true
find "$APPFOLDER" -name '*.so' -exec chmod +x {} + 2>/dev/null || true

cat > "$APPDIR/usr/bin/EZManifest" <<'WRAPPER'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${TMPDIR:-/tmp}/ezmanifest-bundle"
exec "$DIR/../share/EZManifest/EZManifest" "$@"
WRAPPER
chmod +x "$APPDIR/usr/bin/EZManifest"

cp "$ROOT/Packaging/EZManifest.desktop" "$APPDIR/usr/share/applications/EZManifest.desktop"

# linuxdeploy requires standard icon resolutions; the repo ships a 256x256 variant.
cp "$ROOT/Assets/EZManifestLogo-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png"

LINUXDEPLOY="${LINUXDEPLOY:-$TOOLS/linuxdeploy-x86_64.AppImage}"
if [ ! -x "$LINUXDEPLOY" ]; then
  echo "linuxdeploy not found at $LINUXDEPLOY" >&2
  exit 1
fi

export OUTPUT="$OUTDIR/EZManifest-$VERSION-x86_64.AppImage"
export ARCH=x86_64
export VERSION

# Deploy shared-library dependencies of the bundled binaries without letting
# linuxdeploy move or parse the wrapper. The app is self-contained .NET + CEF,
# so this only picks up a handful of system libraries.
"$LINUXDEPLOY" \
  --appdir="$APPDIR" \
  --desktop-file="$APPDIR/usr/share/applications/EZManifest.desktop" \
  --icon-file="$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png" \
  --deploy-deps-only="$APPFOLDER" \
  --output=appimage

echo "AppImage built:"
ls -la "$OUTDIR"/*.AppImage
