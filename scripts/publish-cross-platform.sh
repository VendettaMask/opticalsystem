#!/usr/bin/env bash
set -euo pipefail

PROJECT="src/OptilandWorkbench.App/OptilandWorkbench.App.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
SELF_CONTAINED="${SELF_CONTAINED:-false}"
export AVALONIA_TELEMETRY_OPTOUT=1

for runtime in osx-arm64 osx-x64 win-x64 win-arm64; do
  dotnet publish "$PROJECT" \
    -c "$CONFIGURATION" \
    -r "$runtime" \
    --self-contained "$SELF_CONTAINED"

  if [[ "$runtime" == osx-* ]]; then
    runtime_root="src/OptilandWorkbench.App/bin/$CONFIGURATION/net10.0/$runtime"
    publish_root="$runtime_root/publish"
    bundle_root="$runtime_root/Optical System Design.app"
    bundle_contents="$bundle_root/Contents"

    rm -rf "$bundle_root"
    mkdir -p "$bundle_contents/MacOS" "$bundle_contents/Resources"
    cp -R "$publish_root/." "$bundle_contents/MacOS/"
    cp "packaging/macos/Info.plist" "$bundle_contents/Info.plist"
    cp "src/OptilandWorkbench.App/Assets/Brand/AppIcon.icns" \
      "$bundle_contents/Resources/AppIcon.icns"
    chmod +x "$bundle_contents/MacOS/OptilandWorkbench.App"
  fi
done
