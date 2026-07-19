#!/usr/bin/env bash
set -u

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)"
PROJECT="src/OptilandWorkbench.App/OptilandWorkbench.App.csproj"

pause_if_interactive() {
  if [ -t 0 ]; then
    read -r -p "Press Return to close this window..."
  fi
}

if command -v dotnet >/dev/null 2>&1; then
  DOTNET="$(command -v dotnet)"
elif [ -x "$HOME/.dotnet/dotnet" ]; then
  DOTNET="$HOME/.dotnet/dotnet"
elif [ -x "/usr/local/share/dotnet/dotnet" ]; then
  DOTNET="/usr/local/share/dotnet/dotnet"
else
  echo ".NET SDK was not found."
  echo "Install .NET SDK 10 or later and try again."
  pause_if_interactive
  exit 1
fi

cd "$ROOT_DIR" || exit 1
export AVALONIA_TELEMETRY_OPTOUT=1

echo "Starting Optical System Design (S.T.A.R. Labs)..."
echo "Project: $PROJECT"
echo

"$DOTNET" run --project "$PROJECT"
STATUS=$?

echo
if [ "$STATUS" -eq 0 ]; then
  echo "Optical System Design closed."
else
  echo "Optical System Design exited with code $STATUS."
fi

pause_if_interactive
exit "$STATUS"
