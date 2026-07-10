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

Panels and application code should mutate an optic through `OptilandConnector`. This keeps undo/redo, pickup/solve refresh, status text, and GUI invalidation in one path.

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

Legacy table fields are synchronized into composition objects for normal table edits. JSON load can restore rich component snapshots without losing component-specific fields.

## Geometry Coverage

Geometry implementations expose the same `IGeometry` contract:

```csharp
double Sag(double x, double y);
double? DistanceToIntersection(Vector3D origin, Vector3D direction);
Vector3D SurfaceNormal(Vector3D localPoint);
```

Analytic surfaces and freeforms share Newton intersection fallback for consistent sequential tracing. Current freeform coverage includes Chebyshev, Zernike, and Forbes Q models with schema-versioned JSON round-trip. Remaining NURBS/grating work can be added behind the same contract without changing the tracer or GUI connector.

## Backend Layer

`INumericBackend` is the only supported abstraction for backend-aware numeric operations. `ManagedCpuBackend` is the default implementation. The provider is intentionally extensible for a later TorchSharp/GPU/autograd backend, but the current implementation is CPU-only.

## Ray Tracing

Ray tracing is sequential:

1. Generate field/wavelength/pupil samples.
2. Aim rays with the selected ray aiming strategy.
3. Transform rays into each surface coordinate system.
4. Intersect the surface geometry.
5. Clip through the physical aperture.
6. Apply interaction, coating, and scattering models.
7. Record per-surface history for analysis and visualization.

The current tracer favors deterministic, testable behavior over production optical accuracy.

## Analysis

All analyses inherit from `BaseAnalysis` and implement:

```csharp
AnalysisData GenerateData();
```

`AnalysisData.ExportText()` provides a simple export path for the current GUI. Several analysis classes currently produce proxy data; their class boundaries are in place so rigorous numerical methods can replace proxy formulas without changing GUI wiring.

## Optimization And Tolerancing

Optimization uses:

- `OptimizationProblem`
- `IOptimizationVariable`
- `Operand`
- `IVariableScaler`
- `IOptimizer`
- `OptimizerCatalog`

Tolerancing reuses optimization variables and operands through:

- perturbations
- samplers
- compensators
- sensitivity analysis
- seeded Monte Carlo

## GUI Layer

The Avalonia application is localized for Chinese display and remains intentionally connector-centered:

```text
MainWindow
  ActionManager
  AppSettings
  OptilandConnector
    LensEditorPanel
    SystemPropertiesPanel
    ViewerPanel
    AnalysisPanel
    OptimizationPanel
    TolerancingPanel
    MultiConfigurationPanel
```

Panels should not directly replace the active `Optic`; they call connector methods that trigger status updates and change events.

`ActionManager` registers menu, toolbar, and command-palette actions from one source so future panels can expose commands without duplicating event wiring. `AppSettings` persists theme, window size, split-pane width, and selected panel tabs under the user's application data folder.

The analysis panel now consumes structured connector data. It shows a metric table, keeps a text report view, and provides copy/export affordances while preserving the same analysis data generation path used by tests and future file exports.

The tolerancing panel exposes the current CPU tolerancing framework through sensitivity and Monte Carlo tables. The first GUI workflow perturbs selected surface radius/thickness, uses RMS spot radius as the merit proxy, and can compensate with image-surface thickness. The multi-configuration panel exposes configuration creation, activation, and linked/unlinked thickness edits through the existing `MultiConfiguration` model.

## Persistence

Native persistence uses schema-versioned JSON snapshots. Commercial format support uses a common sequential lens subset, mapped to ZMX/SEQ/LEN syntax by format adapters.
