#!/usr/bin/env bash
# Build CDni.xcframework: iOS device + simulator, macOS, Mac Catalyst.
# MUST run on macOS. Produces swift/Frameworks/CDni.xcframework.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJ="$ROOT/dotnet/DotnetBridge.Native/DotnetBridge.Native.csproj"
HEADER="$ROOT/dotnet/abi/dni.h"
MODMAP="$ROOT/build/module.modulemap"
OUT="$ROOT/build/out"
FWK="CDni"
SIGN_ID="${CODESIGN_IDENTITY:--}"   # default to ad-hoc "-"; override for distribution

rm -rf "$OUT"; mkdir -p "$OUT"

# publish one RID, echo the produced dylib path
publish() {
  local rid="$1" dir="$OUT/publish/$1"
  dotnet publish "$PROJ" -c Release -r "$rid" -o "$dir" -p:UseAppHost=false >&2
  local lib; lib="$(ls "$dir"/*.dylib | head -n1)"
  [ -f "$lib" ] || { echo "ERROR: no dylib for $rid" >&2; exit 1; }
  echo "$lib"
}

# assemble a signed .framework from 1+ dylibs (lipo if >1); echo its path
make_framework() {
  local platdir="$1"; shift
  local fw="$platdir/$FWK.framework"
  mkdir -p "$fw/Headers" "$fw/Modules"
  if [ "$#" -gt 1 ]; then lipo -create "$@" -output "$fw/$FWK"; else cp "$1" "$fw/$FWK"; fi
  install_name_tool -id "@rpath/$FWK.framework/$FWK" "$fw/$FWK"
  cp "$HEADER" "$fw/Headers/dni.h"
  cp "$MODMAP" "$fw/Modules/module.modulemap"
  cat > "$fw/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>com.swiftdotnetbridge.CDni</string>
  <key>CFBundleName</key><string>CDni</string>
  <key>CFBundleExecutable</key><string>CDni</string>
  <key>CFBundlePackageType</key><string>FMWK</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
</dict></plist>
PLIST
  # install_name_tool invalidates the signature -> re-sign with timestamp.
  codesign --force --timestamp --sign "$SIGN_ID" "$fw/$FWK" >&2
  echo "$fw"
}

FW_IOS=$(make_framework "$OUT/fw/ios"        "$(publish ios-arm64)")
FW_SIM=$(make_framework "$OUT/fw/ios-sim"    "$(publish iossimulator-arm64)" "$(publish iossimulator-x64)")
FW_MAC=$(make_framework "$OUT/fw/macos"      "$(publish osx-arm64)"          "$(publish osx-x64)")
FW_CAT=$(make_framework "$OUT/fw/catalyst"   "$(publish maccatalyst-arm64)"  "$(publish maccatalyst-x64)")

DEST="$ROOT/swift/Frameworks/$FWK.xcframework"
rm -rf "$DEST"; mkdir -p "$ROOT/swift/Frameworks"
xcodebuild -create-xcframework \
  -framework "$FW_IOS" -framework "$FW_SIM" \
  -framework "$FW_MAC" -framework "$FW_CAT" \
  -output "$DEST"

echo "Built $DEST"
