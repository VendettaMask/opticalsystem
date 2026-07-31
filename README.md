# Optical System Design

**Optical System Design** is produced by **S.T.A.R. Labs**. It is a pure .NET/C# + Avalonia optical design workbench that follows the public Optiland documentation shape without calling a Python Optiland backend. It targets Windows and macOS first, with Linux also supported by Avalonia in principle.

The implementation is being built in small git milestones. The current codebase includes:

- A central `Optic` object with aperture, fields, wavelengths, surfaces, backend selection, ray tracers, analysis, optimization, tolerancing, pickups, solves, and multi-configuration entry points.
- A composition-based surface model: `Geometry + MaterialBefore + MaterialAfter + Coating + Interaction + PhysicalAperture + optional Scattering + CoordinateSystem`, while retaining GUI-compatible legacy table fields.
- An embedded 1,740-entry compatibility library plus a bundled 63-catalog Zemax glass database containing 5,502 AGF records, with manufacturer-aware lookup, all 13 Zemax dispersion formulas, thermal/mechanical/transmission metadata, and wavelength-dependent refractive-index/extinction calculations. The Zemax source catalogs are converted once into the Workbench-owned compressed `.ogdb` format.
- Managed CPU backend abstraction through the compatible scalar `INumericBackend` plus optional `IBatchedNumericBackend`; the built-in backend uses `System.Numerics.Vector<double>` SIMD for the common sequential path and falls back to scalar kernels for unsupported surfaces.
- Sequential real-ray tracing with surface-owned trace kernels, local coordinates, aperture clipping, explicit transmitted/reflected/total-internal-reflection outcomes, coating/scattering hooks, Python-style angle/object-height/paraxial-image-height fields plus Zemax real-image-height chief-ray solving, vignetting and object-space telecentric launch. `TraceRequest` selects final-only, selected-surface, or full-history retention; pooled SoA state, deterministic parallel tracing, and shared flat result views keep large ray bundles bounded by the number of retained surfaces while legacy `Trace` entry points remain compatible.
- Plane, standard, even/odd asphere, biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q, and placeholder geometry models for not-yet-implemented freeforms.
- Air/vacuum, constant-index, Cauchy, Sellmeier, polynomial-dispersion, Abbe, catalog extinction, and absorption support.
- Optiland 0.5.8 Cooke Triplet and Tessar F/4.5 compatibility fixtures with matching EFL, F-number, entrance/exit pupil geometry, per-surface real rays, intensity, optical path, and line-bundle spot results.
- A 69-entry desktop analysis catalog. Thirty numerical/graphical views have source-derived Python contracts, including standard spot and ray-aberration plots, single-ray trace reports, through-focus spots/MTF, distortion/field curvature, RMS field sweeps, chief-ray and centroid/best-fit reference-sphere wavefronts, Zernike, FFT/Huygens/geometric/sampled MTF, FFT/MMDFT/Huygens PSF, irradiance/radiant intensity, incident-angle scans, Jones pupil, and image simulation. Additional Zemax-style views include full-field/configuration-matrix spot diagrams, diffraction and extended-source encircled energy, line/edge spread, optical-path difference, Foucault, Seidel, axial/lateral color, full-field aberration, cardinal data, vignetting, footprint, relative illumination, and extended image analysis.
- Optimization plus a Zemax-style tolerancing workflow with a TDE-like operand editor, bulk tolerance wizard, radius/thickness/conic/decenter/tilt/index/Abbe operands, image-distance compensation, two-sided sensitivity, seeded Monte Carlo, percentile/yield statistics, native tolerance-file save/load, and report export.
- A native `.staropt` project container with magic/version headers, Brotli compression, SHA-256 integrity validation, schema-4 optical-state validation, transactional temporary construction, atomic saves, and lossless multi-configuration round-trip. Checksum-valid payloads are still rejected when wavelengths, coordinates, components, numbering, or typed cross-references are semantically invalid. Legacy Workbench JSON remains a compatibility import; Python Optiland 0.5.8 JSON, Zemax `.zmx`, CODE V `.seq`, and OSLO `.len` remain explicit exchange formats.
- A packaged read-only lens library under **Database > Lens Library**, currently containing 849 entries: 56 microscope objectives, 5 industrial examples, and 788 compatible public Zemax designs. Microscope entries are restricted to standalone objectives; tube lenses, condensers, Fourier-imaging trains, and complete microscope systems are excluded. An external maintenance tool converts reviewed local source files once into native `.staropt` projects using the Workbench glass database; the desktop application only loads the finished library and shows per-lens parameters plus an interactive 2D optical layout.
- A standalone single-file Zemax installer, `Convert-Zemax-Lens.cmd`, converts one reviewed `.zmx` into the native checksummed `.staropt` format and atomically adds it to both the repository example library and the packaged **Database > Lens Library** index without rebuilding existing entries.
- An explicit **File > Export CAD** workflow that writes millimetre-based faceted STEP AP203 geometry from the sampled 3D lens scene. This exporter is currently an experimental mesh interchange path rather than a native analytic/NURBS B-rep kernel; verify generated files in the target CAD system before manufacturing use.
- .NET plugin discovery with geometry, material, and analysis registration.
- Chinese Avalonia GUI panels for lens editing, component editing, interactive 2D/3D system viewing, configurable graphical analysis, optimization, tolerancing, multi-configuration, and system properties, presented with a compact semi-flat desktop style.
- A dedicated **Manufacturing & Drawings** workspace with rule-based optical-element manufacturability review, ISO 10110-series Chinese drawing previews, editable tolerances and title blocks, and vector PDF export.
- Reproducible S.T.A.R. Labs desktop branding with packaged Windows/macOS application icons and a startup image that remains visible while the workspace session is restored.
- A linked category Ribbon: selecting a top category replaces the large-command region, with dedicated 2D/3D view commands and analysis commands grouped once under **分析** using the Zemax image-quality hierarchy. User-facing command and command-palette labels are consistently Chinese while stable English/Zemax keys remain internal compatibility identifiers. Buttons use content-driven minimum sizing and horizontal scrolling, so long titles remain readable without a fixed 78-by-66-pixel box.
- A Dock.Avalonia workspace with draggable document tabs, split panes, tab merging, native resizable floating windows, redocking, tiling/cascading commands, `Ctrl/Cmd+K` command palette actions, and per-file workspace sessions.
- A UI-free `OptilandWorkbench.Application` boundary. Avalonia panels depend only on immutable DTOs and application services; Core objects stay behind the application context, revision stream, undo/redo, snapshot, and cancellation boundary.
- Interactive analysis plots with pointer-centered wheel zoom, drag pan, double-click reset, and nearest-sample hover readout. Analysis pages expose **Plot / Data / Text** tabs at the bottom and keep graph settings in a collapsed-by-default panel with an icon-only synchronization action.
- Extended image analysis includes geometric, geometric-bitmap, light-source, partially coherent, and extended-diffraction calculations. The same Ribbon group also opens standalone Zemax IMA/BIM and common bitmap viewers; IMA/BIM data can be inspected as false color, grayscale, RGB, or individual channels.

## Requirements

- .NET SDK 10 or newer.
- Windows 10/11 or macOS for the primary target platforms.

## Run

One-click launchers:

- macOS: double-click `Run-Optiland.command`.
- Windows: double-click `Run-Optiland.cmd`.

First launch may take a moment because `dotnet run` restores and builds the project.

### Desktop workflow

- Use the top categories to switch the large-command Ribbon. Analysis commands have one semantic menu home—for example, **光程差图** is under **波前**, while **全视场像差** is under **像差分析**—and narrow windows expose the remaining commands through horizontal scrolling.
- Open **View** and choose **2D Layout**, **3D Layout**, or **Solid Model**. The 2D view uses outlined optical elements and colored rays; the 3D layout keeps a light engineering background, while the solid-model view uses a clean dark studio background and continuous dielectric glass driven by the catalog refractive index, Fresnel reflectance, and element-thickness attenuation. Highlighted optical surfaces and ray bundles remain visible through the glass.
- Open **Analysis**, choose an image-quality category, and then select the required method from its second-level menu. The selected analysis runs and opens its own closable result page.
- Open **Optimization** for grouped manual, automatic, and global workflows. Quick Focus applies a traced through-focus correction to the image-space thickness; Quick Adjust and Slider provide direct surface editing; the merit editor, wizard, and run commands use the marked radius/thickness variables. Global Optimization uses differential evolution, while Hammer Optimization uses basin hopping.
- Open **Database > Lens Library** to filter packaged microscope and industrial designs, inspect their basic parameters, and preview each native system in 2D. Library download and conversion are intentionally absent from the desktop application.
- Open **Manufacturing & Drawings** to review center/edge thickness, curvature, slope, and special-surface risks, or to prepare a Chinese ISO 10110-series reference drawing and export it as PDF.
- Use **File > Export CAD** for the current faceted STEP output. Treat it as an experimental exchange file and validate it in the receiving CAD application before downstream engineering use.
- Open **Tolerance** to generate editable tolerance operands with the wizard, validate the table, select RMS spot or RMS wavefront performance, and run sensitivity plus Monte Carlo analysis. Tolerance definitions can be saved as `*.startol.json`; completed results can be exported as a text report.
- In **System Options**, expand **Material Library** to move catalogs between **Current Glass Catalogs** and **Available Glass Catalogs**. The current list is ordered resolution priority for unqualified glass names and is saved with the optical project.
- Expand **Settings** only when parameters need adjustment, then use the adjacent synchronization icon to rerun the current page.
- Drag any document tab to split, merge, float, or redock it. Use **Window** for bulk docking, floating, tiling, cascading, locking, closing, and default-layout commands.
- In an analysis plot, use the wheel to zoom, drag to pan, double-click to reset, and hover near a sample to inspect its values. Flexible multi-pane results also provide an optional **Square panes** checkbox; it is off by default so the original auto-fill layout remains available.

Terminal:

```bash
AVALONIA_TELEMETRY_OPTOUT=1 dotnet run --project src/OptilandWorkbench.App/OptilandWorkbench.App.csproj
```

The telemetry opt-out environment variable is useful in sandboxed environments that cannot write Avalonia build logs under the user profile.

## Test

```bash
dotnet restore OptilandWorkbench.slnx --locked-mode
dotnet build OptilandWorkbench.slnx --no-restore /m:1 /nr:false
dotnet test tests/OptilandWorkbench.Tests/OptilandWorkbench.Tests.csproj --no-build /m:1 /nr:false
```

In restricted sandboxes, VSTest may need permission to bind a local socket. The
validated baseline as of 2026-07-30 is a zero-warning solution build and 577
passing tests; details are recorded in
[Build and release](docs/BUILD_AND_RELEASE.md).

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
- [Large-scale ray tracing performance](docs/RAY_TRACING_PERFORMANCE.md)
- [Zemax sequential operand support specification](docs/ZEMAX_OPERAND_SUPPORT.md)
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

Manual Zemax import sources are centralized under [`local-data/lens-library/originals/user-zmx/project/samples/lenses`](local-data/lens-library/originals/user-zmx/project/samples/lenses), including angle, finite-object-height, and Zemax real-image-height systems backed by bundled catalog glass. Converted `.staropt` viewer samples remain under [`samples/lenses`](samples/lenses).

Public downloads remain in the ignored adjacent `user-zmx/public/` tree. Use
`tools/Sync-Public-ZemaxCorpus.ps1` for the open-data providers and
`tools/Sync-DanReileyLensExchange.ps1` for the public-domain Dan Reiley Lens
Design Exchange mirror; the common importer reads both generated manifests.

## Notes

This repository is a clean-room .NET implementation shaped by the Optiland documentation. The parity claim is intentionally limited to the source-derived Cooke/Tessar contracts documented under `docs/`. Remaining work includes Forbes/NURBS/grid-sag freeform JSON, diffraction efficiency, full thin-film TMM, vectorial diffraction methods, broader commercial-format and Python-JSON coverage, non-sequential tracing, and optional GPU/autograd backends.
