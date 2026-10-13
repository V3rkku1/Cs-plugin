#!/usr/bin/env bash
# Builds the plugin and lays it out exactly as it goes into the CS2 server folder:
#   build/addons/counterstrikesharp/plugins/LanSkinTester/
set -euo pipefail
cd "$(dirname "$0")"

OUT=build/addons/counterstrikesharp/plugins/LanSkinTester
rm -rf build
dotnet publish src/LanSkinTester -c Release -o "$OUT"
# Only our own DLL is needed; CounterStrikeSharp provides the rest at runtime.
find "$OUT" -maxdepth 1 -type f ! -name 'LanSkinTester.dll' ! -name 'LanSkinTester.deps.json' -delete

(cd build && zip -qr LanSkinTester.zip addons) && echo "Built build/LanSkinTester.zip"
