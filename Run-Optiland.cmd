@echo off
setlocal

cd /d "%~dp0"
set "PROJECT=src\OptilandWorkbench.App\OptilandWorkbench.App.csproj"
set "AVALONIA_TELEMETRY_OPTOUT=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo The .NET SDK was not found.
    echo Install .NET SDK 10 or newer, then run this file again.
    pause
    exit /b 1
)

echo Starting Optiland Workbench...
echo Project: %PROJECT%
echo.

dotnet run --project "%PROJECT%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Optiland Workbench closed.
) else (
    echo Optiland Workbench exited with code %EXITCODE%.
)

pause
exit /b %EXITCODE%
