# Monochrome slanted-edge SFR — 2026-11-02

Scope: the current sequential lens snapshot → monochrome local PSF → analytic slanted-edge target → floating-point full-square-pixel image → measured single-ROI SFR → matched directional optical/pixel MTF → independent one-axis refinement. Product implementation is pure C# in the shared Core and existing camera laboratory. No foreground application was launched or controlled.

## Reproducible input and result

- Reuse the existing input at `artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX`; no new copy or modified prescription. Source-file SHA-256: `0cd65a2f823baf5079f20f91d8310765899a182a6be72dddac53ede943f2bf75b`.
- Runtime model `MonochromeLocalFourierEdge/1`, estimator `LinearEdgeSfr/1`: 550 nm, Field 1, 3 µm full square pixels, 96×96 linear image, ROI (0,0,96,96), 5° edge, 0.2 px phase, dark/bright 0.2/0.8, pupil 128, subgrid 8, PSF radius 8 physical pixels, ESF half-window 24 pixels.
- SFR MTF50 **0.1311964291 cycles/pixel** (43.7321430 cycles/mm); maximum SFR–pixel-integrated MTF discrepancy **0.00411683194** on 129 frequencies through Nyquist.
- Finite PSF captured energy **0.9877177235** relative to the full sampled pupil. Both image and MTF explicitly use that conditional window normalization; this is not a claim that 100% of the optical PSF was captured.

| Refinement (others fixed) | Max relative image difference | Max SFR difference | Max pixel-MTF difference | Relative PSF integral difference | 0.01 gate |
| --- | ---: | ---: | ---: | ---: | --- |
| Pupil 128 → 256 | 0.0002942967 | 0.0008057952 | 0.0008071118 | 0.0001546743 | Pass |
| Subgrid 8 → 16 | 0.0000241814 | 0.0000679334 | 0.0000679340 | 0.0000532308 | Pass |
| PSF radius 8 → 16 | 0.0027590966 | 0.0081351472 | 0.0085372014 | 0.0070965098 | Pass |

Gate means completion and finite numerical differences under the declared threshold; it does not replace an optical accuracy tolerance or establish an infinite-sampling limit. Separate image, SFR and MTF comparisons are retained. The original lens and current workspace are not modified, refocused or fitted to the SFR.

`reference-lens.sfr-experiment.zip` is the one authoritative frozen run with all baseline/refined linear arrays, curves, ESF/LSF, exact snapshot and provenance. `service-summary.json` gives compact metrics and exact frozen replay after replacing the workspace and switching to NSC. The zip has six entries and a SHA-256 JSON digest; the CSV retains 17 significant digits. The PNG is only a preview, never an SFR input.

## Verification records

Initial `tests.log`/`sfr-focused.trx`: 9 passed, 3 failed. The Fourier-equivalence test originally used the general FFT's historical (N−1) display-pitch metadata with a cell-centered N-node pupil; its fixture now uses the exact cell-centered Fourier lattice. No generic FFT implementation or optical tolerance was changed. The initial headless page tests exposed numeric-input initialization clearing the result; fixed by invalidating pending text only when invalid. `tests-repair.log`/`sfr-repair.trx` then passed all three repaired cases.

The final verification run is `tests-final.log`/`sfr-final.trx`: **17/17 passed** (12 new cases and 5 adjacent original camera-page cases), zero warnings/errors, 2 min 20 s. It includes the estimator's independent Gaussian/polarity/phase checks, exact square-pixel delta-PSF check, independent anisotropic Gaussian directional MTF, shared FFT lattice equality, actual-lens convergence/resource guards, current-lens nonmutation, floating arrays, integrity rejection, exact replay after source replacement, two-theme offscreen UI/PNG/line-width/legend checks and adjacent original camera pages. Current scoped counts are also recorded in `docs/CURRENT_STATUS.md`. `app-build.log` records the normal default Release App build, including both built-in modules, with zero errors/warnings.

`sfr-Light.png` and `sfr-Dark.png` are the actual 2404×1804 exported curves. Page captures show controls and linear image previews. Light runs the actual page action including convergence; Dark uses an explicitly unchecked fixture, and its caption truthfully states that sampling was not executed.

## Retained unsuccessful exploration and limits

The `core-probe*.log`, `probe-fnum*.log`, `probe-opd*.log`, `probe-50mm.log`, `50mm/` and top-level `core-probe.json` are **early unsuccessful probes**, not final verification. Two existing Double Gauss prescriptions were not converged at the stated settings, with large OPD/window sensitivity; they were neither refocused nor declared passed. An initial Huygens-based prototype also exposed a sampling-dependent finite-cell footprint, so the final new local workflow uses the shared traced OPD pupil and explicitly bounded separable Fourier readout. Existing Huygens behavior and reference tolerances were not changed. `probe-123456.log`/`123456/core-probe.json` document the successful Core-only run with the final algorithm before adding complete application provenance.

This task does not establish ISO 12233 compliance, Speos parity, a new native Zemax comparison, absolute sensor/RAW calibration, full scene/field-dependent target projection, vector diffraction, or whole-product/full-suite certification. Reusing the primary reference *input* is not a new comparison with its frozen Zemax output. Existing general FFT scale conventions remain untouched; the new continuous local readout uses its declared physical cell-centered lattice.

Usage and algorithm: [product contract](../../../../docs/MONOCHROME_SLANTED_EDGE_SFR.md).
