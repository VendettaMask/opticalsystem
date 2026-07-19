@echo off
setlocal

cd /d "%~dp0"
set "PROJECT=src\OptilandWorkbench.App\OptilandWorkbench.App.csproj"
set "AVALONIA_TELEMETRY_OPTOUT=1"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo .NET SDK was not found.
    echo Install .NET SDK 10 or later and try again.
    pause
    exit /b 1
)

echo Starting Optical System Design ^(S.T.A.R. Labs^)...
echo Project: %PROJECT%
echo.

dotnet run --project "%PROJECT%"
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo Optical System Design closed.
) else (
    echo Optical System Design exited with code %EXITCODE%.
)

pause
exit /b %EXITCODE%
