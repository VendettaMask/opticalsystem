# Architecture

Optiland Workbench follows the public Optiland architecture at module-boundary level while remaining a pure .NET implementation.

## Core Object Model

`Optic` is the central object. It owns:

- `SystemAperture`
- fields and wavelengths
- `SurfaceGroup`
- backend provider
- material registry
- real and sequential ray tracers
- paraxial and aberration services
- pickups and solves
- analysis, optimization, tolerancing, and multi-configuration entry points

`Optic` is owned by `OptilandWorkbench.Application`; Avalonia code cannot access it. UI edits use application commands so validation, undo/redo, pickup/solve refresh, revision updates, and structured invalidation remain on one path.

## Surface Composition

Each `OpticalSurface` retains table-friendly properties such as radius, thickness, material, coating, semi-diameter, conic, and stop flag. The architecture-level model is composition based:

```text
OpticalSurface
  Geometry
  MaterialBefore
  MaterialAfter
  CoatingModel
  InteractionModel
  PhysicalAperture
  ScatteringModel
  CoordinateSystem
```

`MaterialRegistry` resolves custom materials, an embedded 1,740-entry compatibility database generated from Optiland 0.5.8/refractiveindex.info CC0 data, and a bundled database converted from 63 Zemax AGF catalogs. `ZemaxAgfCatalogReader` preserves the official catalog, general, coefficient, thermal, mechanical, durability, wavelength, internal-transmission, and stress-birefringence records, including legacy missing-value and shortened-record variants found in real Glasscat data. `CatalogGlassMaterial` evaluates all 13 Zemax dispersion formulas in addition to the compatibility formula 1/2/3/5 and tabulated models. The source AGF files are converted once to the schema-versioned, GZip-compressed Workbench `.ogdb` format and embedded in Core. Each optic stores an ordered current-catalog list; `MaterialRegistry` uses it when an unqualified glass name is ambiguous, while catalog enumeration remains independent so the database view never hides same-named records. The list is snapshot/undo state, and Zemax `GCAT` imports and exports it. Unknown names do not silently become constant-index glass.

Legacy table fields are synchronized into composition objects for normal table edits. JSON load can restore rich component snapshots without losing component-specific fields.

## Geometry Coverage

Geometry implementations expose the same `IGeometry` contract:

```csharp
double Sag(double x, double y);
double? DistanceToIntersection(Vector3D origin, Vector3D direction);
Vector3D SurfaceNormal(Vector3D localPoint);
```

Analytic surfaces and freeforms share Newton intersection fallback for consistent sequential tracing. Standard/conic geometry treats zero or infinite radius as a Zemax-style plane for sag, intersection, and local surface normal calculations, so public Core API and plugin-created `StandardGeometry(0)` instances stay equivalent to `PlaneGeometry`. Standard conics and Zemax-style `BiconicGeometry` also keep the explicit square-root sag domain: when the root argument is negative, sag returns `NaN` and intersections are rejected instead of extending a nonphysical branch. The current implementation does not enable Zemax-style hyperhemispheric/extrapolated behavior as an implicit default; that would need a separate geometry mode. `BiconicGeometry` follows the Zemax/OpticStudio shared-root biconic sag equation; Python Optiland 0.5.8 separable biconic dictionaries are preserved with `SeparableBiconicGeometry` instead of being silently reinterpreted as Zemax biconics. Current coverage includes Chebyshev, Zernike, Forbes Q, and planar/standard grating models with schema-versioned JSON round-trip. Remaining NURBS and grid-sag work can be added behind the same contract without changing the tracer or GUI connector.

## Backend Layer

`INumericBackend` remains the compatible scalar abstraction for backend-aware numeric operations. The optional `IBatchedNumericBackend` adds structure-of-arrays kernels for direction normalization, propagation, plane/standard intersection, circular-aperture clipping, refraction, ordinary reflection, and total internal reflection. `ManagedCpuBackend` implements these kernels with `System.Numerics.Vector<double>` SIMD and scalar tail handling. A third-party scalar backend is automatically exposed through a scalar batch adapter, so existing plugins do not need to change.

The provider remains extensible for later GPU or automatic-differentiation backends, but the current implementation is managed CPU only. GPU and derivative propagation are deliberately outside this compatibility-focused phase.

## Ray Tracing

Optical interaction is sequential by surface, while rays within a surface are processed in deterministic index ranges:

1. Generate field/wavelength/pupil samples.
2. Aim rays with the selected ray aiming strategy.
3. Transform rays into each surface coordinate system.
4. Intersect the surface geometry.
5. Clip through the physical aperture.
6. Apply interaction, coating, and scattering models.
7. Retain only the requested surface samples.

`TraceRequest` selects one of three retention modes: `FinalOnly`, `SelectedSurfaces`, or `FullHistory`. The compatibility `Trace()` and `TraceFinalSamples()` methods wrap this path. Unless full history and recording are explicitly requested, tracing does not populate `SurfaceGroup.RecordedTrace`. `RequestedTrace` owns one pooled flat sample buffer; its ray and surface views address that same storage, and legacy object samples are materialized only at API boundaries.

Active ray state uses pooled SoA buffers for position, direction, wavelength, intensity, path, OPL/OPD, polarization, liveness, normalization, and current material. The tracer freezes a cloned read-only surface context, runs a surface-major serial or chunked-parallel loop, and uses the batched backend for supported common surfaces. Complex geometry, apertures, coatings, scattering, and GRIN propagation fall back to the scalar state path. OPD reference reduction is performed in ray-index order and updates the retained buffer in place.

Interaction results distinguish transmission, ordinary reflection, and total internal reflection. Only transmission advances the current material; reflection and total internal reflection retain the incident medium. Directions emitted by a thin lens are normalized before the next surface, keeping geometric path, OPL, and absorption based on physical distance.

The standard centered sequential refractive path is validated point-for-point against the Python 0.5.8 Cooke and Tessar samples. GRIN curved-ray intersection, non-sequential propagation, and broader polarization/coating behavior remain outside that validated path. See [Large-scale ray tracing performance](RAY_TRACING_PERFORMANCE.md) for API, lifetime, fallback, and benchmark details.

Visualization layout is generated in core before Avalonia rendering. `Layout2DBuilder` samples each surface with `Sag(0, y)` in the YZ projection, groups lens surfaces by material transitions, extends smaller-aperture lens faces to the group's maximum extent before closing the 2D body, builds 3D surface rims/meridians from the same geometry, and draws ray paths from `SequentialRayTracer` histories so clipped or vignetted rays stop at the recorded failure point. The GUI maps those scene primitives to pixels and owns only pens, colors, projection, and interaction.

## Analysis

All analyses inherit from `BaseAnalysis` and implement:

```csharp
AnalysisData GenerateData();
AnalysisData GenerateData(CancellationToken cancellationToken);
```

The parameterless entry point remains the compatibility contract. Long-running GUI work enters through the cancellation-aware overload, and the principal ray-generation and tracing loops observe the current computation token.

`AnalysisData` separates numerical reporting from presentation:

- `Values` drives metric tables, text export, and automation.
- `Series` / `SeriesList` carries ordered line, scatter, bar, heatmap, raster, and value-colored-line data.
- `PlotPanes` carries Python-style field, wavelength, focus, component, and cross-section layouts.
- `AnalysisPlotOptions` carries limits, aspect, legends, zero references, grids, and axis visibility.

Thirty analysis views have source-derived Python 0.5.8 contracts on Cooke and Tessar. The shared engines include chief-ray, centroid-sphere, and best-fit-sphere wavefront OPD, Fringe Zernike fitting, FFT/MMDFT/Huygens PSF, FFT/Huygens/sampled/geometric MTF, radiometric histograms, distortion, and spatially variable image simulation. Diffraction PSF engines treat traced `Intensity` as power/flux and convert it to field amplitude with `sqrt(Intensity)` before coherent FFT, MMDFT, or Huygens summation, so coatings, absorption, vignetting, and apodization are not squared again. Sampled MTF evaluates a complex-pupil autocorrelation with the same fitted continuous wavefront on both pupil points, normalizes by the actual zero-frequency OTF, and maps Tangential/Sagittal to Zemax Y/X pupil shifts without changing FFT MTF or Contrast Loss Map directions. Core constructors and `AnalysisCatalog` retain general-purpose defaults. Application-layer factories own Workbench product presets, while parity tests pass the settings captured from `123456.ZMX` explicitly; a captured-file value is never treated as a universal Zemax specification.

Golden data is generated by `tools/python-reference/generate_analysis_reference.py` and checked point-for-point or pixel-for-pixel by `PythonAnalysisParityTests`. Claims outside those fixtures are listed explicitly in `PYTHON_ANALYSIS_PARITY.md` rather than inferred from class names.

## Optimization And Tolerancing

Optimization uses:

- `OptimizationProblem`
- `IOptimizationVariable`
- `Operand`
- `IVariableScaler`
- `IOptimizer`
- `OptimizerCatalog`

The application optimization service also owns bulk radius/thickness variable
selection and traced quick focus. The Optimization Ribbon exposes manual
adjustment, the merit-function editor and wizard, LM/DLS execution, and the
population-search entries backed by differential evolution and basin hopping.
The slider edits one surface value at a time through `IPrescriptionService`, so
undo, revision publication, solves, and automatic semi-diameter updates follow
the same path as lens-editor edits.

`Glass Replacement Template` currently opens the glass catalog together with a
default merit-function workflow for reviewed manual substitutions. It is not an
automatic glass-selection optimizer and does not claim parity with Zemax Glass
Expert.

Tolerancing reuses optimization variables and operands through:

- perturbations
- samplers
- compensators
- sensitivity analysis
- seeded Monte Carlo

Monte Carlo trials are independent: each worker restores its own `Optic` from the nominal snapshot, uses a trial seed derived from the global seed and trial index, and writes back by trial index. Results are therefore stable across supported parallelism levels. Cancellation and maximum parallelism are explicit, while inner tracing is suppressed when an outer Monte Carlo or parallel-Jacobian loop already owns parallel execution.

## Application Boundary

`OptilandWorkbench.Application` has no Avalonia or Dock dependency. It owns the active Core model and exposes only interfaces and immutable DTOs:

```text
WorkbenchApplication (composition and lifecycle root)
  WorkspaceCoordinator
    revision, mutation, cancellation, and event policies
    OpticContext
      OpticalWorkspaceModel
        Core Optic
  OpticalDocumentService
  PrescriptionService
  AnalysisService
  VisualizationService
  OptimizationService
  TolerancingService
  MultiConfigurationService
  MaterialCatalogService
  LensLibraryService
  CadExportService
```

`WorkbenchApplication` only constructs and exposes the independent services. `WorkspaceCoordinator` serializes mutations, controls document-lifetime cancellation, increments a monotonic model revision, and publishes one categorized event per command. Analysis and visualization run against Core snapshots rather than the live model. Public App-facing APIs are checked by architecture tests so Core types cannot leak back into Avalonia.

The former large connector implementation is split by responsibility under `OpticalWorkspaceModel`. Production services depend on that model; `OptilandConnector` is now only a thin source-compatibility facade for older callers and tests. Chinese names, icons, formatting, controls, and other presentation choices remain in the App layer.

## GUI And Dock Workspace

The Avalonia application is localized for Chinese display and consumes only Application services:

```text
MainWindow
  ActionManager
  PanelManager
    WorkspaceDockFactory
      ToolDock: SystemPropertiesPanel
      DocumentDock
        LensEditorPanel
        ViewerPanel 2D / 3D
        AnalysisPanel instances
        Optimization / Tolerancing / MultiConfiguration
  AppSettings
  IWorkbenchApplication
```

`WorkspaceDockFactory` supplies stable IDs, content rehydration, and Dock.Avalonia model creation. Tabs can be dragged into top/bottom/left/right panes, merged into another pane, floated into native resizable windows, and docked again. Only Lens Data opens in a new default workspace; standard analysis commands focus an existing instance while clone/new-page commands create an independent GUID-backed instance.

`PanelManager` delegates ownership and drag/drop behavior to Dock instead of creating Avalonia `Window` objects or transferring tabs manually. It provides bulk dock, float, tile, cascade, close, lock, and layout commands. `MainWindow` owns application commands, file dialogs, appearance, and top-level lifecycle.

`MainWindow` is physically split into lifecycle, action, shell, document, workspace, and import files. `ActionManager` registers menu, toolbar, and command-palette actions from one source so future panels can expose commands without duplicating event wiring. It also catches command failures and routes them to one application error surface. `AppSettings` retains window appearance, the legacy left-pane width migration value, and per-analysis GUI defaults.

`LocalIcon` renders the pinned Lucide catalog embedded under `Assets/Icons`; GUI commands therefore use one vector icon vocabulary without a runtime network or font dependency. See `LOCAL_ICONS.md` for usage and update rules.
Theme rendering is palette-driven. `src/OptilandWorkbench.App/Theming/ThemePalette.cs` owns the shared light and OpticStudio-inspired dark color tokens, while `src/OptilandWorkbench.App/Theming/IsekaiTheme.cs` independently owns the sword-and-magic “异世界” palette and gold accent resources. All three export Avalonia resource dictionaries through `ThemeResourceBindings`. The palettes include primary, secondary, muted, disabled, accent, on-accent, warning, error, and success text roles plus warning/error/success semantic surfaces; global windows, buttons, input controls, lists, grids, system-property cards, plots, wavefront views, and 2D/3D scenes consume dynamic resources. Layout, UI copy, Ribbon ordering, analysis-series colors, and analysis DTOs remain theme-independent. A new theme must define every `ThemeResourceBindings` brush in its resource dictionary before adding any theme-specific rendering branch.

The analysis panel consumes structured DTO data. Its lifecycle, parameter editor, result presentation, plot layout, and export logic are split into focused files. Metric, graph, and report views are built without parsing display strings. Each analysis document owns one settings instance and bottom-aligned Plot/Data/Text result views. Heavy analyses do not rerun on ordinary edits: the event revision marks the result stale, and the icon-only synchronization action starts a cancellable snapshot calculation. Instance ID, generation, and source revision must all match before a result is accepted.

The analysis catalog currently contains 67 entries. Encircled-energy variants
share common curve construction while diffraction energy integrates FFT PSF
samples and adds a polychromatic diffraction-limit reference. Extended image
analysis is split into geometric, bitmap, light-source, partially coherent, and
extended-diffraction engines. IMA/BIM and ordinary bitmap viewers are file
utilities owned by the App layer rather than numerical analyses.

`CadExportService` snapshots the active optic and delegates sampled lens geometry
to the Core STEP writer. The current writer produces closed, consistently
oriented faceted B-reps in millimetres; it is deliberately documented as an
experimental exchange boundary, not an analytic CAD kernel.

2D/3D views are lightweight consumers. They debounce model events by approximately 120 ms, cancel superseded requests, and apply only the newest matching revision. Locking a document freezes its current result; unlocking a lightweight view refreshes immediately, while a heavy analysis still waits for explicit synchronization.

The tolerancing panel exposes a Zemax-style tolerance-data workflow. Its wizard generates editable TRAD/TTHI/TEDX/TEDY/TETX/TETY/TIND/TABB/COMP rows across a selected surface range, with normal or uniform statistics. Rows are validated before execution and can be saved to or loaded from the native `*.startol.json` interchange format. CPU analysis evaluates both tolerance limits for sensitivity, runs deterministic seeded Monte Carlo trials, optionally refocuses through an image-distance compensator, and reports nominal/mean/sigma/min/max/P50/P90/P95/yield statistics for RMS spot-radius or RMS-wavefront criteria. Text reports can be exported from the panel. The multi-configuration panel exposes configuration creation, activation, and linked/unlinked thickness edits through the existing `MultiConfiguration` model.

## Persistence

Native persistence uses schema-versioned JSON snapshots. `ZemaxZmxReader` maps the Python Optiland 0.5.8 sequential Zemax parser/converter boundary into Core models, including encoding detection, system data, supported geometry, materials, and coordinate breaks. CODE V and OSLO continue to use the common sequential subset adapters.

Dock sessions are separate from optical files. The global default is stored at `%APPDATA%\OptilandWorkbench\workspace-default.json`; a saved optical file uses the SHA-256 hash of its normalized absolute path under `%APPDATA%\OptilandWorkbench\sessions`. A session stores the Dock graph, open document descriptors, analysis keys/settings/instance IDs, active document, lock state, and floating bounds, but never large calculation results.

Layout changes are saved after a 500 ms debounce and flushed during application shutdown. File switching saves the outgoing session before restoring the incoming one. Unsupported or damaged sessions are backed up and replaced by the default layout, unknown analyses are skipped, and floating bounds are clipped to the current primary working area. The legacy `LeftPaneWidth` migrates to ToolDock proportion; legacy tab indices are intentionally ignored.
