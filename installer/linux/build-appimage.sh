#!/usr/bin/env bash
set -euo pipefail

SRC="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
OUT="$SRC/installer/Output"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$SRC/WiimControl.csproj" | head -1)"

APPIMAGETOOL_VERSION=1.9.1
APPIMAGETOOL_SHA256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
RUNTIME_VERSION=20251108
RUNTIME_SHA256=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d

missing=()
command -v file >/dev/null 2>&1 || missing+=(file)
command -v python3 >/dev/null 2>&1 || missing+=(python3)
if [ ${#missing[@]} -gt 0 ] && command -v apt-get >/dev/null 2>&1; then
  apt-get update -qq >/dev/null && apt-get install -y -qq "${missing[@]}" >/dev/null
fi

fetch() {
  curl -fsSL --retry 3 --retry-all-errors --retry-delay 5 --max-time 180 "$@"
}

fetch_release_file() {
  local repo="$1" tag="$2" name="$3" sha256="$4" target="$5" auth=() asset
  if [ -n "${GITHUB_TOKEN:-}" ]; then auth=(-H "Authorization: Bearer $GITHUB_TOKEN"); fi
  if ! fetch -o "$target" "https://github.com/$repo/releases/download/$tag/$name"; then
    echo "The normal download of $name failed, trying the GitHub API instead"
    asset="$(fetch "${auth[@]}" "https://api.github.com/repos/$repo/releases/tags/$tag" |
      python3 -c 'import json, sys; print(next(a["url"] for a in json.load(sys.stdin)["assets"] if a["name"] == sys.argv[1]))' "$name")"
    fetch "${auth[@]}" -H "Accept: application/octet-stream" -o "$target" "$asset"
  fi
  echo "$sha256  $target" | sha256sum -c -
}

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
RUNTIME="$WORK/runtime-x86_64"
fetch_release_file AppImage/appimagetool "$APPIMAGETOOL_VERSION" appimagetool-x86_64.AppImage "$APPIMAGETOOL_SHA256" "$TOOL"
fetch_release_file AppImage/type2-runtime "$RUNTIME_VERSION" runtime-x86_64 "$RUNTIME_SHA256" "$RUNTIME"
chmod +x "$TOOL"
ARCH=x86_64 "$TOOL" --appimage-extract-and-run --no-appstream --runtime-file "$RUNTIME" "$APPDIR" "$OUT/WiimControl-$VERSION-x86_64.AppImage"

rm -rf "$WORK"
echo "Built $OUT/WiimControl-$VERSION-x86_64.AppImage"
