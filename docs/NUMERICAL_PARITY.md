# Optiland 0.5.8 Numerical Parity

## Reference

The fixed reference is `optiland==0.5.8` and its official `CookeTriplet` and `TessarLens` sample factories. The GUI workflow is documented in the [Optiland GUI Quickstart](https://optiland.readthedocs.io/en/latest/gui_quickstart.html).

The reference can be regenerated with:

```bash
python -m venv .venv-optiland-reference
.venv-optiland-reference/bin/pip install optiland==0.5.8
.venv-optiland-reference/bin/python tools/python-reference/generate_cooke_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-cooke.json
.venv-optiland-reference/bin/python tools/python-reference/generate_tessar_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-tessar.json
.venv-optiland-reference/bin/python tools/python-reference/generate_field_definition_reference.py \
  tests/OptilandWorkbench.Tests/Fixtures/optiland-0.5.8-field-definition-reference.json
```

The generated JSON files record each prescription, normalized generic rays at three wavelengths, every traced surface, and three 9-ray `line_y` bundles.

## Prescription

| Surface | Radius (mm) | Thickness (mm) | Material after | Stop |
| ---: | ---: | ---: | --- | --- |
| 0 | Plane | 0 | Air | No |
| 1 | 22.01359 | 3.25896 | SK16 | No |
| 2 | -435.76044 | 6.00755 | Air | No |
| 3 | -22.21328 | 0.99997 | F2 | No |
| 4 | 20.29192 | 4.75041 | Air | Yes |
| 5 | 79.68360 | 2.95208 | SK16 | No |
| 6 | -18.39533 | 42.20778 | Air | No |
| 7 | Plane | 0 | Air | No |

The entrance pupil diameter is 10 mm. Fields are 0, 14, and 20 degrees. Wavelengths are 0.48, 0.55 (primary), and 0.65 micrometers.

## Refactor

The comparison exposed four systemic differences:

| Area | Previous Workbench behavior | Refactored behavior |
| --- | --- | --- |
| Material index | Approximate Abbe F2; unknown SK16 fell back to n=1.5 | Exact Optiland F2 Sellmeier and SK16 polynomial dispersion |
| Absorption | Extinction coefficients were ignored during propagation | Tabulated extinction and Beer-Lambert attenuation are applied per segment |
| First order | EFL summed isolated surface powers and ignored spacing/index transitions | Sequential ABCD refraction/translation matrices calculate EFL and entrance pupil location |
| Ray launch | Rays started on the first surface and used the stop semi-diameter | Infinite-conjugate rays start one EPD before the system and aim through the calculated entrance pupil |
| Standard surface | Newton intersection and finite-difference normals | Analytic conic intersection and normal |

## Results

The committed golden tests use strict absolute tolerances of `1e-11` for first-order scalars and `1e-10` for real-ray values.

Measured comparison before applying test tolerances:

| Quantity | Maximum absolute difference |
| --- | ---: |
| Effective focal length | 2.132e-14 mm |
| F-number | 1.776e-15 |
| Entrance pupil location | 1.776e-15 mm |
| Per-surface position | 5.329e-14 mm |
| Per-surface direction cosine | 1.110e-15 |
| Cumulative optical path | 9.059e-14 mm |
| Per-surface intensity | 0 |
| Line-bundle centroid | 7.105e-15 mm |
| Line-bundle RMS spot radius | 2.580e-15 mm |

The comparison intentionally skips Python surface 0 because Optiland represents the infinite object sample at the generated ray start (`z=-10 mm`), while the Workbench keeps an explicit zero-thickness object plane at the first optical surface. Physical surfaces 1 through 7 are indexed identically and are compared directly.

### Field Definitions

`optiland-0.5.8-field-definition-reference.json` validates angle, object-height, and paraxial-image-height fields for finite and infinite conjugates. It compares initial distribution and generic rays, final generic rays, normalized paraxial traces, vignetting factors, object-space telecentric launch, object-NA entrance-pupil conversion, and the forward/reverse unit chief rays used to resolve paraxial image height. Finite-object Python coordinates are translated to the Workbench object-plane origin before comparison; Python JSON import/export preserves that conjugate distance.

Python Optiland 0.5.8 does not expose Zemax's real-image-height field type. The Workbench implements it as a documented Zemax extension: a damped two-dimensional solve adjusts the infinite-conjugate object angle or finite-conjugate object coordinate until the primary-wavelength chief ray reaches the requested local image-surface X/Y coordinate. Distortion temporarily converts real image height to field angle or object height, while image simulation temporarily uses paraxial image height. Field curvature and distortion then scan a NumPoints half-fan along the selected `+x/-x/+y/-y` direction, switch tangential/sagittal planes for X scans, and either ignore or apply vignetting factors without mutating the source optic. All field types use the same Python-compatible maximum-radial-field normalization.

### Tessar F/4.5

The second fixture is the official four-element, three-group `TessarLens` sample using N-SK15, F2, and K10 glass. It has EFL `3.9977777470211935 mm`, image-space F-number `4.5`, and entrance pupil diameter `0.8883950548935986 mm`.

| Quantity | Maximum absolute difference |
| --- | ---: |
| Effective focal length | 8.882e-16 mm |
| Entrance pupil location | 4.441e-16 mm |
| Per-surface position | 2.220e-15 mm |
| Per-surface direction cosine | 1.554e-15 |
| Cumulative optical path | 6.217e-15 mm |
| Per-surface intensity | 0 |
| Line-bundle centroid | 1.110e-15 mm |
| Line-bundle RMS spot radius | 4.232e-16 mm |

This fixture also verifies image-space F-number conversion to entrance pupil diameter and a cemented K10-to-N-SK15 material transition.

### Native Python JSON

Official `Optic.to_dict()` JSON files for both samples are committed as independent fixtures. They are opened through the same `OpticJsonStore` path used by the GUI and must satisfy the same first-order and per-surface golden data.

C# exports were additionally loaded by real Optiland 0.5.8 using `json.load` and `Optic.from_dict`. For Cooke and Tessar, first-order values matched exactly and a representative ray produced zero difference in `x`, `y`, `L`, `M`, OPD, and intensity.

That external Python round-trip is validated on the uncoated official samples. Workbench can read and write the raw Python `SimpleCoating.to_dict()` dictionary shape, but Python Optiland 0.5.8 may relink arbitrary surface coatings to Fresnel coatings during `Optic.from_dict()`, so coating preservation is not included in the external Python round-trip claim yet.

### Analysis Results

The separate `optiland-0.5.8-analysis-reference.json` fixture validates 30 numerical/graphical views for both Cooke and Tessar. Coverage includes spot and standard/best-fit ray fans, encircled energy, RMS spot/wavefront field sweeps, pupil aberration, through-focus spot and sampled MTF, Y-Ybar, both incident-angle scans, `f-tan`/`f-theta` distortion and grid distortion, field curvature, chief-ray and centroid/best-fit reference-sphere wavefronts, Fringe Zernike, FFT/MMDFT/Huygens PSF, FFT/Huygens/geometric/sampled MTF, incoherent irradiance, radiant intensity, Jones pupil, and spatially variable image simulation.

Tests compare deterministic output at its native level: traced samples and fitted sphere parameters, curve points, wavefront/Zernike values, heatmap and PSF pixels, MTF values, and distortion coordinates. Huygens regressions additionally require exact adjacent-point Image Delta and the local image-surface normal for tilted and curved detectors. RMS scan regressions require the Show Diffraction Limit switch to add a dashed horizontal series for Field/Focus and a wavelength-dependent spot reference curve for Wavelength, while preserving Zemax's approximate `1.22 × F/# × wavelength`, `0.072 waves`, and `0.8 Strehl` thresholds. Image-simulation regressions validate the Zemax workflow invariants—selectable None/Geometric/Diffraction modes, black guard bands, Field Height mapping, relative illumination, distortion/lateral color, and severe-aberration fallback—rather than preserving obsolete fixed-FFT RGB pixels. Presentation tests verify pane order, labels, line pairing, marker/style choices, zero lines, aspect, legends, limits, and colorbars. The normal analysis tolerance is `2e-8 * max(1, abs(expected))`.

Relative Illumination is intentionally outside the Optiland 0.5.8 parity fixture because that release does not expose an equivalent analysis. Its contract follows the [Zemax Relative Illumination definition](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v252/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Relative_Illumination.html): trace a rectangular pupil grid, integrate transmitted effective pupil area in image-space direction cosines, and normalize to the maximum field illumination. Regression tests cover Cooke and Tessar curves, signed field scans, effective F-number output, vignetting-factor removal, and the desktop connector contract.

See [Python analysis and plot parity](PYTHON_ANALYSIS_PARITY.md) for the numerical and presentation contract.

## Regression Contract

`CookeTripletGoldenTests` verifies both official samples:

- EFL, F-number, entrance pupil diameter, and entrance pupil location.
- Position, direction, cumulative optical path, and intensity at every physical surface for eight rays.
- Weighted image centroid and RMS spot radius for 9-ray line bundles at normalized fields 0, 0.7, and 1.0.

The prescription/ray fixtures validate the centered standard sequential refractive path. The analysis fixture adds the specifically listed PSF, MTF, wavefront, Jones, and radiometric contracts on those two lenses. Neither fixture claims general parity for freeforms, vectorial diffraction, arbitrary polarization/coating stacks, GRIN curved-ray intersection, non-sequential tracing, or systems outside the documented method and sample boundaries.

The validated repository baseline on 2026-07-30 is a zero-warning solution build with `577/577` passing tests. It includes the Huygens image-surface normal and Image Delta corrections, selectable Zemax-style image simulation, distortion and RMS reference-series regressions, compact spot-diagram layout, optional square multi-pane layout, current glass-catalog priority persistence, and merit-operand row-color coverage.
