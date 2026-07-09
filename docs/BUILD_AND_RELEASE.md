# Build And Release

## Local Build

Use telemetry opt-out in restricted environments:

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
```

If dependencies have not been restored yet:

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet restore OptilandWorkbench.slnx
AVALONIA_TELEMETRY_OPTOUT=1 dotnet build OptilandWorkbench.slnx /m:1 /nr:false
```

## Tests

```bash
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

VSTest opens a local socket. Some sandboxes need elevated permission for that test run.

## Run Desktop App

One-click launchers from the repository root:

- macOS: `Run-Optiland.command`
- Windows: `Run-Optiland.cmd`

Both launchers set `AVALONIA_TELEMETRY_OPTOUT=1` and run the Avalonia app project.

Terminal equivalent:

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

## Publish Targets

Publish every primary Windows/macOS runtime:

```bash
bash scripts/publish-cross-platform.sh
```

Set `SELF_CONTAINED=true` for self-contained outputs:

```bash
SELF_CONTAINED=true bash scripts/publish-cross-platform.sh
```

Framework-dependent:

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-arm64 --self-contained false
```

Self-contained:

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-x64 --self-contained true
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained true
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-arm64 --self-contained true
```

Publish output is under:

```text
src/OptilandWorkbench.App/bin/Release/net10.0/<runtime>/publish
```

## Current Validation Baseline

The current local baseline is:

- `dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false`
- `dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false`

The test suite covers architecture entry points, geometry/material behavior, tracing history, analysis catalog generation, optimization, tolerancing, JSON round-trip, rich component snapshots, commercial format round-trip, and plugin discovery.
