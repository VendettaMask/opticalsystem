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

The Avalonia application is intentionally connector-centered:

```text
MainWindow
  OptilandConnector
    LensEditorPanel
    SystemPropertiesPanel
    ViewerPanel
    AnalysisPanel
    OptimizationPanel
```

Panels should not directly replace the active `Optic`; they call connector methods that trigger status updates and change events.

## Persistence

Native persistence uses schema-versioned JSON snapshots. Commercial format support uses a common sequential lens subset, mapped to ZMX/SEQ/LEN syntax by format adapters.
