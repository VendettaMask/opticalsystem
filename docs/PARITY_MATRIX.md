# Optiland Parity Matrix

This matrix tracks the .NET implementation against the Optiland documentation.

| Optiland area | .NET module | Current status |
| --- | --- | --- |
| Configurable backend | `Backend` | `INumericBackend`, managed CPU backend, backend registry |
| Optic container | `Optic` | Aperture, fields, wavelengths, surfaces, backend, materials, ray tracers, analysis entry point |
| Surface composition | `Domain`, `Geometries`, `Materials`, `Coatings`, `Interactions`, `Apertures`, `Scattering` | Composition model added while retaining GUI-compatible legacy fields |
| Real/paraxial/polarized rays | `Rays` | Ray records and bundle model |
| Ray generation | `Raytrace` | Grid, hexapolar, random, Sobol-like pupil sampling with apodization/telecentric options |
| Ray aiming | `Raytrace` | Paraxial, iterative, robust, cached strategy interfaces |
| Sequential tracing | `Raytrace.SequentialRayTracer` | Surface-local intersection, aperture clipping, refraction/reflection, coating/scattering hooks |
| Geometry | `Geometries` | Plane, standard, even/odd asphere, biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q, placeholders for remaining named freeforms |
| Propagation | `Propagation` | Homogeneous and GRIN models |
| Materials | `Materials` | Air, vacuum, constant, Cauchy, Sellmeier, Abbe/catalog registry |
| Thin films | `Coatings` | Stack model and quarter-wave synthesis scaffold |
| Sources | `Sources` | Point and single-mode fiber sources |
| Analysis | `Analysis` | Base analysis plus documented analysis catalog with concrete proxy data outputs |
| Optimization | `Optimization` | Problem, operands, variables, scaling, optimizer catalog, local/global numerical optimizer implementations, Glass Expert scaffold |
| Tolerancing | `Tolerancing` | Perturbations, samplers, compensators, sensitivity, seeded Monte Carlo |
| Multi-configuration | `Multiconfig` | Config cloning, default base linking, property unlinking |
| File format | `Serialization`, `FileIO` | Schema version 2 JSON snapshot plus ZMX/SEQ/LEN common sequential subset import/export |
| Plugins | `Plugins` | `IOptilandPlugin` assembly/directory discovery with geometry, material, analysis registration and warning isolation |
| Visualization | `Visualization` | Theme and primitive scene DTOs for 2D/3D renderers |
| GUI | `OptilandWorkbench.App` | Chinese Avalonia shell, connector, editor/viewer/analysis/optimization/tolerancing/multi-configuration/system panels, command palette, light/dark theme persistence, split-pane layout persistence; startup fix retained |

## Milestone Notes

The current milestone has moved beyond module scaffolding into a usable workbench shell:

- Native JSON round-trip preserves rich surface components.
- ZMX/SEQ/LEN import/export covers a common sequential lens subset.
- The GUI can edit surface components, system aperture, backend selection, fields, wavelengths, analysis selection, and optimizer selection.
- The GUI presents Chinese labels/status text while keeping internal English keys for JSON, plugins, and algorithms.
- Menu, toolbar, and command palette actions are registered through a shared action manager.
- Analysis output is now shown as both a structured metric table and exportable text report.
- Tolerancing GUI runs sensitivity and Monte Carlo tables for selected surface radius/thickness perturbations.
- Multi-configuration GUI can add configurations, activate a configuration, and edit per-configuration surface thickness.
- Chebyshev, Zernike, and Forbes Q freeform geometries now support sag, finite-difference normals, Newton intersection, GUI selection, and JSON round-trip.
- Optimization and tolerancing have concrete CPU algorithms and deterministic tests.
- Plugin discovery supports geometry, material, and analysis registration with warning isolation.

Advanced numerical fidelity still needs targeted follow-up work for NURBS/grating freeforms, diffraction efficiency, full thin-film TMM, rigorous PSF/MTF/wavefront math, commercial format breadth, and optional GPU/autograd backend.
