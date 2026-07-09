#!/usr/bin/env bash
set -u

ROOT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="src/OptilandWorkbench.App/OptilandWorkbench.App.csproj"
cd "$ROOT_DIR" || exit 1

if command -v dotnet >/dev/null 2>&1; then
  DOTNET="dotnet"
elif [ -x "/usr/local/share/dotnet/dotnet" ]; then
  DOTNET="/usr/local/share/dotnet/dotnet"
else
  echo "The .NET SDK was not found."
  echo "Install .NET SDK 10 or newer, then run this file again."
  read -r -p "Press Return to close this window..."
  exit 1
fi

export AVALONIA_TELEMETRY_OPTOUT=1

echo "Starting Optiland Workbench..."
echo "Project: $PROJECT"
echo

"$DOTNET" run --project "$PROJECT"
STATUS=$?

echo
if [ "$STATUS" -eq 0 ]; then
  echo "Optiland Workbench closed."
else
  echo "Optiland Workbench exited with code $STATUS."
fi

read -r -p "Press Return to close this window..."
exit "$STATUS"
