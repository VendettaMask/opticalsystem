# Optiland Workbench

A cross-platform .NET desktop prototype inspired by the Optiland architecture guide.

The project mirrors the documented Optiland shape:

- `Optic` is the central container for fields, wavelengths, surfaces, ray tracing, paraxial data, aberration estimates, pickups, and solves.
- `SurfaceGroup` owns optical interfaces and renumbers them as users edit the lens data.
- `RealRayTracer`, `AnalysisRunner`, and `SimpleOptimizer` operate against the current `Optic`.
- The Avalonia GUI talks to the model through `OptilandConnector`, so panels do not mutate backend state directly.

## Requirements

- .NET SDK 10 or newer.
- Windows 10/11 or macOS. Avalonia also supports Linux, but this prototype was scoped for Windows and macOS.

## Run

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

The telemetry opt-out environment variable is useful in sandboxed environments that cannot write Avalonia build logs under the user profile.

## Test

```bash
dotnet test OptilandWorkbench.slnx
```

## Structure

```text
src/OptilandWorkbench.Core   Backend model, tracing, analysis, optimization, JSON IO
src/OptilandWorkbench.App    Avalonia desktop GUI and connector layer
tests/OptilandWorkbench.Tests
```

## Notes

The ray tracer and optimizer are intentionally lightweight approximations. They are written behind the same component boundaries that the Optiland guide describes, so replacing them with higher-fidelity models later should not force a GUI rewrite.
