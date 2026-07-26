@echo off
setlocal
dotnet run --project "%~dp0tools\OptilandWorkbench.ZemaxLibraryImporter\OptilandWorkbench.ZemaxLibraryImporter.csproj" -- %*
exit /b %errorlevel%
