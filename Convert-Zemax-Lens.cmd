@echo off
setlocal
dotnet run --project "%~dp0tools\OpticalSystemDesign.ZemaxLibraryImporter\OpticalSystemDesign.ZemaxLibraryImporter.csproj" -- %*
exit /b %errorlevel%
