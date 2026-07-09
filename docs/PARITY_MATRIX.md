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
| Geometry | `Geometries` | Plane, standard, even/odd asphere, biconic, toroidal, polynomial, placeholders for named freeforms |
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
| GUI | `OptilandWorkbench.App` | Avalonia shell, connector, editor/viewer/analysis/optimization/system panels; startup fix retained |

## Milestone Notes

This milestone establishes module boundaries and representative implementations. Advanced numerical fidelity still needs targeted follow-up work for high-order freeforms, diffraction efficiency, full thin-film TMM, PSF/MTF/wavefront math, commercial format fidelity, and optional GPU/autograd backend.
