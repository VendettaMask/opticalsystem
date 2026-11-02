# Double Gauss SFR diagnosis and V2 repair — 2026-11-02

## Cause: distinguish physical inputs from numerical sampling

The previous investigation used two saved library files. Neither is the current built-in standard Double Gauss 28° example. The first has a finite 500 mm object distance, with a saved image distance still near the infinite-conjugate design; the second is also severely defocused at its saved image plane. No prescription, conjugate, stop, material, tolerance or native reference was changed in this task.

| Input identity | Object distance | RMS spot radius at saved image, 550 nm | Maximum adjacent unwrapped OPD, pupil 128 / 256 |
| --- | --- | ---: | ---: |
| Library `Double Gauss 28 degree field_2.zmx` | 500 mm | 2.15209568 mm | 13.6868965 / 6.89605386 waves |
| Library `double-gauss-50mm.zmx` | Infinity | 7.31210285 mm | 11.0229406 / 5.54243702 waves |
| Built-in `Objectives/Double Gauss 28 degree field.zmx` | Infinity | 0.009868946 mm | 0.0856140 / 0.0434622 waves |

The default 96×96 image at 3 µm spans only 0.288 mm. The two library files' millimeter-scale blur and multi-wave pupil steps cannot be resolved by those local image/pupil settings. Increasing the subpixel image grid alone does not repair an aliased pupil. The coarse shared-Core focus diagnostic (10 mm steps) finds a much smaller spot around +20 mm for the finite-object file; it is a diagnostic sweep, not an applied refocus or a claimed precise best-focus solution.

File SHA-256 identities:

- Finite-object library file: `5533b4b22220433d22afb10b12562874c7568c92fcde6a1d6f0e36359b2df525`.
- 50 mm library file: `95636a6246893802d676c7bad3c73869c13461ea17dae3ea9bd05eea2a13eaf6`.
- Built-in standard file: `f994005d633d4af742402469c9e4e2e0c289a57aed3ae876fb3231d1d3f95460` — byte-identical to the existing reference input under `validation/zemax/2026-r1/objectives-repair-2026-10-23/captures/dense-energy/1634-052-double-gauss-28-degree-field-c1-primary-f1-w2-diffraction-encircled-energy-p256/source.zmx`. Reuse those inputs; no duplicate lens payload was added.

The standard file has a separate, ordinary sampling issue: pupil and image subgrid were already stable, but PSF radius 8→16 pixels changed the pixel-integrated MTF by **0.01767618**, above the existing 0.01 numerical gate. The previous two-file conclusion must not be generalized to the built-in standard example.

## General repairs

1. The new local PSF path observes the existing shared traced wavefront and checks **unwrapped** neighboring illuminated OPD before using its complex pupil. More than 0.5 wave per step is explicitly rejected, including when optional convergence checks are disabled. This is a necessary phase-sampling check, not a sufficiency certificate. Wrapped complex samples alone cannot expose a 13-wave step. The existing public FFT/Huygens algorithms retain their original behavior; the new internal observer is null for their ordinary calls.
2. For the analytic infinite straight edge, sort actual two-dimensional PSF samples by normal displacement and retain cumulative weight and first/second moments. This exactly sums the piecewise quadratic overlap of a physical square pixel with the shifted half-plane. It is equivalent to the previous sampled-PSF convolution/pixel integral, supports asymmetric kernels and either edge tilt, and does not substitute a Gaussian or an ideal one-dimensional lens. Independent polygon clipping verifies the pixel integrals. Removing the large source-image FFT makes the wider window's independent image-grid refinement fit without raising the shared Fourier/MTF work budgets.
3. Set the general default PSF radius to **16 physical pixels**, retaining pupil 128 and subgrid 8. Refinements are independently 256, 16 and radius 32. No file-name/material-specific runtime branch, autofocus, tolerance relaxation or hidden resampling was introduced.
4. New runs are **`MonochromeLocalFourierEdge/2`**. V1 archives retain their identity, can be viewed/resaved with a historical notice and cannot silently recompute under V2. This prevents promoting old aliased or unchecked records to current validated results. The archive schema, ROI estimator (`LinearEdgeSfr/1`) and sensor/NSC protocols remain unchanged.

## Standard Double Gauss V2 result

550 nm, Field 1, 3 µm full square pixel, 96×96 linear image, ROI (0,0,96,96), 5° edge, dark/bright 0.2/0.8, phase 0.2 px, pupil 128, subgrid 8, PSF radius 16, ESF half-window 24. Source unchanged.

- **MTF50: 0.1011427006 cycles/pixel** (33.71423354 cycles/mm).
- **Maximum SFR vs pixel-integrated MTF absolute discrepancy: 0.00390648934** through Nyquist.
- **PSF captured energy: 0.9934444664** relative to the full sampled pupil; conditional finite-window normalization remains explicitly reported.

| Independent refinement | Maximum image difference / contrast | Maximum SFR difference | Maximum pixel-MTF difference | Relative raw PSF integral difference | 0.01 numerical gate |
| --- | ---: | ---: | ---: | ---: | --- |
| Pupil 128→256 | 0.0008102884 | 0.0019902785 | 0.0020219417 | 0.0004761282 | Pass |
| Subgrid 8→16 | 0.0000201473 | 0.0000177761 | 0.0000261094 | 0.0000205739 | Pass |
| PSF radius 16→32 | 0.0023174476 | 0.0026889542 | 0.0072012487 | 0.0057232762 | Pass |

`123456.ZMX` also retains convergence under the larger default window (MTF50 0.1305576664; maximum SFR/reference difference 0.00413721173). Both severely defocused files explicitly reject undersampled phase at 128 **and 512**. Their historical failure verdicts are preserved; the new standard-example pass does not reclassify those files.

## Evidence and reproduction

- Reproducible pure C# probe: `tools/diagnostics/DoubleGaussSfr20261102/Probe.csproj`; run with repository root and an output directory as its two arguments. It calls shared Core tracing/spot/focus/wavefront/SFR APIs. It never applies the focus sweep to the input lens.
- Before: `diagnostic-before-v2.log` and `diagnostic.json` record the actual V1 build, four input identities, OPD/spot/focus metrics and exact input nonmutation. `diagnostic-before.log` is an initial diagnostic-writer failure on JSON Infinity, not a product optical failure.
- After: `diagnostic-after.log` and `after/diagnostic.json` record the V2 guarded local PSF and wider window. Each record includes `PrescriptionUnchanged=true`. The standard reference-input bytes are identical to the current App asset.
- `double-gauss-standard.sfr-experiment.zip` is the authoritative V2 frozen standard run; it contains all linear arrays, ESF/LSF, MTF/SFR and refined frames, source provenance, exact lens snapshot and integrity hash. Application replay after serialization is exact. `reference-lens.sfr-experiment.zip` is the separate V2 123456 control, not a duplicate Double Gauss payload.
- `tests.log`/`double-gauss-sfr.trx`: **40/40** targeted cases passed, covering new diagnostics/integration, existing SFR, frozen replay, GUI and adjacent shared FFT/F-number regressions. No full-suite claim.
- Final tests settle asynchronous document-change notifications before invoking a new UI action. `tests-ui-final.log` retains the one initial UI assertion failure caused by an outstanding notification cancelling that immediate action; its other three cases passed. The follow-up is recorded in `tests-ui-settled.log`/`double-gauss-sfr-ui-settled.trx`, without adding repeat runs to the distinct test count.
- Offscreen Light uses the actual built-in Double Gauss page action including all refinements. Dark uses the 123456 display control. `sfr-Light.png`/`sfr-Dark.png` are the actual 2404×1804 exports; page captures show controls and linear previews. UI tests also verify the phase-sampling error and V1 view-only state.

Original V1 results and source/binary hash receipts remain under [the initial stage](../monochrome-sfr/README.md). No evidence was overwritten there. New source/binary/archive fingerprints and documentation checks are recorded in this directory. The normal default Release App and both built-in modules are built by the test commands; no foreground application was launched or controlled. This is a numerical convergence/implementation check, not new native Zemax/Speos parity, ISO certification, a whole-product audit or a physical sensor calibration.

Usage and boundaries: [current SFR contract](../../../../docs/MONOCHROME_SLANTED_EDGE_SFR.md).
