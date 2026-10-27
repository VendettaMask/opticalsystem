#!/usr/bin/env bash
set -euo pipefail
repository="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dependency="${1:?Dependency directory required}"
bundle="${2:?Destination bundle required}"
bash "$repository/scripts/fetch-sparkle.sh" "$dependency"
sparkle="$dependency/Sparkle-2.10.1"
mkdir -p "$bundle/Contents/Frameworks" "$bundle/Contents/MacOS/Licenses"
/usr/bin/ditto "$sparkle/Sparkle.framework" "$bundle/Contents/Frameworks/Sparkle.framework"
cp "$sparkle/LICENSE" "$bundle/Contents/MacOS/Licenses/Sparkle-LICENSE.txt"
xcrun clang -arch arm64 -mmacosx-version-min=14.0 -fobjc-arc -fblocks -dynamiclib -fvisibility=hidden -Wall -Werror \
  -F "$sparkle" -framework Sparkle -framework AppKit \
  -install_name @rpath/libOpticalSystemDesign.Updater.dylib -Wl,-rpath,@loader_path -Wl,-rpath,@loader_path/../Frameworks \
  "$repository/packaging/macos/UpdaterBridge.m" -o "$bundle/Contents/MacOS/libOpticalSystemDesign.Updater.dylib"
python3 "$repository/scripts/macos-ota-feed.py" configure "$bundle/Contents/Info.plist" "$repository/packaging/macos/ota-public-key.txt"
