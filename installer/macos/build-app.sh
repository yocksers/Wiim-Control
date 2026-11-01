#!/usr/bin/env bash
set -euo pipefail

SRC="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$SRC/installer/Output"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/WiimControl.csproj" | head -1)"
RCODESIGN_VERSION=0.29.0
mkdir -p "$OUT"

TOOLS="$(mktemp -d)"
trap 'rm -rf "$TOOLS"' EXIT
if ! command -v codesign >/dev/null 2>&1 && [ -z "${RCODESIGN:-}" ]; then
  curl -fsSL -o "$TOOLS/rcodesign.tar.gz" \
    "https://github.com/indygreg/apple-platform-rs/releases/download/apple-codesign%2F$RCODESIGN_VERSION/apple-codesign-$RCODESIGN_VERSION-x86_64-unknown-linux-musl.tar.gz"
  tar -xzf "$TOOLS/rcodesign.tar.gz" -C "$TOOLS"
  RCODESIGN="$(find "$TOOLS" -name rcodesign -type f | head -1)"
  chmod +x "$RCODESIGN"
fi

u32() { od -An -tu4 -j "$2" -N4 "$1" | tr -d ' '; }

set_macho_uuid() {
  local file="$1" hex off ncmds cmd size i
  hex="$(printf '%s' "$2" | sha1sum | cut -c1-32)"
  hex="${hex:0:12}5${hex:13:3}$(printf '%x' $(( (0x${hex:16:1} & 3) | 8 )))${hex:17:15}"
  ncmds="$(u32 "$file" 16)"
  off=32
  for ((i = 0; i < ncmds; i++)); do
    cmd="$(u32 "$file" "$off")"
    size="$(u32 "$file" $((off + 4)))"
    if [ "$cmd" = 27 ]; then
      local bytes=""
      for ((i = 0; i < 32; i += 2)); do bytes+="\\x${hex:i:2}"; done
      printf "$bytes" | dd of="$file" bs=1 seek=$((off + 8)) conv=notrunc status=none
      return
    fi
    off=$((off + size))
  done
}

for ARCH in arm64 x64; do
  WORK="$(mktemp -d)"
  APP="$WORK/Wiim Control.app"
  mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

  dotnet publish "$SRC/WiimControl.csproj" -c Release -r "osx-$ARCH" -o "$APP/Contents/MacOS" -p:DebugType=none

  sed "s/VERSION/$VERSION/g" "$SRC/installer/macos/Info.plist" > "$APP/Contents/Info.plist"
  cp "$SRC/installer/macos/logo.icns" "$APP/Contents/Resources/logo.icns"
  chmod +x "$APP/Contents/MacOS/WiimControl"
  set_macho_uuid "$APP/Contents/MacOS/WiimControl" "com.wiimcontrol.app-$ARCH"

  if command -v codesign >/dev/null 2>&1; then
    if [ -n "${MACOS_SIGNING_IDENTITY:-}" ]; then
      codesign --force --deep --options runtime --timestamp --sign "$MACOS_SIGNING_IDENTITY" "$APP"
    else
      codesign --force --deep --sign - "$APP"
    fi
  else
    find "$APP/Contents/MacOS" -type f \( -name "*.dylib" -o -name WiimControl -o -name createdump \) -print0 |
      while IFS= read -r -d '' file; do "$RCODESIGN" sign "$file" >/dev/null; done
    "$RCODESIGN" sign "$APP" >/dev/null
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
