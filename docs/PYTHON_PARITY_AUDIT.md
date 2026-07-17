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

It is now substantially beyond the original skeleton: the standard Cooke/Tessar sequential path, Python JSON subset, and 30 analysis views have source-derived golden contracts. Optimization, tolerancing, visualization, and the Chinese Avalonia workbench are functional but do not match Python's full breadth. Parity is therefore claimed per documented method and fixture, never for the repository as a whole.

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
- Polarization remains partial; all seven Python Optiland 0.5.8 apodization profiles now have root models and shared ray-generation behavior.

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
- Spot diagram, encircled energy, RMS spot size versus field, RMS wavefront versus field, ray fan, best-fit ray fan, pupil aberration, through-focus spot diagram, through-focus sampled MTF, both incident-angle-versus-height scans, incoherent irradiance, radiant intensity, Y-Ybar, distortion, grid distortion, field curvature, chief-ray/centroid-sphere/best-fit-sphere wavefronts, Fringe Zernike, FFT/MMDFT/Huygens PSF, FFT/Huygens MTF, geometric MTF, and sampled MTF now match Python 0.5.8 numerical algorithms and data contracts. Cooke and Tessar fixtures verify every deterministic sample, fitted sphere parameter, coefficient, heatmap pixel, PSF pixel, and MTF point.
- The remaining analyses still do not match all Python defaults, reference choices, or alternative physical methods.
- Image simulation and Jones pupil now use source-derived numerical/data/display contracts with Cooke and Tessar golden tests. Vectorial diffraction and broader analysis defaults remain separate follow-up work.

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

- It has Chinese panels, connector change signals, metric/graph/report analysis views, generated per-analysis parameter editors, persisted analysis settings, numbered multi-analysis pages, and clone/close behavior.
- It does not yet have detachable docking, scripting terminal behavior, toast/command registry depth, true VTK-like 3D interaction, or broad UI automation for those workflows.
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

The .NET native snapshot remains a separate schema. A Python Optiland 0.5.8 adapter now imports and exports the validated angle-field sequential subset, including EPD/image-F-number/object-NA/float-by-stop-size system apertures, wavelengths, plane/standard/planar-grating/standard-grating/biconic surfaces, representable toroidal surfaces, pure polynomial/Chebyshev/fringe Zernike surfaces, representable high-order even/odd aspheres, homogeneous catalog/ideal/Abbe materials, radial/annular/offset-radial, centered/asymmetric rectangular, offset elliptical, polygon/file-backed, and recursive union/intersection/difference physical apertures, all seven apodization profiles, transforms, refractive/reflective, non-reflective thin-lens, plane-surface phase interactions with all four Python profiles, transmissive/reflective diffractive interactions, and simple Python coating dictionaries on the Workbench adapter path. Python Optiland 0.5.8 itself may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, and its grating geometry dictionaries cannot reconstruct themselves, so those external Python retention paths are not claimed yet. Forbes/NURBS/grid-sag freeforms, Python standard/noll Zernike or finite base-radius Zernike terms, Python polynomial/Chebyshev finite base radius/conic terms, Python toroidal `conic_yz` and `coeffs_poly_y` terms, non-homogeneous material propagation models, Fresnel/polarized coatings, thin-film/TMM coating stacks, BSDFs, reflective thin-lens, polarization, pickups, solves, and multi-configuration remain incomplete.

Required fix:

Extend the existing Python DTO path component by component, keeping unsupported types explicit rather than silently replacing them.

## Current Next Milestones

Completed foundations include normalized trace APIs, surface-owned tracing, surface-major recorded data, Python JSON subset round-trips including planar/standard grating, BiconicGeometry, representable toroidal geometry, pure polynomial/Chebyshev/fringe Zernike geometry, representable high-order aspheres, homogeneous material dictionaries, radial/annular/offset-radial, rectangular, elliptical, polygon/file-backed, and recursive boolean physical apertures, all seven apodization profiles, non-reflective thin-lens, plane-surface phase, and diffractive interactions, and simple coating dictionaries on the Workbench adapter path, Python-compatible field/pupil distributions, 30 source-validated analysis views, generated analysis parameter editors with persisted settings, and Cooke/Tessar golden suites.

The next implementation order is:

1. Extend Python JSON interoperability to the remaining freeforms, Python-preserved coating models, BSDFs, solves, pickups, and polarization.
2. Integrate GRIN propagation with curved-ray intersection and add explicit GRIN golden systems.
3. Expand field definitions beyond angle fields and complete vignetting/telecentric behavior.
4. Rework visualization toward canonical trace arrays, projection modes, aperture overlays, sag inspection, and higher-performance 3D rendering.
5. Add vectorial PSF/MTF contracts and any remaining diffraction defaults not covered by FFT, MMDFT, Huygens, sampled, and geometric methods.
6. Deepen optimization/tolerancing parity with manager-based variables/operands, batched evaluation, material variables, and broader compensators.
7. Add optional backend-array/GPU/autograd execution without changing the validated managed public contract.
8. Add broad GUI automation for generated analysis parameters, persistence, detachable layout behavior, file dialogs, edits, themes, and command-palette navigation.

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

This is still an adapter over the current simplified sequential tracer. It fixes the public trace shape and unit boundary first; the underlying physics kernel still needs to be replaced with Python-equivalent `Surface.Trace`, material-owned propagation, and backend-array behavior.

### 2026-07-11 Surface Trace Kernel Split

Moved the single-surface real-ray kernel into `OpticalSurface.TraceRay(...)`:

- local/global coordinate conversion now lives with the surface
- geometry intersection, segment length, OPL accumulation, aperture clipping, interaction, coating, scattering, and sample creation are performed in one surface-owned method
- `SequentialRayTracer` now iterates surfaces and delegates the per-surface physics instead of owning the whole kernel
- tests cover single-surface intersection, cumulative OPL, refractive-index state handoff, and aperture clipping

This is structurally closer to Python `Surface.trace()` / `_trace_real()`. Remaining gaps: material-owned propagation models, backend-array tracing, exact per-surface recorder arrays during propagation, and polarization-dependent coatings.

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

### 2026-07-13 Huygens And MMDFT Diffraction Expansion

- Added Python source-derived MMDFT PSF, Huygens-Fresnel PSF, and Huygens-derived MTF engines.
- Registered MMDFT PSF, Huygens PSF, and Huygens MTF as structured analysis catalog entries.
- Golden tests compare every bounded Cooke/Tessar PSF pixel, Huygens MTF point, working F-number, pixel pitch, and Strehl convention.
- Corrected odd-size `fftshift` behavior so Huygens MTF matches NumPy for 9 by 9 reference grids.

### 2026-07-13 Python JSON Geometry Expansion

- Extended the Python Optiland JSON adapter beyond Plane/StandardGeometry to import and export BiconicGeometry.
- Added lossless Workbench-to-Python JSON round-trips for high-order EvenAsphere/OddAsphere coefficients by reserving Python's first departure term, which the current Workbench asphere model does not represent.
- Kept still-unsupported geometries explicit: Forbes, NURBS, and grid-sag geometries still fail rather than being silently flattened. The former grating guardrail was superseded by the validated expansion below.
- Regression coverage now verifies non-standard geometry round-trips and keeps unsupported geometry rejection in place.

### 2026-07-13 Python JSON Toroidal Geometry Expansion

- Added Python `ToroidalGeometry` dictionary import/export for the subset represented by Workbench's tangential and sagittal radii.
- Kept Python-only `conic_yz` and `coeffs_poly_y` terms explicit: imports reject them instead of flattening into the simpler Workbench toroidal model.
- Regression coverage now verifies toroidal round-trips and unsupported toroidal term rejection.

### 2026-07-13 Python JSON Polynomial Geometry Expansion

- Added Python `PolynomialGeometry` dictionary import/export for the pure XY-polynomial subset represented by Workbench coefficient pairs.
- Export writes an infinite base radius so Python's conic base term contributes zero sag.
- Kept Python finite base-radius polynomial geometries explicit: imports reject them instead of silently dropping the conic base.
- Regression coverage now verifies pure polynomial round-trips and unsupported polynomial base rejection.

### 2026-07-13 Python JSON Chebyshev Geometry Expansion

- Added Python `ChebyshevPolynomialGeometry` dictionary import/export for the pure normalized Chebyshev subset represented by Workbench coefficient pairs and X/Y normalization radii.
- Export writes an infinite base radius so Python's conic base term contributes zero sag.
- Kept Python finite base-radius Chebyshev geometries explicit: imports reject them instead of silently dropping the conic base.
- Regression coverage now verifies pure Chebyshev round-trips and unsupported Chebyshev base rejection.

### 2026-07-13 Python JSON Zernike Geometry Expansion

- Added Python `ZernikePolynomialGeometry` dictionary import/export for the pure `fringe` subset represented by Workbench `(n, m)` coefficient pairs and pupil radius.
- Export writes an infinite base radius so Python's conic base term contributes zero sag and the Workbench coefficient dictionary maps directly to Python's fringe coefficient list.
- Kept Python `standard`/`noll` Zernike types and finite base-radius Zernike geometries explicit: imports reject them instead of silently changing normalization or dropping the conic base.
- Regression coverage now verifies pure fringe Zernike round-trips and unsupported Zernike base/type rejection.

### 2026-07-13 Python JSON Simple Coating Expansion

- Added Python `SimpleCoating` dictionary import/export through the Workbench recursive JSON adapter.
- Preserved the richer Workbench thin-film stack model as unsupported in Python JSON instead of flattening it into a simple transmittance.
- Verified against Python Optiland 0.5.8 that the raw `SimpleCoating.to_dict()` shape matches, while `Optic.from_dict()` can relink surface coatings to Fresnel during surface-group reconstruction.
- Regression coverage now verifies simple coating round-trips and keeps unsupported coating rejection in place.

### 2026-07-13 Python JSON Thin Lens Interaction Expansion

- Added Python `ThinLensInteractionModel` dictionary import/export for non-reflective thin-lens surfaces, including focal length and existing raw coating dictionary support.
- Preserved reflective thin-lens behavior as unsupported because the current Workbench thin-lens interaction has no independent reflective flag.
- Regression coverage now verifies thin-lens interaction round-trips and unsupported reflective thin-lens rejection.

### 2026-07-13 Python JSON Physical Aperture Guardrails

- Added regression coverage for Python centered `RadialAperture` and `RectangularAperture` dictionary round-trips.
- Kept Python annular radial apertures and asymmetric rectangular bounds explicit until equivalent Workbench models were added.

### 2026-07-14 Python JSON Root Contract Guardrails

- Kept unsupported root-level Python contracts explicit: nonempty `pickups`, nonempty `solves`, non-ignored `polarization`, object-space telecentric apertures, and telecentric field groups fail during import instead of being ignored. The former apodization guardrail was superseded by the validated expansion below.
- Regression coverage verifies every guardrail while preserving import of the empty/default Python Optiland 0.5.8 root dictionaries used by the Cooke and Tessar fixtures.

### 2026-07-14 Python JSON Material Propagation Guardrails

- Kept unsupported material propagation explicit: Python material dictionaries with non-`HomogeneousPropagation` propagation models now fail during import instead of being silently flattened into homogeneous Workbench materials.
- Regression coverage verifies `GRINPropagation` rejection while preserving the homogeneous material dictionaries used by validated Python Optiland 0.5.8 samples.

### 2026-07-15 Python JSON System Aperture Guardrails

- Kept unsupported system aperture types explicit: Python aperture dictionaries outside the supported `EPD`, `imageFNO`, `objectNA`, and `float_by_stop_size` set now fail during import instead of being silently mapped to EPD.
- Regression coverage verifies unknown aperture-type rejection while preserving the validated Python Optiland 0.5.8 aperture modes.

### 2026-07-15 Python JSON Field Definition Guardrails

- Added source-validated regressions for Optiland 0.5.8 `ObjectHeightField` and `ParaxialImageHeightField` definitions.
- Both field definitions remain explicitly outside the angle-field adapter contract instead of risking a silent import as `AngleField`.

### 2026-07-15 Viewer Interaction And Rendering Alignment

- Aligned the 2D YZ viewport with Python's `axis("image")` behavior by using one physical scale for Z and Y, and moved the optical axis through the same pan/zoom transform as lenses and rays.
- Changed wheel zoom to preserve the world point under the pointer in both viewer tabs.
- Added selectable translucent-solid and wireframe rendering for the lightweight 3D projection, following Python's solid VTK lens actor as the default while retaining the diagnostic framework view.

### 2026-07-17 Python JSON Physical Aperture Expansion

- Added lossless models and Python dictionary import/export for annular `RadialAperture`, `OffsetRadialAperture`, asymmetric `RectangularAperture`, and offset `EllipticalAperture`.
- Added a pinned Optiland 0.5.8 generator fixture that compares every dictionary field and 28 Python `contains()` decisions against Workbench clipping.
- Extended native component snapshots and incoherent detector extents so offsets and inner radii survive beyond the Python adapter path.

### 2026-07-17 Python JSON Composite Aperture Expansion

- Added lossless Python dictionary import/export for `PolygonAperture`, file-backed polygon metadata, and recursive union, intersection, and difference apertures.
- Added explicit polygon-edge containment and recursive physical bounds shared by analysis detector extents and serialization.
- Extended the pinned Optiland 0.5.8 fixture with exact nested dictionaries and Python `contains()` decisions for five composite cases.
- Added optional child snapshots for recursive apertures while preserving compatibility with existing native snapshots.

### 2026-07-17 Python JSON Apodization Expansion

- Added native root models and exact Python dictionary import/export for uniform, Gaussian, cosine-squared, Hann, polynomial, super-Gaussian, and Tukey apodization.
- Applied the optic-owned profile consistently to ordinary bundles, generic rays, normalized traces, wavefront sampling, and diffraction sampling.
- Added system-properties controls for profile selection and profile-specific parameters, while preserving the distinction between no apodization and an explicit uniform profile.
- Added a pinned Optiland 0.5.8 fixture covering exact dictionaries, radial intensity samples, finite-radius boundaries, and the Tukey `alpha=0` limit.

### 2026-07-17 Python JSON Phase Interaction Expansion

- Replaced the zero-gradient placeholder with generalized-Snell real and paraxial interaction, phase-derived OPD shift, evanescence clipping, reflective mode, and profile efficiency.
- Added exact Python dictionary import/export and native snapshots for constant, linear-grating, radial, and grid phase profiles; phase interactions remain explicitly restricted to plane geometry as in Python.
- Implemented tensor-product not-a-knot cubic splines for grid phase, matching Python's SciPy `RectBivariateSpline` phase and gradient values.
- Moved surface interaction into local coordinates before globalizing the outgoing ray, which also corrects transformed thin-lens and diffractive axes.
- Added a pinned Optiland 0.5.8 fixture comparing profile values, gradients, paraxial gradients, transmissive/reflective direction cosines, intensity, and OPD ray by ray.

### 2026-07-17 Python JSON Diffractive Interaction Expansion

- Added planar and standard grating geometries with order, micrometer period, groove orientation, sag/intersection, surface normal, grating vector, editable GUI controls including reflective diffraction, and native snapshot support.
- Replaced the frequency-only placeholder with Optiland 0.5.8 real-ray grating diffraction and paraxial equations, including refractive indices, reflective mode, curved-surface projection, local coordinates, and non-propagating orders.
- Added Python dictionary import/export for `DiffractiveInteractionModel`, `PlaneGrating`, and `StandardGratingGeometry`, with strict geometry/interaction pairing and compatibility reads for legacy native `grooveFrequency/order` snapshots.
- Recorded the upstream 0.5.8 defect where planar dictionaries omit grating parameters and both grating `from_dict()` implementations fail to reconstruct emitted dictionaries; Workbench writes all required fields and does not claim external Python reload for this subset.
- Added a pinned Optiland 0.5.8 fixture comparing six plane/curved transmissive/reflective/default/evanescent cases ray by ray, including grating vectors, real direction cosines, and paraxial slopes.
