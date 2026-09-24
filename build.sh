#!/bin/zsh
# Builds Murmur.app.
#
#   ./build.sh              -> ./Murmur.app
#   ./build.sh --release    -> dist/Murmur.app and dist/Murmur-<version>.zip
set -euo pipefail
cd "$(dirname "$0")"

OUT=.
[[ "${1:-}" == "--release" ]] && OUT=dist

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
  VERSION=$(/usr/libexec/PlistBuddy -c "Print CFBundleShortVersionString" Info.plist)
  ZIP="dist/Murmur-$VERSION.zip"
  rm -f "$ZIP"
  ditto -c -k --keepParent "$APP" "$ZIP"
  echo "Packaged $ZIP"
fi
