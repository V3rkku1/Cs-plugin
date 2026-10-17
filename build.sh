#!/usr/bin/env bash
# Builds:
#   build/LanSkinTester.zip            - the plugin, laid out as it goes into game/csgo/
#   build/LanSkinTesterLauncher.exe    - Windows launcher (installer + skin picker), plugin included
#   build/LanSkinTesterLauncher-small.zip - same, needs the .NET 10 Desktop Runtime installed
#   build/LanSkinTesterSetup.exe       - installer (Start menu / desktop shortcuts, uninstaller), if NSIS is installed
set -euo pipefail
cd "$(dirname "$0")"

# The commit goes into the launcher's version so it can tell when GitHub has a newer build.
REV=$(git rev-parse HEAD 2>/dev/null || true)
VERSION_ARGS=(-p:SourceRevisionId="$REV")
# TEST_BUILD=1: no commit in the version ("dev build"), so the launcher doesn't update itself to the main release.
[ "${TEST_BUILD:-}" = 1 ] && VERSION_ARGS=(-p:IncludeSourceRevisionInInformationalVersion=false)

OUT=build/addons/counterstrikesharp/plugins/LanSkinTester
rm -rf build
dotnet publish src/LanSkinTester -c Release -o "$OUT"
# Only our own DLL is needed; CounterStrikeSharp provides the rest at runtime.
find "$OUT" -maxdepth 1 -type f ! -name 'LanSkinTester.dll' ! -name 'LanSkinTester.deps.json' -delete
(cd build && zip -qr LanSkinTester.zip addons) && echo "Built build/LanSkinTester.zip"

# Self-contained single exe: runs on any Windows 10/11 without installing .NET.
dotnet publish src/Launcher -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:PluginOutput="$PWD/$OUT/" "${VERSION_ARGS[@]}" -o build/launcher
mv build/launcher/LanSkinTesterLauncher.exe build/ && rm -rf build/launcher
echo "Built build/LanSkinTesterLauncher.exe"

# Small variant (~0.3 MB zip) that uses the installed .NET 10 Desktop Runtime (Windows offers to download it).
rm -rf src/Launcher/obj src/Launcher/bin  # don't reuse the self-contained build's intermediate files
dotnet publish src/Launcher -c Release -r win-x64 --self-contained false \
  -p:DebugType=none -p:PluginOutput="$PWD/$OUT/" "${VERSION_ARGS[@]}" -o build/LanSkinTesterLauncher-small
(cd build && zip -qr LanSkinTesterLauncher-small.zip LanSkinTesterLauncher-small) && rm -rf build/LanSkinTesterLauncher-small
echo "Built build/LanSkinTesterLauncher-small.zip"

# Installer (needs NSIS: apt-get install nsis). Wraps the self-contained exe.
if command -v makensis >/dev/null; then
  VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/LanSkinTester/LanSkinTester.csproj | head -1)
  makensis -V2 -DVERSION="${VERSION:-0.0.0}" -DEXE="$PWD/build/LanSkinTesterLauncher.exe" \
    -DOUT="$PWD/build/LanSkinTesterSetup.exe" installer/LanSkinTester.nsi
  echo "Built build/LanSkinTesterSetup.exe"
else
  echo "makensis not found - skipping the installer (apt-get install nsis)"
fi
