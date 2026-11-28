@echo off
setlocal
call :main "%~1"
exit /b %ERRORLEVEL%

:main

cd /d "%~dp0"
set "PROJECT=src\OpticalSystemDesign.App\OpticalSystemDesign.App.csproj"
set "AVALONIA_TELEMETRY_OPTOUT=1"

set "DOTNET_EXE="
set "FIRST_DOTNET_EXE="
if defined DOTNET_ROOT_X64 call :try_dotnet "%DOTNET_ROOT_X64%\dotnet.exe"
if defined DOTNET_ROOT call :try_dotnet "%DOTNET_ROOT%\dotnet.exe"
for /f "delims=" %%D in ('"%SystemRoot%\System32\where.exe" dotnet.exe 2^>nul') do call :try_dotnet "%%D"
if defined ProgramW6432 call :try_dotnet "%ProgramW6432%\dotnet\dotnet.exe"
if defined ProgramFiles call :try_dotnet "%ProgramFiles%\dotnet\dotnet.exe"
if defined USERPROFILE call :try_dotnet "%USERPROFILE%\.dotnet\dotnet.exe"

if not defined DOTNET_EXE (
    echo A .NET SDK compatible with global.json was not found.
    echo Install the SDK specified by global.json and try again.
    echo.
    type "global.json"
    if defined FIRST_DOTNET_EXE (
        echo.
        echo Installed SDKs:
        "%FIRST_DOTNET_EXE%" --list-sdks
    )
    if /i "%~1"=="--check" exit /b 1
    pause
    exit /b 1
)

echo .NET host: %DOTNET_EXE%
echo SDK:
"%DOTNET_EXE%" --version
if not "%ERRORLEVEL%"=="0" exit /b 1
if /i "%~1"=="--check" exit /b 0

echo Preparing Optical System Design ^(S.T.A.R. Labs^)...
echo Project: %PROJECT%
echo.

echo [1/4] Restoring package dependencies...
"%DOTNET_EXE%" restore "%PROJECT%" --nologo --verbosity minimal
if not "%ERRORLEVEL%"=="0" goto restore_failed

echo.
echo [2/4] Cleaning previous build outputs...
"%DOTNET_EXE%" clean "%PROJECT%" --configuration Release --nologo --verbosity minimal
if not "%ERRORLEVEL%"=="0" goto clean_failed

echo.
echo [3/4] Rebuilding the application...
"%DOTNET_EXE%" build "%PROJECT%" --configuration Release --no-restore --nologo --verbosity minimal
if not "%ERRORLEVEL%"=="0" goto build_failed

echo.
echo [4/4] Starting the rebuilt application...
"%DOTNET_EXE%" run --project "%PROJECT%" --configuration Release --no-build
set "EXITCODE=%ERRORLEVEL%"
goto report

:restore_failed
set "EXITCODE=%ERRORLEVEL%"
echo.
echo Restoring package dependencies failed with code %EXITCODE%.
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

:try_dotnet
if defined DOTNET_EXE exit /b 0
if not exist "%~1" exit /b 0
if not defined FIRST_DOTNET_EXE set "FIRST_DOTNET_EXE=%~1"
"%~1" --version >nul 2>nul
if not "%ERRORLEVEL%"=="0" exit /b 0
set "DOTNET_EXE=%~1"
exit /b 0
