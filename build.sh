#!/usr/bin/env bash
# Builds:
#   build/LanSkinTester.zip            - the plugin, laid out as it goes into game/csgo/
#   build/LanSkinTesterLauncher.exe    - Windows launcher (installer + skin picker), plugin included
#   build/LanSkinTesterLauncher-small.zip - same, needs the .NET 10 Desktop Runtime installed
set -euo pipefail
cd "$(dirname "$0")"

# The commit goes into the launcher's version so it can tell when GitHub has a newer build.
REV=$(git rev-parse HEAD 2>/dev/null || true)

OUT=build/addons/counterstrikesharp/plugins/LanSkinTester
rm -rf build
dotnet publish src/LanSkinTester -c Release -o "$OUT"
# Only our own DLL is needed; CounterStrikeSharp provides the rest at runtime.
find "$OUT" -maxdepth 1 -type f ! -name 'LanSkinTester.dll' ! -name 'LanSkinTester.deps.json' -delete
(cd build && zip -qr LanSkinTester.zip addons) && echo "Built build/LanSkinTester.zip"

# Self-contained single exe: runs on any Windows 10/11 without installing .NET.
dotnet publish src/Launcher -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:PluginOutput="$PWD/$OUT/" -p:SourceRevisionId="$REV" -o build/launcher
mv build/launcher/LanSkinTesterLauncher.exe build/ && rm -rf build/launcher
echo "Built build/LanSkinTesterLauncher.exe"

# Small variant (~0.3 MB zip) that uses the installed .NET 10 Desktop Runtime (Windows offers to download it).
rm -rf src/Launcher/obj src/Launcher/bin  # don't reuse the self-contained build's intermediate files
dotnet publish src/Launcher -c Release -r win-x64 --self-contained false \
  -p:DebugType=none -p:PluginOutput="$PWD/$OUT/" -p:SourceRevisionId="$REV" -o build/LanSkinTesterLauncher-small
(cd build && zip -qr LanSkinTesterLauncher-small.zip LanSkinTesterLauncher-small) && rm -rf build/LanSkinTesterLauncher-small
echo "Built build/LanSkinTesterLauncher-small.zip"
