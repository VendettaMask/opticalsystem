@echo off
cd /d "%~dp0"
set AVALONIA_TELEMETRY_OPTOUT=1
dotnet run --project labs/CoatingDesign/src/OptilandWorkbench.CoatingDesign.App/OptilandWorkbench.CoatingDesign.App.csproj -c Release -- %*
