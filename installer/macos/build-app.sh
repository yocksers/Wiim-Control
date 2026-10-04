#!/usr/bin/env bash
set -euo pipefail

SRC="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$SRC/installer/Output"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/WiimControl.csproj" | head -1)"
mkdir -p "$OUT"

for ARCH in arm64 x64; do
  WORK="$(mktemp -d)"
  APP="$WORK/Wiim Control.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

  dotnet publish "$SRC/WiimControl.csproj" -c Release -r "osx-$ARCH" -o "$APP/Contents/MacOS" -p:DebugType=none

  sed "s/VERSION/$VERSION/g" "$SRC/installer/macos/Info.plist" > "$APP/Contents/Info.plist"
  cp "$SRC/installer/macos/logo.icns" "$APP/Contents/Resources/logo.icns"
  chmod +x "$APP/Contents/MacOS/WiimControl"

  if command -v codesign >/dev/null 2>&1; then
    if [ -n "${MACOS_SIGNING_IDENTITY:-}" ]; then
      codesign --force --deep --options runtime --timestamp --sign "$MACOS_SIGNING_IDENTITY" "$APP"
    else
      codesign --force --deep --sign - "$APP"
    fi
  fi

  ZIP="$OUT/WiimControl-$VERSION-macos-$ARCH.zip"
  rm -f "$ZIP"
  if command -v ditto >/dev/null 2>&1; then
    ditto -c -k --sequesterRsrc --keepParent "$APP" "$ZIP"
  else
    (cd "$WORK" && zip -qry "$ZIP" "Wiim Control.app")
  fi
  rm -rf "$WORK"
  echo "Built $ZIP"
done
