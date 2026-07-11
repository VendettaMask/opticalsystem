# Python Optiland Parity Audit

Date: 2026-07-11

Baseline checked:

- Python package: `optiland 0.6.0` source distribution from PyPI.
- Source repository provenance: `optiland/optiland`, tag `v0.6.0`, commit `f854a0bf5145f7931a40a3da77191c2b3e955745`.
- Documentation cross-check: ReadTheDocs latest developer guide, currently labeled `0.5.8`.

## Verdict

The current .NET implementation is not numerically or behaviorally equivalent to Python Optiland.

It has a useful Optiland-shaped skeleton: central `Optic`, surface composition, basic ray tracing, analyses, JSON, GUI panels, optimization and tolerancing placeholders. But many implementations are simplified approximations. Some class and feature names match Python Optiland, but the underlying data model, algorithms, default parameters, and GUI behavior do not yet match.

Going forward, features should not be marked as "parity" until they pass source-derived parity tests against Python Optiland behavior.

## Major Mismatches

### 1. Optic Object Model

Python `Optic` owns:

- `aperture: BaseSystemAperture | None`
- `surfaces: SurfaceGroup`
- `fields: FieldGroup`
- `wavelengths: WavelengthGroup`
- `paraxial`, `aberrations`, `ray_tracer`
- `polarization`, `apodization`
- `pickups`, `solves`, `obj_space_telecentric`, `updater`

The .NET `Optic` has similar entry points but not the same model:

- Fields are only simple angle records, not Python's selectable field types and normalized field coordinate model.
- Wavelengths are stored in nanometers, while Python APIs use micrometers internally.
- There is no equivalent `OpticUpdater` layer that updates paraxial data, surface normalization, pickups, solves, and geometry-derived fields in one route.
- Polarization and apodization exist only partially or as placeholders.

Required fix:

Create true equivalents of `FieldGroup`, `WavelengthGroup`, `OpticUpdater`, aperture classes, and explicit unit conversion boundaries.

### 2. Ray Tracing

Python tracing flow:

1. `RealRayTracer.trace(Hx, Hy, wavelength, num_rays, distribution)`
2. Generate normalized field and pupil samples.
3. `RayGenerator.generate_rays(...)` uses a ray aiming strategy.
4. `optic.surfaces.trace(rays)` calls each `Surface.trace`.
5. Each surface localizes rays, computes geometry distance, propagates through the pre-surface material, updates OPD, clips by physical aperture, applies interaction/coating/scattering, globalizes rays, and records x/y/z/L/M/N/intensity/opd arrays.
6. Rays propagate to the image surface through the final material.

The .NET tracing currently does not match this:

- It traces per `RealRay` list rather than backend arrays.
- It does not expose Python-style `trace(Hx, Hy, Px, Py, wavelength)` or `trace_generic`.
- Field and pupil coordinates are not normalized like Python.
- Surface recording is per ray history, not per-surface arrays matching `surfaces.x/y/z/L/M/N/intensity/opd`.
- Propagation is simplified and not delegated through `material_pre.propagation_model`.
- OPD is mean-final-OPL normalized, while Python wavefront OPD is strategy based and reference-geometry based.

Required fix:

Rebuild tracing around Python-compatible `RealRays` arrays, `SurfaceGroup.Trace`, `Surface.Trace`, `trace`, and `trace_generic`. Keep the current history model only as an adapter for GUI visualization.

### 3. Analysis Framework

Python analyses generate data in constructors through `BaseAnalysis._generate_data()`, store structured data, and provide plot/view behavior. Several defaults are materially different from our .NET version:

- `SpotDiagram` resolves fields/wavelengths, supports global/local coordinates, chief-ray or centroid reference centering, Airy disk calculation, and returns nested field/wavelength `SpotData`.
- `EncircledEnergy` inherits from `SpotDiagram`, defaults to primary wavelength, random distribution, and `100_000` rays.
- `RMS vs Field` samples a normalized field sweep, usually 64 fields, instead of aggregating only the currently defined field list.
- `ThroughFocusAnalysis` moves the image surface geometry z position, runs a subclass analysis at each position, then restores nominal focus.
- `Wavefront` uses reference strategies such as chief ray, centroid sphere, best-fit sphere, optional tilt removal, and reports OPD in waves.
- `PSF` and `MTF` have FFT, Huygens-Fresnel, vectorial, sampled, and geometric implementations.

The .NET analyses are currently mixed:

- Spot/encircled energy/RMS-vs-field/through-focus/wavefront now consume sequential ray samples, which is better than pure placeholders.
- They still do not match Python defaults, data shapes, reference choices, normalized coordinates, or physical methods.
- PSF, MTF, Zernike, distortion, field curvature, pupil aberration, image simulation, and Jones pupil are still proxies or partial placeholders.

Required fix:

Port analysis defaults and data contracts first, then implement each numeric method. Do not use one summary value as a replacement for Python's field-by-wavelength data.

### 4. Visualization

Python 2D visualization:

- `Rays2D` traces and reads recorded surface arrays.
- `OpticalSystem` identifies lenses, mirrors, standalone surfaces, stops, and apertures based on material index transitions and interaction type.
- `Lens2D` samples sagged surfaces and extends smaller-aperture faces to the lens maximum extent before polygon closure.
- `Surface2D` respects physical aperture clipping and supports XY/XZ/YZ projections.
- 3D visualization uses VTK and can revolve symmetric contours or mesh asymmetric apertures.

The .NET viewer is closer than earlier commits but still not equivalent:

- It uses a custom `Layout2DBuilder` scene model rather than the same surface/ray recorded arrays.
- The 3D viewer is a lightweight Avalonia drawing/projection, not VTK-equivalent geometry interaction.
- Projection modes, aperture overlays, reference rays, tooltip model, sag viewer, and hide-vignetted behavior are incomplete.

Required fix:

Make visualization consume the same canonical traced arrays as analysis, then mirror Python component identification: `OpticalSystem`, `Lens2D/3D`, `Surface2D/3D`, `Rays2D/3D`.

### 5. GUI

Python GUI source contains:

- `MainWindow`, `ActionManager`, `PanelManager`
- `OptilandConnector` as a QObject facade
- service classes for files, surfaces, system properties, optimization, and analysis
- Lens editor, viewer, analysis panel, optimization panel, system properties panel
- command palette, sidebar, custom title bar, toast manager
- embedded Python terminal via qtconsole
- Matplotlib 2D plots and optional VTK 3D viewer

The .NET Avalonia GUI currently covers only part of that:

- It has Chinese panels and basic connector signals.
- It does not yet have equivalent dynamic analysis settings, multi-page analysis plotting, clone/save/load analysis pages, scripting terminal behavior, toast/command registry depth, or true VTK-like 3D interaction.
- Some Python GUI panels are service-driven; our connector still contains too much mixed domain/UI logic.

Required fix:

Split connector logic into service classes and implement GUI behavior by feature parity, not by current panel names.

### 6. Optimization, Tolerancing, Multi-Configuration

Python optimization includes:

- `OptimizationProblem` with managers for operands and variables.
- Batched ray evaluator.
- effective operand weighting through field and wavelength weights.
- many variable types: radius, reciprocal radius, norm radius, thickness, material, index, conic, decenter, tilt, asphere/freeform coefficients, grid sag, NURBS, Torch variables.
- SciPy and Torch optimizer families, including Glass Expert.

The .NET implementation has the surface-level shape but not equivalent depth:

- Variables and operands are basic delegates.
- Batched ray evaluation is not equivalent.
- Glass/material categorical optimization is not implemented.
- Tolerancing exists but does not match Python perturbation/compensator breadth.
- Multi-configuration is close in intent but incomplete relative to generic property linking and pickup behavior.

Required fix:

Implement Python's operand/variable managers and batched evaluator before adding more optimizers.

### 7. File Format

Python `to_dict/from_dict` serializes object graphs by each component's own type registry. The file handler can load/save any object that supports those methods, not only an `Optic`.

The .NET JSON snapshot is schema-oriented and useful, but not Python-compatible:

- Field/wavelength/aperture structures differ.
- Surface type and geometry dictionaries differ.
- Materials, coatings, interaction models, apertures, polarization, apodization, pickups, solves, and multi-config are incomplete.

Required fix:

Add a Python-compatible JSON DTO path beside the .NET schema, with round-trip tests using Python-generated sample files.

## Corrected Implementation Order

The old plan should be narrowed. The next milestones should be:

1. Build a source-derived parity matrix from Python `optiland 0.6.0`, including exact file/class/function mapping.
2. Rebuild `Optic`, `FieldGroup`, `WavelengthGroup`, `SurfaceGroup`, `OpticUpdater`, and unit conventions.
3. Rebuild `RealRays`, `PolarizedRays`, `ParaxialRays`, `RayGenerator`, ray aiming, `trace`, and `trace_generic`.
4. Rebuild `Surface.Trace` and per-surface recorded arrays.
5. Rebuild geometry intersection and propagation to match Python algorithms.
6. Port `SpotDiagram` exactly, including nested field/wavelength data, references, distributions, local/global coordinates, and Airy disk.
7. Port encircled energy, RMS vs field, through-focus, and wavefront on top of the exact spot/tracing data.
8. Replace current proxy PSF/MTF/Zernike with Python-equivalent physical implementations.
9. Rework visualization to consume canonical traced arrays and mirror Python `OpticalSystem`, `Lens`, `Surface`, and `Rays` classes.
10. Rework GUI services and panels to match Python GUI behavior within Avalonia.
11. Add Python-vs-.NET golden parity tests using small public sample systems.

## Immediate Code Tasks

- Stop labeling simplified analyses as parity complete.
- Add `PythonParityTests` that compare DTO shapes and simple trace outputs against checked-in golden JSON generated from Python Optiland.
- Replace current `AnalysisRunner.EvaluateSpotDiagram()` with a real `SpotDiagram` data object.
- Add normalized field and pupil coordinate APIs before modifying more analysis code.
- Add explicit unit conversion tests for micrometers vs nanometers.
- Update visualization tests to check lens closure against Python `Lens2D._extend_surface` behavior.

