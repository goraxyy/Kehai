#!/bin/sh
# Builds the Mac camera helper for the blink mechanic: tools/blink/mac/build/BlinkVision.
# Needs only the Xcode command line tools (run `xcode-select --install` once if swiftc is missing).
set -e
cd "$(dirname "$0")"
mkdir -p build/ModuleCache

# The usage string macOS shows when it asks for camera access.
cat > build/Info.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.tokenlimit.kehai.blinkvision</string>
  <key>CFBundleName</key><string>BlinkVision</string>
  <key>NSCameraUsageDescription</key><string>Kehai reads whether your eyes are open. Nothing is recorded or sent anywhere.</string>
</dict></plist>
PLIST

xcrun --sdk macosx swiftc -module-cache-path build/ModuleCache -O BlinkVision.swift -o build/BlinkVision \
  -framework AVFoundation -framework Vision -framework CoreMedia -framework Network \
  -Xlinker -sectcreate -Xlinker __TEXT -Xlinker __info_plist -Xlinker build/Info.plist

echo "Built $(pwd)/build/BlinkVision"
