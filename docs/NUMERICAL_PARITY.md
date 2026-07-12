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

### Analysis Results

Python-generated golden arrays also validate spot diagram, encircled energy, RMS spot size versus field, ray fan, pupil aberration, through-focus spot diagram, Y-Ybar, distortion, grid distortion, field curvature, chief-ray wavefront, Fringe Zernike, FFT PSF, and FFT MTF for Cooke and Tessar. Both `f-tan` and `f-theta` distortion models are checked. The tests compare every deterministic analysis point, wavefront sample, coefficient, PSF pixel, and MTF value and verify Python's field/focus panes, marker/series labels, line pairing, zero-line behavior, equal-aspect grids, legend use, and axis rules.

See [Python analysis and plot parity](PYTHON_ANALYSIS_PARITY.md) for the numerical and presentation contract.

## Regression Contract

`CookeTripletGoldenTests` verifies both official samples:

- EFL, F-number, entrance pupil diameter, and entrance pupil location.
- Position, direction, cumulative optical path, and intensity at every physical surface for eight rays.
- Weighted image centroid and RMS spot radius for 9-ray line bundles at normalized fields 0, 0.7, and 1.0.

This fixture validates the standard sequential refractive path. It does not claim numerical parity for freeforms, diffraction, polarization, coatings, GRIN curved-ray intersection, PSF/MTF, or non-sequential tracing.
