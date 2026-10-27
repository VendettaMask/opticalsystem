#!/usr/bin/env bash
set -euo pipefail
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dependency="${1:?Dependency directory required}"
output="${2:?Test output directory required}"
bash "$repository/scripts/fetch-sparkle.sh" "$dependency"
sparkle="$(cd "$dependency/Sparkle-2.10.1" && pwd)"
mkdir -p "$output"
xcrun clang -arch arm64 -mmacosx-version-min=14.0 -fobjc-arc -fblocks -dynamiclib -fvisibility=hidden -Wall -Werror \
  -F "$sparkle" -framework Sparkle -framework AppKit \
  "$repository/packaging/macos/UpdaterBridge.m" -o "$output/bridge.dylib"
xcrun clang -arch arm64 -mmacosx-version-min=14.0 -fobjc-arc -fblocks -Wall -Werror \
  -F "$sparkle" -framework Sparkle -framework AppKit -Wl,-rpath,"$sparkle" \
  "$repository/packaging/macos/UpdaterBridgeTests.m" -o "$output/native-tests"
"$output/native-tests"
