# Optiland Workbench

Optiland Workbench is a pure .NET/C# + Avalonia optical design workbench that follows the public Optiland documentation shape without calling a Python Optiland backend. It targets Windows and macOS first, with Linux also supported by Avalonia in principle.

The implementation is being built in small git milestones. The current codebase includes:

- A central `Optic` object with aperture, fields, wavelengths, surfaces, backend selection, ray tracers, analysis, optimization, tolerancing, pickups, solves, and multi-configuration entry points.
- A composition-based surface model: `Geometry + MaterialBefore + MaterialAfter + Coating + Interaction + PhysicalAperture + optional Scattering + CoordinateSystem`, while retaining GUI-compatible legacy table fields.
- Managed CPU backend abstraction through `INumericBackend`.
- Sequential real-ray tracing with surface-owned trace kernels, local coordinates, aperture clipping, refraction/reflection, coating/scattering hooks, Python-style normalized `Trace`/`TraceGeneric` entry points, and per-surface geometric path, optical path, OPD, and recorded array data.
- Plane, standard, even/odd asphere, biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q, and placeholder geometry models for not-yet-implemented freeforms.
- Air/vacuum, constant-index, Cauchy, Sellmeier, polynomial-dispersion, Abbe, catalog extinction, and absorption support.
- Optiland 0.5.8 Cooke Triplet and Tessar F/4.5 compatibility fixtures with matching EFL, F-number, entrance pupil, per-surface real rays, intensity, and line-bundle RMS spot results.
- Analysis catalog entries for spot, ray fan, distortion, grid distortion, field curvature, encircled energy, pupil aberration, RMS vs field, through focus, Y-Ybar, PSF, MTF, wavefront, Zernike, image simulation, Jones pupil, first order, and prescription report; spot, encircled energy, RMS vs field, through focus, and wavefront now consume sequential ray histories and weighted image samples.
- Optimization and tolerancing foundations with variables, operands, scaling, optimizer catalog, seeded Monte Carlo, perturbations, samplers, and compensators.
- Native JSON snapshot round-trip, Python Optiland 0.5.8 JSON import/export for the validated sequential subset, and common sequential subset import/export for Zemax `.zmx`, CODE V `.seq`, and OSLO `.len`.
- .NET plugin discovery with geometry, material, and analysis registration.
- Chinese Avalonia GUI panels for lens editing, component editing, interactive 2D/3D system viewing, graphical multi-page analysis, optimization, tolerancing, multi-configuration, and system properties.
- GUI infrastructure for panel management, `Ctrl/Cmd+K` command palette actions, light/dark theme selection, split-pane layout slots, and analysis report copy/export.

## Requirements

- .NET SDK 10 or newer.
- Windows 10/11 or macOS for the primary target platforms.

## Run

One-click launchers:

- macOS: double-click `Run-Optiland.command`.
- Windows: double-click `Run-Optiland.cmd`.

First launch may take a moment because `dotnet run` restores and builds the project.

Terminal:

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

The telemetry opt-out environment variable is useful in sandboxed environments that cannot write Avalonia build logs under the user profile.

## Test

```bash
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

In restricted sandboxes, VSTest may need permission to bind a local socket.

## Publish

Framework-dependent:

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained false
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained false
```

Self-contained:

```bash
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r osx-arm64 --self-contained true
dotnet publish src/OptilandWorkbench.App/OptilandWorkbench.App.csproj -c Release -r win-x64 --self-contained true
```

Use `osx-x64`, `osx-arm64`, `win-x64`, or `win-arm64` depending on the target machine.

## Structure

```text
src/OptilandWorkbench.Core   Backend model, tracing, analysis, optimization, tolerancing, JSON/file IO, plugins
src/OptilandWorkbench.App    Avalonia desktop GUI and connector layer
tests/OptilandWorkbench.Tests Core, serialization, file format, plugin, optimization, and tolerancing tests
docs                         Architecture, parity, file format, plugin, and release notes
```

See:

- [Architecture](docs/ARCHITECTURE.md)
- [Python Optiland parity audit](docs/PYTHON_PARITY_AUDIT.md)
- [Parity matrix](docs/PARITY_MATRIX.md)
- [File formats and plugins](docs/FILE_FORMATS_AND_PLUGINS.md)
- [Build and release](docs/BUILD_AND_RELEASE.md)
- [GUI Quickstart comparison and refactor](docs/GUI_QUICKSTART_REFACTOR.md)
- [Optiland 0.5.8 numerical parity](docs/NUMERICAL_PARITY.md)
- [Python Optiland JSON interoperability](docs/PYTHON_JSON_INTEROP.md)
- [Python analysis and plot parity](docs/PYTHON_ANALYSIS_PARITY.md)

## Notes

This repository is a clean-room .NET implementation shaped by the Optiland documentation. Advanced numerical fidelity still needs staged follow-up work for NURBS/grating freeforms, diffraction efficiency, full thin-film TMM, rigorous PSF/MTF/wavefront math, broader commercial format compatibility, and optional GPU/autograd backend support.
