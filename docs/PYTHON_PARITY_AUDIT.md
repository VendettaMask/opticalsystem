# Python Optiland Parity Audit

Initial audit: 2026-07-11

Current status update: 2026-07-13

Baseline checked:

- Python package: `optiland 0.6.0` source distribution from PyPI.
- Source repository provenance: `optiland/optiland`, tag `v0.6.0`, commit `f854a0bf5145f7931a40a3da77191c2b3e955745`.
- Documentation cross-check: ReadTheDocs latest developer guide, currently labeled `0.5.8`.
- Executable numerical/analysis golden baseline: pinned `optiland==0.5.8` Cooke Triplet and Tessar Lens fixtures.

## Verdict

The .NET implementation is not a complete numerical or behavioral replacement for Python Optiland.

It is now substantially beyond the original skeleton: the standard Cooke/Tessar sequential path, Python JSON subset, and 27 analysis views have source-derived golden contracts. Optimization, tolerancing, visualization, and the Chinese Avalonia workbench are functional but do not match Python's full breadth. Parity is therefore claimed per documented method and fixture, never for the repository as a whole.

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

- Fields are angle records with Python-compatible maximum-radial-field normalization, but object-height, paraxial-image-height, and real-image-height field types are not implemented.
- Wavelengths are stored in nanometers internally with explicit micrometer API conversion boundaries.
- There is no equivalent `OpticUpdater` layer that updates paraxial data, surface normalization, pickups, solves, and geometry-derived fields in one route.
- Polarization and apodization exist only partially or as placeholders.

Required fix:

Add the remaining field types and group behavior, then introduce an `OpticUpdater`-equivalent route for coordinated paraxial, solve, pickup, and geometry refresh.

### 2. Ray Tracing

Python tracing flow:

1. `RealRayTracer.trace(Hx, Hy, wavelength, num_rays, distribution)`
2. Generate normalized field and pupil samples.
3. `RayGenerator.generate_rays(...)` uses a ray aiming strategy.
4. `optic.surfaces.trace(rays)` calls each `Surface.trace`.
5. Each surface localizes rays, computes geometry distance, propagates through the pre-surface material, updates OPD, clips by physical aperture, applies interaction/coating/scattering, globalizes rays, and records x/y/z/L/M/N/intensity/opd arrays.
6. Rays propagate to the image surface through the final material.

The .NET tracing matches the validated sequential sample path but not Python's execution model:

- It traces per `RealRay` list rather than backend arrays.
- `Optic.Trace` and `TraceGeneric` expose normalized field/pupil coordinates and micrometer wavelengths; field normalization uses Python's maximum radial field.
- Per-ray histories are adapted into surface-major `x/y/z/L/M/N/intensity/opd/OPL` records rather than being native backend arrays.
- Homogeneous propagation is material-owned, but GRIN intersection still starts from a straight-line surface distance.
- Chief-ray, centroid-sphere, and best-fit-sphere reference OPD strategies are validated on the Cooke/Tessar fixtures.

Required fix:

Retain the validated public trace contract while adding backend-array execution, complete field/vignetting behavior, GRIN curved-ray intersection, and the remaining reference strategies.

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
- Spot diagram, encircled energy, RMS spot size versus field, RMS wavefront versus field, ray fan, best-fit ray fan, pupil aberration, through-focus spot diagram, through-focus sampled MTF, both incident-angle-versus-height scans, incoherent irradiance, radiant intensity, Y-Ybar, distortion, grid distortion, field curvature, chief-ray/centroid-sphere/best-fit-sphere wavefronts, Fringe Zernike, FFT PSF, FFT MTF, geometric MTF, and sampled MTF now match Python 0.5.8 numerical algorithms and data contracts. Cooke and Tessar fixtures verify every deterministic sample, fitted sphere parameter, coefficient, heatmap pixel, PSF pixel, and MTF point.
- The remaining analyses still do not match all Python defaults, reference choices, or alternative physical methods.
- Image simulation and Jones pupil now use source-derived numerical/data/display contracts with Cooke and Tessar golden tests. Alternative wavefront strategies and non-FFT diffraction methods remain separate follow-up work.

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

- It has Chinese panels, connector change signals, metric/graph/report analysis views, numbered multi-analysis pages, and clone/close behavior.
- It does not yet have dynamic analysis parameter editors, analysis settings save/load, detachable docking, scripting terminal behavior, toast/command registry depth, or true VTK-like 3D interaction.
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

The .NET native snapshot remains a separate schema. A Python Optiland 0.5.8 adapter now imports and exports the validated angle-field sequential subset, including system apertures, wavelengths, plane/standard surfaces, catalog/ideal/Abbe materials, radial/rectangular apertures, transforms, and refractive/reflective interactions. Freeforms, coatings, BSDFs, phase/diffractive models, polarization, apodization, pickups, solves, and multi-configuration remain incomplete.

Required fix:

Extend the existing Python DTO path component by component, keeping unsupported types explicit rather than silently replacing them.

## Current Next Milestones

Completed foundations include normalized trace APIs, surface-owned tracing, surface-major recorded data, Python JSON subset round-trips, Python-compatible field/pupil distributions, 27 source-validated analysis views, and Cooke/Tessar golden suites.

The next implementation order is:

1. Add Huygens-Fresnel and MMDFT PSF/MTF contracts with bounded public fixtures.
2. Add dynamic analysis parameter editors and analysis settings persistence in the GUI.
3. Extend Python JSON interoperability to freeforms, coatings, BSDFs, phase/diffractive models, solves, pickups, polarization, and apodization.
4. Integrate GRIN propagation with curved-ray intersection and add explicit GRIN golden systems.
5. Expand field definitions beyond angle fields and complete vignetting/telecentric behavior.
6. Rework visualization toward canonical trace arrays, projection modes, aperture overlays, sag inspection, and higher-performance 3D rendering.
7. Deepen optimization/tolerancing parity with manager-based variables/operands, batched evaluation, material variables, and broader compensators.
8. Add optional backend-array/GPU/autograd execution without changing the validated managed public contract.

Every new parity claim must add a pinned Python generator output and a .NET point/pixel/parameter comparison before this list is updated.

## Repair Progress

### 2026-07-11 Trace API Foundation

Implemented the first compatibility layer:

- `Optic.Trace(Hx, Hy, wavelengthMicrometers, sampleCount, distribution)`
- `Optic.TraceGeneric(Hx, Hy, Px, Py, wavelengthMicrometers)`
- normalized coordinate validation for field and pupil inputs
- micrometer/nanometer conversion helpers
- `Wavelength.Micrometers`
- `SurfaceTraceData` and `SurfaceTraceRecord`, matching Python's surface-major recorded array shape
- `SurfaceGroup.RecordedTrace` as the latest per-surface trace record
- distributions for `line_x`, `line_y`, and `ring` in addition to existing grid/hexapolar/random/Sobol-like sampling

This is still an adapter over the current simplified sequential tracer. It fixes the public trace shape and unit boundary first; the underlying physics kernel still needs to be replaced with Python-equivalent `Surface.Trace`, propagation model, reference OPD, and backend-array behavior.

### 2026-07-11 Surface Trace Kernel Split

Moved the single-surface real-ray kernel into `OpticalSurface.TraceRay(...)`:

- local/global coordinate conversion now lives with the surface
- geometry intersection, segment length, OPL accumulation, aperture clipping, interaction, coating, scattering, and sample creation are performed in one surface-owned method
- `SequentialRayTracer` now iterates surfaces and delegates the per-surface physics instead of owning the whole kernel
- tests cover single-surface intersection, cumulative OPL, refractive-index state handoff, and aperture clipping

This is structurally closer to Python `Surface.trace()` / `_trace_real()`. Remaining gaps: material-owned propagation models, backend-array tracing, exact per-surface recorder arrays during propagation, polarization-dependent coatings, and Python reference OPD strategies.

### 2026-07-12 Analysis And Display Parity Expansion

Added source-derived implementations and Cooke/Tessar golden tests for:

- RMS wavefront error versus normalized field
- sampled through-focus tangential and sagittal MTF, including exit-pupil geometry and cubic plot interpolation
- incident angle versus image height through pupil and through field
- incoherent irradiance on an explicit circular or rectangular detector aperture

The plot contract now includes value-colored curves, per-series viridis/inferno/jet color maps, fixed or automatic color limits, and colorbars for both heatmaps and colored lines. Incoherent irradiance intentionally preserves Python's requirement for an explicit detector physical aperture; the GUI reports that requirement instead of substituting the visual semi-diameter.

### 2026-07-12 Radiometric And Geometric MTF Expansion

- Added radiant-intensity angle-space histograms at a selectable reference surface, Python's W/sr solid-angle conversion, shared absolute color limits, jet heatmaps, and central cross-sections.
- Added Python-compatible uniform pupil grids and geometric MTF from spot-histogram Fourier integrals with optional diffraction-limit scaling.
- Cooke and Tessar golden tests compare all radiant-intensity pixels, cross-section points, and geometric tangential/sagittal MTF samples.

### 2026-07-12 Best-Fit Sphere And Sampled MTF Expansion

- Added Python's tilt-corrected three-dimensional wavefront point construction and four-parameter least-squares best-fit sphere.
- Added BestFitRayFan with primary-wavelength fitted centers and no chief-ray distortion recentering.
- Added an efficient reusable sampled-MTF evaluator and a full tangential/sagittal frequency-sweep analysis.
- Golden tests compare all sphere centers/radii, ray-fan points, and sampled-MTF values for Cooke and Tessar.
- Corrected normalized angle-field conversion across tracing, analysis field selection, and wavefront tilt removal to use Python's maximum radial field magnitude.
