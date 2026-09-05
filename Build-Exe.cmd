@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build-installer.ps1" %*
set "BUILD_EXIT_CODE=%ERRORLEVEL%"
echo.
if not "%BUILD_EXIT_CODE%"=="0" echo Installer packaging failed. See the error above.
if not defined OPTILAND_PACKAGE_NO_PAUSE pause
exit /b %BUILD_EXIT_CODE%
