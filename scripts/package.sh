#!/usr/bin/env bash
set -euo pipefail
PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"
CONFIG="${1:-release}"
if [[ "$CONFIG" != "debug" && "$CONFIG" != "release" ]]; then
  echo 'Usage: scripts/package.sh [debug|release]' >&2
  exit 2
fi
mkdir -p .build/module-cache
export CLANG_MODULE_CACHE_PATH="$PROJECT_ROOT/.build/module-cache"
export SWIFTPM_MODULECACHE_OVERRIDE="$PROJECT_ROOT/.build/module-cache"
BUILD_ARGS=(--build-system native --disable-sandbox --cache-path "$PROJECT_ROOT/.build/cache" --config-path "$PROJECT_ROOT/.build/config" --security-path "$PROJECT_ROOT/.build/security")
swift build "${BUILD_ARGS[@]}" -c "$CONFIG"
BIN_DIR="$(swift build "${BUILD_ARGS[@]}" -c "$CONFIG" --show-bin-path)"
APP="$PROJECT_ROOT/dist/Parvathi.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/Parvathi" "$APP/Contents/MacOS/Parvathi"
cp Resources/Info.plist "$APP/Contents/Info.plist"
chmod +x "$APP/Contents/MacOS/Parvathi"
codesign --force --deep --sign "${SIGNING_IDENTITY:--}" "$APP"
if [[ "$CONFIG" == "release" ]]; then
  ditto -c -k --sequesterRsrc --keepParent "$APP" "$PROJECT_ROOT/dist/Parvathi-macOS.zip"
fi
printf 'Built %s\n' "$APP"
