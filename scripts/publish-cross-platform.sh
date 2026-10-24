#!/usr/bin/env bash
set -euo pipefail

PROJECT="src/OpticalSystemDesign.App/OpticalSystemDesign.App.csproj"
CONFIGURATION="${CONFIGURATION:-Release}"
SELF_CONTAINED="${SELF_CONTAINED:-true}"
export AVALONIA_TELEMETRY_OPTOUT=1
app_version="$(dotnet msbuild "$PROJECT" -nologo -getProperty:Version)"
[[ "$app_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid product version" >&2; exit 1; }

if [[ "$(uname -s)" == Darwin ]]; then
  bash scripts/build-macos.sh "$@"
  exit 0
fi

for runtime in win-x64 win-arm64; do
  dotnet publish "$PROJECT" \
    -c "$CONFIGURATION" \
    -r "$runtime" \
    --self-contained "$SELF_CONTAINED"

  publish_root="src/OpticalSystemDesign.App/bin/$CONFIGURATION/net10.0/$runtime/publish"
  for module in InitialStructure.Contracts InitialStructure.Engine InitialStructure.Persistence CoatingDesign.Engine Modules; do
    test -s "$publish_root/OpticalSystemDesign.$module.dll" || {
      echo "Required built-in module missing: $module" >&2
      exit 1
    }
  done

done
