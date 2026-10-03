#!/usr/bin/env bash
set -euo pipefail

SRC="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
OUT="$SRC/installer/Output"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/WiimControl.csproj" | head -1)"

if ! command -v file >/dev/null 2>&1 && command -v apt-get >/dev/null 2>&1; then
  apt-get update -qq >/dev/null && apt-get install -y -qq file >/dev/null
fi

mkdir -p "$OUT"
tar -C "$SRC" --exclude=./bin --exclude=./obj --exclude=./installer/Output -cf - . | tar -C "$WORK" -xf -

APPDIR="$WORK/WiimControl.AppDir"
mkdir -p "$APPDIR/usr/bin"
dotnet publish "$WORK/WiimControl.csproj" -c Release -r linux-x64 -o "$APPDIR/usr/bin" -p:DebugType=none

cp "$SRC/installer/linux/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun"
cp "$SRC/installer/linux/wiim-control.desktop" "$APPDIR/wiim-control.desktop"
cp "$SRC/Assets/tray.png" "$APPDIR/wiim-control.png"

TOOL="$WORK/appimagetool-x86_64.AppImage"
curl -fsSL -o "$TOOL" https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x "$TOOL"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream "$APPDIR" "$OUT/WiimControl-$VERSION-x86_64.AppImage"

rm -rf "$WORK"
echo "Built $OUT/WiimControl-$VERSION-x86_64.AppImage"
