# Lens-spectrum slanted edge / SFR — 2026-11-02

## Implemented behavior

The existing camera laboratory now offers **斜边 SFR → 当前镜头波长与权重** (initial selection), alongside manual monochrome mode. It freezes the current unsaved sequential optic, its actual active wavelength rows and raw weights. An imported file's unused trailing WAVM slots are not treated as active wavelengths.

Bands share the lens primary-wavelength chief reference, saved image plane and physical image lattice. Each band retains its own traced OPD, transmission/apodization shape and working F numbers. Linear PSF intensities are mixed with normalized lens weights **before** finite-window normalization and full-square pixel integration. The independent reference sums **complex OTFs before magnitude**. Neither per-band PSF recentering nor averaging MTF magnitudes is used. No refocus, source-prescription change, Gaussian replacement, fitted correction or relaxed accuracy gate was introduced.

This relative image uses the configured weights as relative image-energy weights of full-pupil-normalized monochromatic PSFs; there is no additional QE, photon conversion or absolute spectral throughput factor. Zero-weight rows remain in the record but are not traced; all-zero weights explicitly fail. There are 1–16 bounded rows. A shared in-call sampled pupil avoids repeated tracing for image synthesis and OTF correlation, and a two-point auxiliary readout avoids forming an unused complete FFT image. Pupil-cell phase and physical scaling are unchanged. The independent OTF calculation never reads the finite PSF.

## Explicit sampling and preserved failures

The monochrome radius-32 setting cannot simply be reused for every spectrum: the initial radius-64 refinement reaches the sampled pupil's periodic boundary for a short-wavelength band. `numerical/` and `probe.log` retain four completed failing checks (three Double Gauss fields and 123456 Field 1); the rest of that initial diagnostic was stopped and is not a completed matrix.

The new polychrome page uses **pupil 256, subgrid 8, PSF radius 24 pixels**, with independent refinements to **512 / 16 / radius 48**, preserving the original **0.01** numerical difference gate and all phase, intensity-Nyquist, periodic-boundary and operation/memory limits. The single-band API's radius-32 default is unchanged. Switching modes preserves visible sampling inputs; no hidden correction changes user settings. `CameraSfrRequest.FromLensSpectrum()` supplies the explicit spectrum preset. A failed participating band cannot be skipped to obtain a pass.

`radius24/` and `probe-radius24.log` preserve four completed preliminary checks before removing duplicated trace work; that diagnostic was stopped for the implementation update and is not the final matrix. These intermediate receipts are not added to current pass counts. The completed current matrix is solely `final-numerical/` / `probe-final.log`.

## Actual unchanged inputs and final matrix

- Built-in `src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx`, SHA-256 `f994005d633d4af742402469c9e4e2e0c289a57aed3ae876fb3231d1d3f95460`: active wavelengths **486.1 / 587.6 / 656.3 nm**, weights **1 / 1 / 1**, primary **587.6 nm**.
- `artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX`, SHA-256 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`: active wavelengths **420 / 440 / 460 nm**, weights **1 / 1 / 1**, primary **440 nm**.

No duplicate source file is added. Both use 3 µm full square pixels, 96×96 image/ROI, 5° near-vertical edge, 0.2 px offset, 0.2/0.8 levels and ESF half-window 24. The input snapshot is checked unchanged after each run. The earlier 550 nm monochrome results retain their distinct spectral conditions.

| Input / Field | Measured SFR MTF50 (cycles/pixel) | Maximum SFR vs full-pupil × pixel | Maximum window vs full optical MTF | Captured integral | Three refinements |
| --- | ---: | ---: | ---: | ---: | --- |
| Double Gauss / 1 | 0.1064334306 | 0.0079250337 | 0.0060269725 | 0.9942582477 | Pass |
| Double Gauss / 2 | 0.1920705350 | 0.0083228070 | 0.0063284918 | 0.9938527771 | Pass |
| Double Gauss / 3 | 0.1222690961 | 0.0090533874 | 0.0065905355 | 0.9936313872 | Pass |
| 123456 / 1 | 0.4735737608 | 0.0048539656 | 0.0036883095 | 0.9965162083 | Pass |
| 123456 / 2 | 0.4374149898 | 0.0049663544 | 0.0037784498 | 0.9964269416 | Pass |
| 123456 / 3 | 0.4594242060 | 0.0048904856 | 0.0037169532 | 0.9964868349 | Pass |
| 123456 / 4 | 0.4674289563 | 0.0048667176 | 0.0036981510 | 0.9965059136 | Pass |
| 123456 / 5 | 0.4722565546 | 0.0048576118 | 0.0036905741 | 0.9965137983 | Pass |

All eight final reports contain all 129 curve points, physical frequencies, exact raw/normalized wavelength rows, common reference, each refinement's differences and unchanged-input verdict. `numerical-summary.json` is a compact index. These are single-doubling spatial convergence checks for the declared discrete wavelength table, not continuous-spectrum quadrature certification.

## UI, archives and tests

The new model is **PolychromeLocalFourierEdge/1**. The existing schema-1 archive container stays readable, with `UseLensWavelengths`, per-frame `Spectrum` and `ReferenceWavelengthNanometers` distinguishing the new protocol. Spectral rows and reference must exactly match the embedded lens; mismatches are rejected before replacing a valid file. Monochrome V3 continues to replay with exactly identical arrays/curves; actual V1/V2 records keep their view-only identities. No old experiment is silently converted into a spectral mixture.

**52/52 distinct targeted tests finally pass**, deduplicated in `verification-summary.json`. The first six spectral tests passed. The 51-case regression initially passed 47 with four headless setup failures: the tests looked for newly added controls before the selected tab was realized. Settling layout before accessing the real controls fixed those test fixtures. The final 11-case recheck passes, including the extra historical V3 replay case; the two display-format rechecks pass separately without adding duplicate counts. See the TRX files and logs. An initial test-side nullable warning is fixed in the final builds.

Coverage includes independently derived complex OTF phase/phase-cancellation, raw captured-energy mixing, weight overflow/zero handling, one-band reduction, preserved off-axis chromatic reference, actual unsaved weights 2/1/0, frozen replay after workspace/mode replacement, spectral metadata rejection, existing monochrome algorithms and adjacent Fourier/F-number regressions. The numerical matrix above is reported separately from unit-test counts.

`final/double-gauss-3wave.sfr-experiment.zip` retains the complete standard three-wavelength UI experiment; `final/unsaved-weights.sfr-experiment.zip` retains the separate edited-weight replay fixture. `final/polychrome-*.png` and `final/polychrome-page-*.png` are actual off-screen Light/Field 1 and Dark/Field 3 images. The light case runs the complete UI action; the dark case skips sampling checks and correctly reports that independently of passed MTF agreement. Five legends, current source conditions and the complete caption export at 2404×1804. Wavelength/weight display follows the shared precision formatter; exact float values stay in the experiment rather than leaking binary decimal artifacts into labels. A parameter-specific evidence filter selected no tests (`tests-example.log`) and is excluded; rerunning the two page cases passed and saved the standard experiment (`tests-example-retry.log`).

The final ordinary default-output Release App and both built-in dependencies build successfully. This task uses background computation and headless rendering only. No foreground desktop interaction, full-repository test run, installation, release, new native Zemax/Speos capture or ISO conformity test is claimed. The physical scope remains local scalar imaging, an achromatic analytic edge, no noise and one ROI. See the [current workflow](../../../../docs/MONOCHROME_SLANTED_EDGE_SFR.md).
