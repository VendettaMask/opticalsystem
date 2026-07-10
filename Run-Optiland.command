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
  echo "未找到 .NET SDK。"
  echo "请安装 .NET SDK 10 或更新版本后重新运行。"
  read -r -p "按回车关闭窗口..."
  exit 1
fi

export AVALONIA_TELEMETRY_OPTOUT=1

echo "正在启动 Optiland 光学工作台..."
echo "项目: $PROJECT"
echo

"$DOTNET" run --project "$PROJECT"
STATUS=$?

echo
if [ "$STATUS" -eq 0 ]; then
  echo "Optiland 光学工作台已关闭。"
else
  echo "Optiland 光学工作台退出，代码 $STATUS。"
fi

read -r -p "按回车关闭窗口..."
exit "$STATUS"
