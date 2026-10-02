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
# publish output into the AppDir and launch through a wrapper that disables
# the Chromium sandbox (unsupported inside an AppImage).
mkdir -p "$APPDIR/usr/bin/app"
cp -a "$PUBLISH/." "$APPDIR/usr/bin/app/"
chmod +x "$APPDIR/usr/bin/app/EZManifest" 2>/dev/null || true
find "$APPDIR/usr/bin/app" -name '*.so' -exec chmod +x {} + 2>/dev/null || true

cat > "$APPDIR/usr/bin/EZManifest" <<'WRAPPER'
#!/usr/bin/env bash
DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="${TMPDIR:-/tmp}/ezmanifest-bundle"
exec "$DIR/app/EZManifest" "$@"
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

# Deploy dependencies from the real .NET executable; the launcher wrapper is a
# shell script, so linuxdeploy must not try to parse it as an ELF binary.
APPBIN="$APPDIR/usr/bin/app/EZManifest"
chmod +x "$APPBIN"
"$LINUXDEPLOY" \
  --appdir="$APPDIR" \
  --desktop-file="$APPDIR/usr/share/applications/EZManifest.desktop" \
  --icon-file="$APPDIR/usr/share/icons/hicolor/256x256/apps/EZManifest.png" \
  --executable="$APPBIN" \
  --output=appimage

# linuxdeploy moved the real binary into usr/bin: put the wrapper back on top
# and restore the app folder layout the wrapper expects.
if [ -f "$APPDIR/usr/bin/EZManifest" ] && [ "$APPDIR/usr/bin/EZManifest" -ef "$APPBIN" ]; then
  mv "$APPDIR/usr/bin/EZManifest" "$APPBIN"
fi
printf '#!/usr/bin/env bash\nDIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"\nexec "$DIR/app/EZManifest" "$@"\n' > "$APPDIR/usr/bin/EZManifest"
chmod +x "$APPDIR/usr/bin/EZManifest"

echo "AppImage built:"
ls -la "$OUTDIR"/*.AppImage
