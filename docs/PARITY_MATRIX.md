# Optiland Parity Matrix

This matrix tracks the .NET implementation against the Optiland documentation.

| Optiland area | .NET module | Current status |
| --- | --- | --- |
| Configurable backend | `Backend` | `INumericBackend`, managed CPU backend, backend registry |
| Optic container | `Optic` | Aperture, fields, wavelengths, surfaces, backend, materials, ray tracers, analysis entry point |
| Surface composition | `Domain`, `Geometries`, `Materials`, `Coatings`, `Interactions`, `Apertures`, `Scattering` | Composition model with source-validated phase and diffractive interactions in surface-local coordinates while retaining native snapshot compatibility |
| Real/paraxial/polarized rays | `Rays` | Ray records and bundle model |
| Ray generation | `Raytrace` | Source-validated angle, object-height, and paraxial-image-height fields; radial normalization; nearest-field vignetting; object-space telecentric launch; uniform, hexapolar, random, line, ring, and Sobol-like pupil sampling; seven apodization profiles |
| Ray aiming | `Raytrace` | Paraxial, iterative, robust, cached strategy interfaces |
| Sequential tracing | `Raytrace.SequentialRayTracer` | Surface-local intersection, aperture clipping, refraction/reflection, coating/scattering hooks |
| Geometry | `Geometries` | Plane, standard, planar/standard grating, even/odd asphere, biconic, toroidal, polynomial, Chebyshev, Zernike, Forbes Q, placeholders for remaining named freeforms |
| Propagation | `Propagation` | Homogeneous and GRIN models |
| Materials | `Materials` | Air, vacuum, constant, Cauchy, Sellmeier, Abbe, plus a 1,740-entry manufacturer-aware Optiland/refractiveindex.info glass catalog with formula/tabulated n/k evaluation |
| Thin films | `Coatings` | Stack model and quarter-wave synthesis scaffold |
| Sources | `Sources` | Point and single-mode fiber sources |
| Analysis | `Analysis` | 67 catalog entries; 30 source-derived numerical/display contracts validated on Python 0.5.8 Cooke and Tessar fixtures |
| Optimization | `Optimization` | Problem, operands, variables, scaling, optimizer catalog, local/global numerical optimizer implementations, Glass Expert scaffold |
| Tolerancing | `Tolerancing` | Perturbations, samplers, compensators, sensitivity, seeded Monte Carlo |
| Multi-configuration | `Multiconfig` | Config cloning, default base linking, property unlinking |
| File format | `Serialization`, `FileIO` | Versioned `.staropt` project container with integrity checks and multi-configuration persistence; legacy native JSON import, validated Python Optiland 0.5.8 JSON adapter subset, Optiland-compatible Zemax sequential import/export, plus SEQ/LEN common subset adapters |
| Plugins | `Plugins` | `IOptilandPlugin` assembly/directory discovery with geometry, material, analysis registration and warning isolation |
| Visualization | `Visualization` | Theme primitives plus Optiland-style 2D/3D layout scenes: sag-sampled surfaces, max-extent lens body closure, 3D rims/meridians, sequential ray histories, vignetting truncation, and per-view surface/field/wavelength/pupil controls |
| GUI | `OptilandWorkbench.App` | Chinese Avalonia shell, connector, editor/system-viewer/analysis/optimization/tolerancing/multi-configuration/system panels, equal-scale 2D and solid/wireframe 3D viewer tabs, command palette, consistent light theme, split-pane layout persistence; startup fix retained |

## Milestone Notes

The current milestone has moved beyond module scaffolding into a usable workbench shell:

- STAROPT round-trip preserves rich surface components and all optical configurations; legacy JSON remains readable.
- ZMX import covers the Python Optiland 0.5.8 aperture/field/wavelength and supported-surface boundary; SEQ/LEN remain common sequential subsets.
- The GUI can edit surface components, system aperture, backend selection, fields, wavelengths, analysis selection, and optimizer selection.
- The GUI presents Chinese labels/status text while keeping internal English keys for JSON, plugins, and algorithms.
- Menu, toolbar, and command palette actions are registered through a shared action manager.
- Analysis output includes metric, graphical, and exportable report views, Python-style multi-pane layouts, legends, fixed color scales, and viridis/inferno/jet heatmaps.
- Python golden fixtures validate 30 analysis views point-for-point or pixel-for-pixel, including chief-ray and centroid/best-fit reference-sphere wavefronts, FFT/Huygens/geometric/sampled MTF, FFT/MMDFT/Huygens PSF, radiometry, Jones pupil, and image simulation.
- Python golden fixtures validate all three 0.5.8 field definitions, finite/infinite conjugates, vignetting, telecentric launch, and paraxial image-height unit chief rays.
- Tolerancing GUI provides a TDE-style operand editor and wizard for radius, thickness, conic, decenter, tilt, refractive-index, Abbe-number, and compensator rows; it runs two-sided sensitivity and seeded Monte Carlo analysis with optional refocus compensation, percentile/yield statistics, native tolerance-file save/load, and text-report export.
- Multi-configuration GUI can add configurations, activate a configuration, and edit per-configuration surface thickness.
- Chebyshev, Zernike, and Forbes Q freeform geometries now support sag, finite-difference normals, Newton intersection, GUI selection, and JSON round-trip.
- The system viewer now follows the Optiland visualization model more closely: 2D surfaces are sampled from geometry sag in an equal-scale YZ projection, glass spans are grouped as lens bodies, smaller half-diameter faces are extended to the lens group's maximum extent before closure, rays use viewer-specific stop-aimed sequential traces, and the GUI includes a 3D projection tab with selectable translucent solids or rims, meridians, lens connectors, and 3D ray paths.
- Both layout documents include a collapsed settings panel for start/end surfaces, field and wavelength selection, pupil sampling, ray count, color grouping, Y stretch, scale bar, line width, frame suppression, ray arrows, vignetted-ray removal, and marginal/chief-ray-only display. The synchronization icon rebuilds the scene immediately, while automatic apply uses the standard debounced refresh path.
- Optimization and tolerancing have concrete CPU algorithms and deterministic tests.
- Plugin discovery supports geometry, material, and analysis registration with warning isolation.

The matrix does not imply full Optiland equivalence. Remaining high-value gaps are Forbes/NURBS/grid-sag freeform JSON breadth, diffraction efficiency, full thin-film TMM, vectorial diffraction methods, non-sequential tracing, commercial-format breadth, GUI automation breadth, and optional GPU/autograd backends.
