# Python Analysis and Plot Parity

## Reference

The validated analysis reference is Python `optiland==0.5.8`. Golden data is generated directly from the official `CookeTriplet` and `TessarLens` samples and committed as `optiland-0.5.8-analysis-reference.json`.

The current validated set is:

- `SpotDiagram`, with all configured fields and wavelengths, 6-ring/127-ray hexapolar sampling, primary-wavelength centroid reference, and centered image-plane points.
- `EncircledEnergy`, with field-separated energy accumulation. Golden tests use a deterministic 3-ring hexapolar distribution because Python's default random distribution intentionally has no fixed seed.
- `RmsSpotSizeVsField`, with a normalized Y-field sweep and one RMS curve per wavelength.
- `RmsWavefrontErrorVsField`, with a normalized Y-field sweep and one wavefront RMS curve per wavelength.
- `RayFan`, with odd line-pupil sampling, primary-chief-ray recentering, and paired X/Y fans for every field.
- `BestFitRayFan`, with a three-dimensional least-squares reference sphere fitted from tilt-corrected wavefront points.
- `PupilAberration`, with real-versus-paraxial stop intersections normalized by the on-axis paraxial stop radius.
- `ThroughFocusSpotDiagram`, with image-plane shifts, field-by-focus panes, and shared centered spot limits.
- `ThroughFocusMTF`, with physical image-surface shifts, sampled tangential/sagittal MTF, and Python-compatible cubic display interpolation.
- `PupilIncidentAngleVsHeight` and `FieldIncidentAngleVsHeight`, with pupil- or field-colored incident-angle curves at a selected surface.
- `IncoherentIrradiance`, with detector-aperture extents, intensity-weighted two-dimensional binning, per-pane normalization, and field-by-wavelength heatmaps.
- `RadiantIntensity`, with surface direction-cosine angle projection, solid-angle normalization, shared absolute color limits, and central angular cross-sections.
- `YYbar`, with per-surface paraxial marginal and chief ray heights.
- `OPD/Wavefront`, with chief-ray exit-pupil reference sphere data in waves.
- `CentroidSphereWavefront`, with Optiland's centroid-sphere OPD strategy, 5-ring/91-ray hexapolar sampling, tilt-corrected sphere center/radius, pupil intersections, intensity, OPD, and RMS.
- `BestFitSphereWavefront`, with Optiland's best-fit-sphere OPD strategy, 5-ring/91-ray hexapolar sampling, tilt-corrected least-squares sphere center/radius, pupil intersections, intensity, OPD, and RMS.
- `ZernikeOPD`, with Fringe indexing and least-squares coefficients.
- `FFTPSF`, with complex pupil phase, zero padding, two-dimensional FFT, and diffraction-limited normalization.
- `MMDFTPSF`, with source-matched matrix-multiply DFT kernels, image-plane pixel pitch, peak Strehl, and bounded 16 by 16 reference grids.
- `HuygensPSF`, with Huygens-Fresnel direct summation, image-surface coordinates, ideal on-axis normalization, center Strehl, and bounded 9 by 9 reference grids.
- `FFTMTF`, with field-paired tangential/sagittal curves and on-axis working-F-number frequency scaling.
- `HuygensMTF`, with two-dimensional FFT of the Huygens PSF and DC-normalized tangential/sagittal slices.
- `GeometricMTF`, with spot-histogram Fourier integration and the diffraction-limited modulation envelope.
- `SampledMTF`, with a 37-term Fringe wavefront fit and shifted-pupil overlap evaluation over tangential/sagittal frequency scans.
- `Distortion`, with 17-point `f-tan` and `f-theta` sweeps at every configured wavelength.
- `GridDistortion`, with 10 by 10 `f-tan` and `f-theta` chief-ray grids at the primary wavelength.
- `FieldCurvature`, with 17-point tangential and sagittal parabasal intersections at every configured wavelength.
- `JonesPupil`, with Fresnel polarization propagation and the real/imaginary parts of all four Jones elements over the pupil.
- `ImageSimulationEngine`, with field-dependent FFT PSFs, EigenPSF decomposition, spatially variable convolution, fifth-order geometric distortion, and wavelength-dependent lateral color.

The production defaults remain Python's defaults for the existing deterministic analyses. Jones pupil uses a 65-square pupil grid. The reusable image engine defaults to a 5 by 5 PSF field grid, 128-square PSFs, 64 pupil samples, three EigenPSFs, 64-pixel reflect padding, a 25-square distortion grid, and a fifth-order fit. The interactive analysis preview uses a smaller source-derived configuration to keep the GUI responsive.

Regenerate the fixture and Python reference plots with:

```bash
python tools/python-reference/generate_analysis_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-analysis-reference.json \
  --plot-dir work/python-analysis-plots
```

## Numerical Contract

The C# implementations use Python's normalized field and pupil coordinates and the final traced image-plane sample. Angle fields are normalized by the maximum radial field `sqrt(x²+y²)`, matching `FieldGroup.max_field`, rather than scaling X and Y independently. The analyses no longer use EFL, Petzval, or three-ray summary proxies.

| Analysis | Python-compatible calculation |
| --- | --- |
| Spot diagram | Nested field/wavelength traces, intensity filtering, and centering all wavelengths on the primary-wavelength geometric centroid |
| Encircled energy | Radius sweep to 1.2 times the global geometric spot radius and direct sum of ray intensity inside each radius |
| RMS versus field | 64-point normalized Y-field sweep and unweighted geometric RMS radius for every wavelength |
| RMS wavefront versus field | Normalized Y-field sweep and chief-ray-reference RMS OPD in waves for every wavelength |
| Ray fan | Orthogonal line-pupil traces with invalid-ray gaps and primary-wavelength center-ray distortion removal |
| Best-fit ray fan | Tilt-corrected wavefront points, four-parameter least-squares sphere fit, and line-pupil image intersections referenced to the fitted center |
| Pupil aberration | Real stop-surface intersections minus the normalized paraxial stop trace, reported as stop-radius percent |
| Through-focus spot | Final rays projected onto each shifted image plane, then centered on the primary-wavelength centroid |
| Through-focus MTF | Image geometry moved and restored at each focus position; sampled MTF evaluated at one spatial frequency for tangential and sagittal axes |
| Angle versus image height | Generic-ray pupil or field scan with local surface height and incident direction angle, colored by normalized scan coordinate |
| Incoherent irradiance | Final local detector coordinates binned with `histogram2d` boundary semantics and weighted by ray intensity per physical pixel area |
| Radiant intensity | Selected-surface direction cosines projected with `atan2(L,N)` / `atan2(M,N)`, intensity-weighted angle bins, and W/sr solid-angle scaling |
| Y-Ybar | Python-compatible per-surface paraxial refraction of the marginal and maximum-field chief rays |
| Wavefront | Image rays traced backward to the chief-ray exit-pupil sphere, object-plane angular tilt correction, and OPD conversion to waves |
| Centroid sphere wavefront | Python `centroid_sphere` OPD strategy with tilt-corrected reference-sphere center/radius, pupil intersections, intensity, OPD, and RMS |
| Best-fit sphere wavefront | Python `best_fit_sphere` OPD strategy with four-parameter sphere fit, pupil intersections, intensity, OPD, and RMS |
| Zernike | Unnormalized Fringe basis ordering with QR least-squares fitting to valid wavefront samples |
| FFT PSF | Complex pupil amplitude and phase, centered zero padding, two-dimensional FFT, and ideal-pupil peak normalization |
| MMDFT PSF | Uniform pupil complex amplitude/phase propagated with Python's non-unitary matrix-multiply DFT kernels and ideal-pupil peak normalization |
| Huygens PSF | Huygens-Fresnel direct summation over chief-ray exit-pupil points, image-surface sag coordinates, obliquity factor, and ideal on-axis normalization |
| FFT MTF | Two-dimensional FFT of PSF intensity with normalized center-axis tangential and sagittal slices |
| Huygens MTF | Two-dimensional FFT of the Huygens PSF, NumPy-compatible odd-size `fftshift`, DC normalization, and pixel-pitch frequency step |
| Geometric MTF | One-dimensional spot histograms transformed with cosine/sine integrals and optionally multiplied by the circular-pupil diffraction limit |
| Sampled MTF | Complex pupil overlap against a frequency-shifted 37-term Fringe Zernike fit, normalized by zero-frequency intensity |
| Distortion | Chief ray at each normalized Y field; ideal height from the `f-tan` or `f-theta` model; percent difference at every wavelength |
| Grid distortion | Chief-point-centered real grid against the ideal grid over `[-sqrt(2)/2, +sqrt(2)/2]` in X and Y |
| Field curvature | Paired `+/-1e-5` normalized pupil rays; direct tangential and sagittal line intersections from final position and direction cosines |
| Jones pupil | Per-surface `s/p/k` basis transport, optional Fresnel Jones coefficients, final `u/v` projection, and complex `Jxx/Jxy/Jyx/Jyy` pupil samples |
| Image simulation | Normalized field PSF stack, Gram/SVD-equivalent EigenPSFs, spatial coefficient interpolation, spatial-axis FFT-convolution contract, polynomial inverse distortion map, bilinear warp, and RGB channel stacking |

The tests compare every generated point for both official lenses. The normal tolerance is `2e-8 * max(1, abs(expected))`. Image-simulation pixels use an absolute `5e-5` tolerance because the C# symmetric eigensolver and NumPy LAPACK accumulate slightly different rounding through PSF convolution. Every intermediate blur pixel, distortion-grid coordinate, and final RGB pixel is checked.

Repository validation as of 2026-07-13 is a zero-warning solution build and `163/163` passing tests.

## Plot Contract

The Avalonia plot model now supports multiple ordered series, named legends, Matplotlib C0-C9 colors, solid/dashed/dotted styles, value-colored lines, viridis/inferno/jet heatmaps, fixed or automatic color limits, per-series line widths, symmetric X limits, fixed or automatic axis limits, equal aspect, title text, zero reference lines, and hidden top/right axes.

The thirty views mirror Python's presentation:

- Spot diagram: up to three square field subplots per row, shared limits, field-coordinate titles, wavelength colors and circle/square/triangle markers, low-opacity grids, and a shared legend below the panes.
- Encircled energy: one field curve per normalized field coordinate, radius and dimensionless-energy axes, primary wavelength title, nonnegative axes, and external legend.
- RMS versus field: one line per wavelength, normalized Y field from 0 to 1, nonnegative RMS axis, and external legend.
- RMS wavefront versus field: one line per wavelength, normalized Y field from 0 to 1, RMS wavefront error in waves, and external legend.
- Ray fan: two panes per field for Y and X pupil fans, shared limits, horizontal and vertical zero references, and a shared wavelength legend.
- Best-fit ray fan: the same paired fan layout, referenced to the primary-wave best-fit sphere center without chief-ray distortion removal.
- Pupil aberration: the same two-pane field layout with percent axes and stop-normalized errors.
- Through-focus spot: one row per field and one column per focus plane, shared square limits, defocus titles on the first row, and a shared wavelength legend.
- Through-focus MTF: field-colored tangential solid and sagittal dashed pairs, defocus in millimeters, dotted grid, and external legend.
- Angle versus image height: viridis-colored incident-angle curves with a scan-coordinate colorbar for pupil and field scan modes.
- Incoherent irradiance: one row per field and one column per wavelength, equal detector axes, per-pane peak normalization, inferno heatmaps, and normalized-irradiance colorbars.
- Radiant intensity: one angle-space jet heatmap and one central X-angle cross-section per field/wavelength pair, shared absolute W/sr color limits, dotted grids, and fixed cross-section limits.
- Y-Ybar: one marked segment per adjacent surface pair, named first/stop/image segments, marginal-versus-chief axes, title wavelength, and thin zero references.
- Wavefront: square pupil heatmap, RMS title, pupil axes, viridis scale, and OPD colorbar in waves.
- Centroid sphere wavefront: square pupil heatmap, RMS title, pupil axes, viridis scale, centroid reference-sphere metrics, and OPD colorbar in waves.
- Best-fit sphere wavefront: square pupil heatmap, RMS title, pupil axes, viridis scale, best-fit reference-sphere metrics, and OPD colorbar in waves.
- Zernike: unit-circle Fringe-fit heatmap with pupil axes and OPD colorbar.
- FFT PSF: threshold-centered image crop, physical micrometer axes, relative-intensity heatmap, title, and colorbar.
- MMDFT PSF: full source-bounded image heatmap, physical micrometer axes from the MMDFT pixel pitch, relative-intensity scale, title, and colorbar-ready value label.
- Huygens PSF: full source-bounded image heatmap, physical micrometer axes from the Huygens pixel pitch, relative-intensity scale, title, and colorbar-ready value label.
- FFT MTF: field-colored tangential solid and sagittal dashed pairs, cycles/mm axis, nonnegative modulation range, cutoff limit, and external legend.
- Huygens MTF: field-colored tangential solid and sagittal dashed pairs generated from Huygens PSF data, cycles/mm axis, nonnegative modulation range, and external legend.
- Geometric MTF: the same field-paired solid/dashed MTF presentation with geometric spot-histogram data and a paraxial diffraction cutoff.
- Sampled MTF: field-colored tangential solid and sagittal dashed frequency curves using the sampled-pupil numerical method.
- Distortion: distortion percent on X, field on Y, one line per wavelength, a dashed vertical zero line, symmetric X range, Y starting at zero, and an external right-side legend.
- Grid distortion: orange solid ideal grid, blue dashed distorted grid, equal image-plane scale, dotted grid, hidden top/right axes, maximum distortion in the title, and the repeated per-line legend entries produced by Python's two-dimensional `Axes.plot` call.
- Field curvature: image-plane delta on X, field on Y, same-color tangential solid and sagittal dashed pairs, a thin solid vertical zero line, symmetric X range, title, and external legend.
- Jones pupil: two rows by four columns for real/imaginary `Jxx`, `Jxy`, `Jyx`, and `Jyy`, equal pupil axes, viridis maps, and per-pane colorbars.
- Image simulation: original and simulated RGB images side by side, with image axes hidden and Python-compatible titles.

Line point order is preserved. This is required for two-dimensional grid rows and columns and fixes the previous renderer behavior that sorted every line by X before drawing it.

## Scope

This parity statement applies to the thirty analyses above on the validated sequential refractive path and chief-ray, centroid-sphere, and best-fit-sphere wavefront strategies. Sampled MTF is validated both as a frequency sweep and through focus; geometric MTF has its own spot-based contract. MMDFT and Huygens-Fresnel diffraction are validated on bounded Cooke/Tessar fixtures; vectorial PSF/MTF remains outside this contract.

The checked wavefront samples and FFT arrays are point-for-point equivalent. The native Avalonia OPD heatmaps currently use local inverse-distance interpolation rather than SciPy's `griddata(method="cubic")`; axes, limits, values, color scale, title, and colorbar follow Python, but interpolated pixels between traced samples are not yet claimed identical.
