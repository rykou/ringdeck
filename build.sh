#!/usr/bin/env bash
# Builds RingDeck. Runs from WSL.
#   ./build.sh test   unit tests (Linux)
#   ./build.sh run    publish, copy to %TEMP%\ringdeck-dev and start it on Windows
#   ./build.sh        publish RingDeck.exe and package dist/RingDeck-<version>.msi with WiX 3
# Tools come from the environment, falling back to the decompilerPro ../../.local layout (DOTNET_ROOT, WIX_DIR).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
workspace="$(cd "$here/../.." && pwd)"
if [ -z "${DOTNET_ROOT:-}" ] && [ -x "$workspace/.local/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$workspace/.local/dotnet" NUGET_PACKAGES="${NUGET_PACKAGES:-$workspace/.local/nuget}"
fi
[ -n "${DOTNET_ROOT:-}" ] && export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
wix="${WIX_DIR:-$workspace/.local/wix3}"
cd "$here"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)"

if [ "${1:-}" = test ]; then
  exec dotnet test RingDeck.Core.Tests --nologo -v q
fi

[ -f ringdeck.ico ] && [ -f installer/banner.bmp ] && [ -f installer/dialog.bmp ] || python3 -I make_art.py
dotnet publish RingDeck.App -c Release -r win-x64 --self-contained true --nologo -v q -o "$here/publish" \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
wintemp="$(wslpath "$(cd /mnt/c && cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')")"

if [ "${1:-}" = run ]; then
  dev="$wintemp/ringdeck-dev"
  mkdir -p "$dev" && cp publish/RingDeck.exe "$dev/" || { echo "could not replace $dev/RingDeck.exe; is RingDeck still running?" >&2; exit 1; }
  # Start-Process detaches; `cmd /c start` keeps the WSL interop call open until the app exits.
  (cd /mnt/c && powershell.exe -NoProfile -Command "Start-Process '$(wslpath -w "$dev/RingDeck.exe")'" >/dev/null 2>&1 </dev/null)
  echo "started $dev/RingDeck.exe"
  exit 0
fi

if [ ! -x "$wix/candle.exe" ]; then
  echo "WiX not found at $wix; built publish/RingDeck.exe only. Set WIX_DIR to build the MSI." >&2
  exit 0
fi
# light.exe cannot write cabinets on a \\wsl.localhost path: package in a Windows temp folder, skip ICE validation.
stage="$wintemp/ringdeck-msi"
rm -rf "$stage" && mkdir -p "$stage/bin" dist
cp publish/RingDeck.exe "$stage/bin/"
cp installer/RingDeck.wxs installer/banner.bmp installer/dialog.bmp ringdeck.ico "$stage/"
# light.exe cannot load its extensions' embedded cabinets from a \\wsl.localhost path: run WiX from a Windows-side copy.
tools="$wintemp/ringdeck-wix3"
[ -x "$tools/light.exe" ] || { mkdir -p "$tools" && cp "$wix"/*.exe "$wix"/*.dll "$wix"/*.config "$tools/" 2>/dev/null || true; }
(cd "$stage" && "$tools/candle.exe" -nologo -arch x64 -ext WixUtilExtension -dVersion="$version" -dBinDir=bin \
     -out RingDeck.wixobj RingDeck.wxs \
  && "$tools/light.exe" -nologo -sval -ext WixUIExtension -ext WixUtilExtension -cultures:en-us \
     -out RingDeck.msi RingDeck.wixobj)
cp "$stage/RingDeck.msi" "dist/RingDeck-$version.msi"
rm -rf "$stage"
ls -la "dist/RingDeck-$version.msi"
