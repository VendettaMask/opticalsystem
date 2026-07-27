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
Repository attributes keep the Windows launcher in CRLF format and the macOS launcher in LF format.

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

For each macOS runtime, the script also creates a Finder-ready application bundle:

```text
src/OptilandWorkbench.App/bin/Release/net10.0/<runtime>/Optical System Design.app
```

The bundle declares the native `.staropt` document type, uses `AppIcon.icns` for
both the application and saved projects, and forwards Finder-opened project paths
to the application.

## Current Validation Baseline

The current local baseline is:

- `dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false`
- `dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false`

Expected result as of 2026-07-28:

- solution build: 0 warnings, 0 errors
- tests: 513 passed, 0 failed, 0 skipped

The suite covers architecture entry points, geometry/material behavior, the embedded manufacturer glass catalog, radial field and pupil sampling, per-surface tracing, 30 Python-referenced analysis views plus the broader 67-entry desktop catalog, diffraction/extended-source encircled energy, extended image analysis, generated analysis parameter settings, optimization, TDE-style tolerance generation/validation, two-sided sensitivity, compensated Monte Carlo statistics, native/Python JSON round-trip, rich component snapshots, commercial format round-trip, Zemax and bitmap file viewers, faceted STEP generation, visualization, manufacturing review, optical drawing/PDF rendering, file association, and plugin discovery.

Regenerate Python fixtures only when intentionally updating the pinned `optiland==0.5.8` contract or its embedded CC0 glass data:

```bash
MPLCONFIGDIR=/private/tmp/optiland-mpl .venv/bin/python \
  tools/python-reference/generate_analysis_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-analysis-reference.json

MPLCONFIGDIR=/private/tmp/optiland-mpl .venv/bin/python \
  tools/python-reference/generate_zemax_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-zemax-reference.zmx \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-zemax-reference.json

.venv/bin/python tools/python-reference/generate_glass_catalog.py \
  .venv/lib/python3.14/site-packages/optiland/database \
  src/OptilandWorkbench.Core/Materials/Data/glass-catalog.json

MPLCONFIGDIR=/private/tmp/optiland-mpl .venv/bin/python \
  tools/python-reference/generate_glass_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-glass-reference.json
```

Review and run the full suite before committing regenerated fixture data.
