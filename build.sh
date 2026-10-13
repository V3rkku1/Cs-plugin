#!/usr/bin/env bash
# Builds:
#   build/LanSkinTester.zip            - the plugin, laid out as it goes into game/csgo/
#   build/LanSkinTesterLauncher.exe    - Windows launcher (installer + skin picker), plugin included
set -euo pipefail
cd "$(dirname "$0")"

OUT=build/addons/counterstrikesharp/plugins/LanSkinTester
rm -rf build
dotnet publish src/LanSkinTester -c Release -o "$OUT"
# Only our own DLL is needed; CounterStrikeSharp provides the rest at runtime.
find "$OUT" -maxdepth 1 -type f ! -name 'LanSkinTester.dll' ! -name 'LanSkinTester.deps.json' -delete
(cd build && zip -qr LanSkinTester.zip addons) && echo "Built build/LanSkinTester.zip"

# Self-contained single exe: runs on any Windows 10/11 without installing .NET.
dotnet publish src/Launcher -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -p:PluginOutput="$PWD/$OUT/" -o build/launcher
mv build/launcher/LanSkinTesterLauncher.exe build/ && rm -rf build/launcher
echo "Built build/LanSkinTesterLauncher.exe"
