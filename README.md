# Optical System Design

**Optical System Design** is produced by **S.T.A.R. Labs**. It is a pure .NET/C# + Avalonia optical design workbench that follows the public Optiland documentation shape without calling a Python Optiland backend. It targets Windows and macOS first, with Linux also supported by Avalonia in principle.

The implementation is being built in small git milestones. The current codebase includes:

- A central `Optic` object with aperture, fields, wavelengths, surfaces, backend selection, ray tracers, analysis, optimization, tolerancing, pickups, solves, and multi-configuration entry points.
- A composition-based surface model: `Geometry + MaterialBefore + MaterialAfter + Coating + Interaction + PhysicalAperture + optional Scattering + CoordinateSystem`, while retaining GUI-compatible legacy table fields.
- An embedded 1,740-entry compatibility library plus a bundled 63-catalog Zemax glass database containing 5,502 AGF records, with manufacturer-aware lookup, all 13 Zemax dispersion formulas, thermal/mechanical/transmission metadata, and wavelength-dependent refractive-index/extinction calculations. The Zemax source catalogs are converted once into the Workbench-owned compressed `.ogdb` format.
- Managed CPU backend abstraction through `INumericBackend`.
- Sequential real-ray tracing with surface-owned trace kernels, local coordinates, aperture clipping, refraction/reflection, coating/scattering hooks, Python-style angle/object-height/paraxial-image-height fields plus Zemax real-image-height chief-ray solving, vignetting and object-space telecentric launch, normalized `Trace`/`TraceGeneric` entry points, and per-surface geometric path, optical path, OPD, and recorded array data.
- Plane, standard, even/odd asphere, biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q, and placeholder geometry models for not-yet-implemented freeforms.
- Air/vacuum, constant-index, Cauchy, Sellmeier, polynomial-dispersion, Abbe, catalog extinction, and absorption support.
- Optiland 0.5.8 Cooke Triplet and Tessar F/4.5 compatibility fixtures with matching EFL, F-number, entrance/exit pupil geometry, per-surface real rays, intensity, optical path, and line-bundle spot results.
- A 56-entry desktop analysis catalog. Thirty numerical/graphical views have source-derived Python contracts, including standard spot and ray-aberration plots, single-ray trace reports, through-focus spots/MTF, distortion/field curvature, RMS field sweeps, chief-ray and centroid/best-fit reference-sphere wavefronts, Zernike, FFT/Huygens/geometric/sampled MTF, FFT/MMDFT/Huygens PSF, irradiance/radiant intensity, incident-angle scans, Jones pupil, and image simulation. Additional Zemax-style views include full-field/matrix/defocus spot diagrams, optical-path difference, Foucault, Seidel, axial/lateral color, full-field aberration, cardinal data, vignetting, footprint, and relative illumination.
- Optimization plus a Zemax-style tolerancing workflow with a TDE-like operand editor, bulk tolerance wizard, radius/thickness/conic/decenter/tilt/index/Abbe operands, image-distance compensation, two-sided sensitivity, seeded Monte Carlo, percentile/yield statistics, native tolerance-file save/load, and report export.
- A native `.staropt` project container with magic/version headers, Brotli compression, SHA-256 integrity validation, atomic saves, and lossless multi-configuration round-trip. Legacy Workbench JSON remains a compatibility import; Python Optiland 0.5.8 JSON, Zemax `.zmx`, CODE V `.seq`, and OSLO `.len` remain explicit exchange formats.
- A packaged read-only lens library under **Database > Lens Library**, currently containing 56 microscope objectives and 5 industrial examples. Microscope entries are restricted to standalone objectives; tube lenses, condensers, Fourier-imaging trains, and complete microscope systems are excluded. An external maintenance tool converts reviewed local source files once into native `.staropt` projects using the Workbench glass database; the desktop application only loads the finished library and shows per-lens parameters plus an interactive 2D optical layout.
- .NET plugin discovery with geometry, material, and analysis registration.
- Chinese Avalonia GUI panels for lens editing, component editing, interactive 2D/3D system viewing, configurable graphical analysis, optimization, tolerancing, multi-configuration, and system properties, presented with a compact semi-flat desktop style.
- A dedicated **Manufacturing & Drawings** workspace with rule-based optical-element manufacturability review, ISO 10110-series Chinese drawing previews, editable tolerances and title blocks, and vector PDF export.
- Reproducible S.T.A.R. Labs desktop branding with packaged Windows/macOS application icons and a startup image that remains visible while the workspace session is restored.
- A linked category Ribbon: selecting a top category replaces the large-command region, with dedicated 2D/3D view commands and all 56 analyses grouped under **Analysis** using the Zemax image-quality hierarchy: rays and spots, aberrations, wavefront, PSF, MTF, RMS, encircled energy, and extended image analysis, followed by system reports. The rays-and-spots menu starts with Single Ray Trace, Ray Aberration, Standard Spot Diagram, Footprint Diagram, and Through Focus Spot Diagram. Method families such as PSF and MTF open a second-level menu instead of flattening every variant into the Ribbon.
- A Dock.Avalonia workspace with draggable document tabs, split panes, tab merging, native resizable floating windows, redocking, tiling/cascading commands, `Ctrl/Cmd+K` command palette actions, and per-file workspace sessions.
- A UI-free `OptilandWorkbench.Application` boundary. Avalonia panels depend only on immutable DTOs and application services; Core objects stay behind the application context, revision stream, undo/redo, snapshot, and cancellation boundary.
- Interactive analysis plots with pointer-centered wheel zoom, drag pan, double-click reset, and nearest-sample hover readout. Analysis pages expose **Plot / Data / Text** tabs at the bottom and keep graph settings in a collapsed-by-default panel with an icon-only synchronization action.

## Requirements

- .NET SDK 10 or newer.
- Windows 10/11 or macOS for the primary target platforms.

## Run

One-click launchers:

- macOS: double-click `Run-Optiland.command`.
- Windows: double-click `Run-Optiland.cmd`.

First launch may take a moment because `dotnet run` restores and builds the project.

### Desktop workflow

- Use the top categories to switch the large-command Ribbon; commands are not duplicated in the central workspace.
- Open **View** and choose **2D Layout**, **3D Layout**, or **Solid Model**. The 2D view uses outlined optical elements and colored rays; the 3D layout keeps a light engineering background, while the solid-model view uses a clean dark studio background and continuous dielectric glass driven by the catalog refractive index, Fresnel reflectance, and element-thickness attenuation. Highlighted optical surfaces and ray bundles remain visible through the glass.
- Open **Analysis**, choose an image-quality category, and then select the required method from its second-level menu. The selected analysis runs and opens its own closable result page.
- Open **Database > Lens Library** to filter packaged microscope and industrial designs, inspect their basic parameters, and preview each native system in 2D. Library download and conversion are intentionally absent from the desktop application.
- Open **Manufacturing & Drawings** to review center/edge thickness, curvature, slope, and special-surface risks, or to prepare a Chinese ISO 10110-series reference drawing and export it as PDF.
- Open **Tolerance** to generate editable tolerance operands with the wizard, validate the table, select RMS spot or RMS wavefront performance, and run sensitivity plus Monte Carlo analysis. Tolerance definitions can be saved as `*.startol.json`; completed results can be exported as a text report.
- Expand **Settings** only when parameters need adjustment, then use the adjacent synchronization icon to rerun the current page.
- Drag any document tab to split, merge, float, or redock it. Use **Window** for bulk docking, floating, tiling, cascading, locking, closing, and default-layout commands.
- In an analysis plot, use the wheel to zoom, drag to pan, double-click to reset, and hover near a sample to inspect its values.

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

In restricted sandboxes, VSTest may need permission to bind a local socket. The current validated baseline as of 2026-07-26 is 462 passing tests with a zero-warning solution build.

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
src/OptilandWorkbench.Core   Backend model, tracing, split analysis families, optimization, tolerancing, JSON/file IO, plugins
src/OptilandWorkbench.Application UI-free independent services, workspace coordination, DTO mapping, and a thin legacy compatibility facade
src/OptilandWorkbench.App    Avalonia desktop GUI, split shell/analysis views, drawing renderer facade, Dock workspace, and session persistence
tests/OptilandWorkbench.Tests Core, Python parity, application contracts, Dock/session, analysis, visualization, serialization, file format, plugin, optimization, and tolerancing tests
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
- [Local icon library](docs/LOCAL_ICONS.md)
- [Application branding](docs/BRANDING.md)
- [Manufacturability and optical drawings](docs/MANUFACTURING_DRAWINGS.md)
- [Tolerancing workflow](docs/TOLERANCING.md)
- [Packaged lens library](docs/LENS_LIBRARY.md)

Manual import and viewer samples are available under [`samples/lenses`](samples/lenses), including angle, finite-object-height, and Zemax real-image-height systems backed by bundled catalog glass.

## Notes

This repository is a clean-room .NET implementation shaped by the Optiland documentation. The parity claim is intentionally limited to the source-derived Cooke/Tessar contracts documented under `docs/`. Remaining work includes Forbes/NURBS/grid-sag freeform JSON, diffraction efficiency, full thin-film TMM, vectorial diffraction methods, broader commercial-format and Python-JSON coverage, non-sequential tracing, and optional GPU/autograd backends.
