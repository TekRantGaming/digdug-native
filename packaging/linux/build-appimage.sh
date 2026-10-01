#!/usr/bin/env bash
# Builds DigDug-x86_64.AppImage. Run on Linux (x86_64) with the .NET 8 SDK and libsdl2-2.0-0 installed:
#   sudo apt install libsdl2-2.0-0 wget && ./packaging/linux/build-appimage.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

dotnet publish -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -o out/linux

APPDIR=out/AppDir
rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin" "$APPDIR/usr/lib"
cp out/linux/DigDug "$APPDIR/usr/bin/DigDug"
chmod +x "$APPDIR/usr/bin/DigDug"

# bundle SDL2 so the AppImage works on systems without it
SDL=$(ldconfig -p | awk '/libSDL2-2\.0\.so\.0/ && /x86-64/ {print $NF; exit}')
if [ -z "$SDL" ]; then echo "libSDL2-2.0.so.0 not found (install libsdl2-2.0-0)"; exit 1; fi
cp -L "$SDL" "$APPDIR/usr/lib/libSDL2-2.0.so.0"

cp packaging/linux/DigDug.desktop "$APPDIR/DigDug.desktop"
cp assets/icon.png "$APPDIR/digdug.png"
cp packaging/linux/AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun"

if [ ! -x out/appimagetool ]; then
  wget -q -O out/appimagetool https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod +x out/appimagetool
fi
ARCH=x86_64 out/appimagetool --appimage-extract-and-run "$APPDIR" out/DigDug-x86_64.AppImage
echo "Built out/DigDug-x86_64.AppImage"
