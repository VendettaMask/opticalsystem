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

echo "Preparing Optical System Design (S.T.A.R. Labs)..."
echo "Project: $PROJECT"
echo

echo "[1/3] Cleaning previous build outputs..."
"$DOTNET" clean "$PROJECT" --nologo --verbosity minimal
STATUS=$?
if [ "$STATUS" -eq 0 ]; then
  echo
  echo "[2/3] Rebuilding the application..."
  "$DOTNET" build "$PROJECT" --nologo --verbosity minimal
  STATUS=$?
fi

if [ "$STATUS" -eq 0 ]; then
  echo
  echo "[3/3] Starting the rebuilt application..."
  "$DOTNET" run --project "$PROJECT" --no-build
  STATUS=$?
fi

echo
if [ "$STATUS" -eq 0 ]; then
  echo "Optical System Design closed."
else
  echo "Optical System Design exited with code $STATUS."
fi

pause_if_interactive
exit "$STATUS"
