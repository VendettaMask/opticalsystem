@echo off
setlocal
chcp 65001 >nul

cd /d "%~dp0"
set "PROJECT=src\OptilandWorkbench.App\OptilandWorkbench.App.csproj"
set "AVALONIA_TELEMETRY_OPTOUT=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo 未找到 .NET SDK。
    echo 请安装 .NET SDK 10 或更新版本后重新运行。
    pause
    exit /b 1
)

echo 正在启动 Optiland 光学工作台...
echo 项目: %PROJECT%
echo.

dotnet run --project "%PROJECT%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Optiland 光学工作台已关闭。
) else (
    echo Optiland 光学工作台退出，代码 %EXITCODE%。
)

pause
exit /b %EXITCODE%
