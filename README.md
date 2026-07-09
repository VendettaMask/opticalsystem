# Optiland Workbench

A cross-platform .NET desktop implementation inspired by the Optiland architecture guide.

The project mirrors the documented Optiland shape:

- `Optic` is the central container for fields, wavelengths, surfaces, ray tracing, paraxial data, aberration estimates, pickups, and solves.
- `SurfaceGroup` owns optical interfaces and renumbers them as users edit the lens data.
- `RealRayTracer`, `SequentialRayTracer`, `AnalysisRunner`, and optimization/tolerancing services operate against the current `Optic`.
- The Avalonia GUI talks to the model through `OptilandConnector`, so panels do not mutate backend state directly.
- The new parity foundation adds Optiland-equivalent module boundaries for backend, rays, ray tracing, geometry, materials, coatings, interactions, propagation, apertures, sources, tolerancing, multi-configuration, file IO, plugins, and visualization.

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

See [docs/PARITY_MATRIX.md](docs/PARITY_MATRIX.md) for the implementation map against the Optiland documentation.

## Notes

This repository is being built in milestones toward a pure .NET/C# Optiland-style implementation. The current milestone establishes the architecture and representative CPU implementations; advanced freeform, diffraction, wavefront, thin-film, GPU/autograd, and commercial-format fidelity remain staged follow-up work.
