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

echo Preparing Optical System Design ^(S.T.A.R. Labs^)...
echo Project: %PROJECT%
echo.

echo [1/3] Cleaning previous build outputs...
dotnet clean "%PROJECT%" --nologo --verbosity minimal
if errorlevel 1 goto clean_failed

echo.
echo [2/3] Rebuilding the application...
dotnet build "%PROJECT%" --nologo --verbosity minimal
if errorlevel 1 goto build_failed

echo.
echo [3/3] Starting the rebuilt application...
dotnet run --project "%PROJECT%" --no-build
set "EXITCODE=%ERRORLEVEL%"
goto report

:clean_failed
set "EXITCODE=%ERRORLEVEL%"
echo.
echo Cleaning previous build outputs failed with code %EXITCODE%.
goto report

:build_failed
set "EXITCODE=%ERRORLEVEL%"
echo.
echo Rebuilding Optical System Design failed with code %EXITCODE%.

:report
echo.
if "%EXITCODE%"=="0" (
    echo Optical System Design closed.
) else (
    echo Optical System Design exited with code %EXITCODE%.
)

pause
exit /b %EXITCODE%
