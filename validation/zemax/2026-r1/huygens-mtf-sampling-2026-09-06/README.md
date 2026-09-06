# Huygens MTF sampling evidence — 2026-09-06

Frozen, read-only Zemax OpticStudio 2026 R1 captures (26.1 SP0, API 260127).
These files test PSF-to-MTF postprocessing independently of the Workbench PSF
engine. Passing these tests does **not** establish agreement of ray tracing,
PSF synthesis, through-focus propagation or all field positions.

The two source lens files are included and covered by `manifest.json`:

- `ms-l7.ZMX`: user-selected [MS-L7](10X大NA大视场), SHA-256
  `8bcc937c2c2e02ba175f38875fd0def40db547f7eedab509cbfd1fed4353e0e8`.
- `primary.ZMX`: independent authority `123456.ZMX`, SHA-256
  `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`.

All captures use configuration 1, field 1 (on axis), selected wavelength 1,
32 × 32 pupil sampling, 0.25 µm image pitch, unpolarized light, no centroid
shift, and native linear PSF normalization. MS-L7 wavelength 1 is 0.4861327 µm;
the primary lens uses 0.42 µm. Lens dimensions and defocus are mm, frequencies
are cycles/mm, and modulation is dimensionless. Ray aiming follows each source
lens (MS-L7 enabled, primary disabled). These are explicit capture settings,
**not universal Zemax defaults**. Per-capture `captured-settings.json`, CFG,
`environment.json`, `model.json`, text and unmodified numeric arrays are retained.

| Directories | Image grid | Contract |
|---|---:|---|
| ms-psf / ms-mtf | 32 × 32 | Native PSF to all 300 MTF points, 0–50 cycles/mm |
| ms-image64-psf / ms-image64-mtf | 64 × 64 | Independent image-sampling holdout, same frequency range |
| primary-psf / primary-mtf | 32 × 32 | Independent lens holdout, same frequency range |
| ms-focus-50 / focus-125 / focus-250 / focus-500 | 32 × 32 | Zero-defocus samples at four frequencies; five focus planes over ±0.01 mm, 101 display points |
| primary-focus | 32 × 32 | Independent lens, zero defocus, 50 cycles/mm, same focus range |
| ms-field | 32 × 32 effective image grid | On-axis point at 10, 20, 30, 40, 50, 60 cycles/mm; field density 10, +Y, remove vignetting factors |

The four `ms-*` captures with a 32 × 32 image grid were copied unchanged from
the preceding complete run. The other eight captures were independently generated
to test the inferred postprocessing rule; no lens, native array or tolerance was fitted.
For the field scan, ZOS-API exposes pupil sampling and image pitch, but no separate
image-grid setting; its on-axis values independently reproduce the 32 × 32 PSF contract.

Observed and independently tested rule: ordinary frequency/field MTF uses a
zero-padded 2N × 2N transform; through-focus MTF uses N × N. Both interpolate
with a natural cubic spline over the transform endpoint span `(K-1) * pixelPitch`.
The physical DFT period remains `K * pixelPitch`; only the compatibility sampling
coordinates change. Independent reconstruction agrees within 1e-10 absolute
modulation (observed ordinary-MTF maximum below 7e-16). Full-engine comparison
retains the original tolerances in `comparison-settings.json`.

Reproduce using the independent C# comparison tool with these source files and
the included configuration. Select `Huygens PSF`, `Huygens MTF`,
`Huygens Through Focus MTF`, or `Huygens MTF vs Field` with `--analysis`.
For the holdouts, copy the configuration and set `imageSampling` to 64 for both
PSF/MTF, or `maximumFrequency` to 125/250/500 for through focus. The actual native
properties in each capture take precedence over the human-readable request copy.
The standard host also accepts the `request` object from `captured-settings.json`
as its request JSON, with `input` and `zosApiPath` changed to local paths, followed
by a fresh output-directory argument. Never overwrite these frozen files.

`HuygensMtfSamplingTests` verifies the native reconstruction, physical DFT
spacing, unchanged general sampling, resource bounds, and every manifest hash.
Production `src` has no dependency on these assets or on the capture tool.
