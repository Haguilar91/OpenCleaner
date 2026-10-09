#!/bin/bash
# Builds build/OpenCleaner.app (needs Xcode or the Command Line Tools: xcode-select --install)
set -euo pipefail
cd "$(dirname "$0")"

swift build -c release
BIN="$(swift build -c release --show-bin-path)/OpenCleaner"

APP=build/OpenCleaner.app
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/OpenCleaner"
cp Resources/Info.plist "$APP/Contents/Info.plist"
iconutil -c icns Resources/AppIcon.iconset -o "$APP/Contents/Resources/AppIcon.icns"
codesign --force --sign - "$APP"   # ad-hoc signature so it launches locally

echo "Built $APP"
echo "Run it with:  open $APP"
