#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$PROJECT_ROOT"

# Keep compiler caches inside the project so this also works in a restricted workspace.
export CLANG_MODULE_CACHE_PATH="${CLANG_MODULE_CACHE_PATH:-$PROJECT_ROOT/.build/module-cache}"
export SWIFTPM_MODULECACHE_OVERRIDE="${SWIFTPM_MODULECACHE_OVERRIDE:-$CLANG_MODULE_CACHE_PATH}"
mkdir -p "$CLANG_MODULE_CACHE_PATH"

TEST_FLAGS=(
  --build-system native
  --disable-xctest
  --cache-path "$PROJECT_ROOT/.build/swift-cache"
  --config-path "$PROJECT_ROOT/.build/swift-config"
  --security-path "$PROJECT_ROOT/.build/swift-security"
)

# Some standalone Command Line Tools releases bundle Swift Testing but do not add
# its framework to SwiftPM's default search paths. Full Xcode needs no override.
DEVELOPER_PATH="$(xcode-select -p)"
TEST_FRAMEWORKS="$DEVELOPER_PATH/Library/Developer/Frameworks"
if [[ -d "$TEST_FRAMEWORKS/Testing.framework" ]]; then
  TEST_FLAGS+=(
    -Xswiftc -F -Xswiftc "$TEST_FRAMEWORKS"
    -Xlinker -F -Xlinker "$TEST_FRAMEWORKS"
    -Xlinker -rpath -Xlinker "$TEST_FRAMEWORKS"
  )
fi

# Opt in only when an enclosing development sandbox prevents SwiftPM nesting one.
if [[ "${PARVATHI_DISABLE_SWIFTPM_SANDBOX:-0}" == "1" ]]; then
  TEST_FLAGS+=(--disable-sandbox)
fi

swift test "${TEST_FLAGS[@]}" "$@"
