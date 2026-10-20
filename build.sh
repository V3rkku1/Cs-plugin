#!/usr/bin/env bash
# Builds:
#   build/LanSkinTester.zip            - the plugin, laid out as it goes into game/csgo/
#   build/LanSkinTesterLauncher.exe    - Windows launcher (Tauri, Rust), plugin included
#   build/LanSkinTesterSetup.exe       - installer (Start menu / desktop shortcuts, uninstaller), if NSIS is installed
# The launcher builds on Windows (Git Bash), or on Linux with cargo-xwin (cargo install cargo-xwin).
set -euo pipefail
cd "$(dirname "$0")"
# Windows paths for the Rust compiler and NSIS when this runs in Git Bash.
ROOT=$(pwd -W 2>/dev/null || pwd)

# zip, or 7-Zip where there is no zip (Git Bash on Windows).
zipdir() { if command -v zip >/dev/null; then zip -qr "$1" "$2"; else 7z a -tzip -bso0 "$1" "$2"; fi; }

OUT=build/addons/counterstrikesharp/plugins/LanSkinTester
rm -rf build
dotnet publish src/LanSkinTester -c Release -o "$OUT"
# Only our own DLL is needed; CounterStrikeSharp provides the rest at runtime.
find "$OUT" -maxdepth 1 -type f ! -name 'LanSkinTester.dll' ! -name 'LanSkinTester.deps.json' -delete
(cd build && zipdir LanSkinTester.zip addons) && echo "Built build/LanSkinTester.zip"

# The commit goes into the launcher so it can tell when GitHub has a newer build.
# TEST_BUILD=1: no commit ("dev build"), so the launcher doesn't update itself to the main release.
export LST_PLUGIN_DIR="$ROOT/$OUT"
if [ "${TEST_BUILD:-}" != 1 ]; then export LST_COMMIT=$(git rev-parse HEAD 2>/dev/null || true); fi
TARGET=x86_64-pc-windows-msvc
case "$(uname -s)" in
  MINGW*|MSYS*|CYGWIN*) CARGO=(cargo build) ;;
  *) if command -v cargo-xwin >/dev/null; then CARGO=(cargo xwin build); else CARGO=(); fi ;;
esac
if [ ${#CARGO[@]} -eq 0 ]; then
  echo "Skipping the launcher: build on Windows, or install cargo-xwin (cargo install cargo-xwin)"
  exit 0
fi
"${CARGO[@]}" --release --target "$TARGET" --manifest-path src/Launcher/Cargo.toml
cp "src/Launcher/target/$TARGET/release/LanSkinTesterLauncher.exe" build/
echo "Built build/LanSkinTesterLauncher.exe"

# Installer (needs NSIS). Wraps the launcher exe.
if command -v makensis >/dev/null; then
  VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' src/LanSkinTester/LanSkinTester.csproj | head -1)
  # NSIS on Windows wants backslashes (it found no file at D:/a/...).
  winpath() { cygpath -w "$1" 2>/dev/null || echo "$1"; }
  makensis -V2 -DVERSION="${VERSION:-0.0.0}" -DEXE="$(winpath "$PWD/build/LanSkinTesterLauncher.exe")" \
    -DOUT="$(winpath "$PWD/build/LanSkinTesterSetup.exe")" installer/LanSkinTester.nsi
  echo "Built build/LanSkinTesterSetup.exe"
else
  echo "makensis not found - skipping the installer (apt-get install nsis / choco install nsis)"
fi
