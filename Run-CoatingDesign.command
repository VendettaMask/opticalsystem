#!/usr/bin/env bash
set -eu
COATING_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
if command -v dotnet >/dev/null 2>&1; then
  COATING_DOTNET="$(command -v dotnet)"
elif [ -x /usr/local/share/dotnet/dotnet ]; then
  COATING_DOTNET=/usr/local/share/dotnet/dotnet
else
  echo "Please install .NET SDK 10."
  exit 1
fi
cd "$COATING_ROOT"
export AVALONIA_TELEMETRY_OPTOUT=1
exec "$COATING_DOTNET" run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App/OptilandWorkbench.CoatingDesign.App.csproj -c Release -- "$@"
