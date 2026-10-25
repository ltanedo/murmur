#!/bin/zsh
# Builds Murmur.app.
#
#   ./build.sh              -> ./Murmur.app
#   ./build.sh --release    -> dist/Murmur.app and dist/Murmur-<version>.zip
set -euo pipefail
cd "$(dirname "$0")"

OUT=.
[[ "${1:-}" == "--release" ]] && OUT=dist

# Single source of truth for the version, shared with the Windows build.
VERSION=$(tr -d '[:space:]' < VERSION)
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" Info.plist
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" Info.plist 2>/dev/null || true

swift build -c release

APP="$OUT/Murmur.app"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp .build/release/Murmur "$APP/Contents/MacOS/Murmur"
cp Info.plist "$APP/Contents/Info.plist"
cp Resources/AppIcon.icns "$APP/Contents/Resources/AppIcon.icns"
codesign --force --sign - "$APP"
echo "Built $APP"

if [[ "$OUT" == dist ]]; then
  ZIP="dist/Murmur-$VERSION.zip"
  rm -f "$ZIP" dist/Murmur-mac.zip
  ditto -c -k --keepParent "$APP" "$ZIP"
  cp "$ZIP" dist/Murmur-mac.zip   # stable name for the one-line installer
  echo "Packaged $ZIP"
fi
