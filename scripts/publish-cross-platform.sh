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
done
