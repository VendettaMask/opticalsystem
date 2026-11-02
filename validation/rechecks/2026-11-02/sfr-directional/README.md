# Independent full-pupil MTF / slanted-edge SFR — 2026-11-02

## Finding and repair

The V2 reference was the Fourier transform of the same finite PSF used to synthesize the image. Agreement demonstrated finite-window self-consistency, not agreement with the complete pupil's MTF. The independent complex-pupil autocorrelation exposed maximum SFR/reference differences of 0.01046–0.01317 for the standard Double Gauss fields at the old pupil-128/radius-16 settings. The earlier V2 finite-window convergence results remain historical results under their declared protocol.

V3 implements exact fractional-shift overlap of the sampled rectangular complex pupil cells, normalized to their complete pupil power. It computes complex linear correlation before taking magnitude; zero padding prevents circular pupil wrap. This reference never reads the cropped PSF. Physical scaling uses the same wavelength and separate working F numbers, along the independently fitted edge normal.

The PSF Fourier integral now includes each rectangular pupil cell's sinc footprint. This brings the finite PSF and full-pupil reference under the same declared discrete pupil model; it does not by itself remove finite-window loss. The intermediate `cell-integral/directional-before.json` deliberately retains the failing small-window check. The main remaining discrepancy was PSF truncation, so defaults are now pupil **256**, subgrid **8**, PSF radius **32 pixels**. Refinements are independently **512 / 16 / radius 64**, with the original **0.01** numerical gate.

Large readouts use exact chirp convolution/FFT instead of increasing direct-operation budgets. Tests compare its complex amplitudes against independent finite Fourier sums, large directional PSF MTF against sparse point phasors, and fractional pupil correlation against analytic triangular overlap and sparse complex-cell overlap. FFT lengths remain at most 2048 and bounded work/storage checks remain active. No lens-specific runtime branch, prescription change, refocus, fitted correction factor or tolerance relaxation was introduced. General FFT/Huygens conventions are unchanged.

## Physical inputs and results

Reuse the unchanged inputs, without another source copy:

- Built-in `src/OpticalSystemDesign.App/Assets/SequentialExamples/Objectives/Double Gauss 28 degree field.zmx`, SHA-256 `f994005d633d4af742402469c9e4e2e0c289a57aed3ae876fb3231d1d3f95460`.
- `artifacts/zemax/123456-zemax-2026-r1-baseline/source/123456.ZMX`, SHA-256 `0cd65a2f823baf5079f20f91d8310765899a182a6be72ddac53ede943f2bf75b`.

All results below: 550 nm, 3 µm full square pixel, 96×96 image, ROI (0,0,96,96), near-vertical edge tilted 5°, phase 0.2 px, levels 0.2/0.8, ESF half-window 24. Each field retains its original one-based number. These are local scalar, monochrome, noise-free results; there is no actual-lens horizontal-edge or ISO certification claim.

| Lens / Field | SFR MTF50 (cycles/pixel) | Max SFR vs complete pupil × pixel | Max window vs complete optical MTF | Captured PSF integral | Three refinements |
| --- | ---: | ---: | ---: | ---: | --- |
| Double Gauss / 1 | 0.1014840790 | 0.0084442620 | 0.0053157759 | 0.9949182644 | Pass |
| Double Gauss / 2 | 0.1026758970 | 0.0072128338 | 0.0046781157 | 0.9956105523 | Pass |
| Double Gauss / 3 | 0.0905672156 | 0.0084013751 | 0.0054826098 | 0.9947288558 | Pass |
| 123456 / 1 | 0.1306307672 | 0.0066820504 | 0.0042209187 | 0.9959888210 | Pass |
| 123456 / 2 | 0.1190975237 | 0.0074411691 | 0.0048740516 | 0.9953633957 | Pass |
| 123456 / 3 | 0.1305620688 | 0.0070240777 | 0.0045008978 | 0.9957044409 | Pass |
| 123456 / 4 | 0.1326448186 | 0.0068051312 | 0.0042972545 | 0.9958811071 | Pass |
| 123456 / 5 | 0.1316754869 | 0.0067315327 | 0.0042368817 | 0.9959642329 | Pass |

The actual fitted normal can change slightly during refinement. Each `final/*-field-*.json` records all 129 frequencies, both references, measured SFR and the independent one-axis differences. The maximum complete-pupil × pixel change under pupil refinement is **0.00202682** across these fields; maximum finite-window × pixel change under radius refinement is **0.00202632**. This is a scoped single-doubling check, not proof at infinite sampling.

## Desktop, persistence and verification

Current runs use `MonochromeLocalFourierEdge/3`; estimator remains `LinearEdgeSfr/1`, archive schema remains 1. Five independently selectable curves and two separate statuses distinguish complete-pupil agreement from sampling convergence. CSV adds `full_pupil_mtf` and `full_pupil_pixel_integrated_mtf`. Full-pupil curve values and comparison differences are required and checked for consistency in V3. V1/V2 real archived experiments remain viewable/resavable with their original curves and algorithm identities; recomputation across versions is rejected.

**59/59 distinct cases finally passed**, deduplicated by test name with latest outcomes in `verification-summary.json`. The pupil/reference run passed 16/16; the regression run initially passed 42/43; final targeted recheck passed 12/12 (repeated cases are not added). The single regression failure compared V3's small-window difference against the historical V2 number 0.017; V3's actual value was 0.0168118, still failing the original 0.01 gate as required. The test now verifies that unchanged gate rather than importing a different protocol's number. Its full new-default branch subsequently passed. Receipts: `sfr-pupil.trx`, `sfr-regression.trx`, `sfr-final-recheck.trx` and matching logs. The final recheck also verifies real V2 archives display only their original three curves and disable replay.

The initial restricted dependency-check invocation stalled before completing tests (`tests-pupil.log`) and was stopped only for this task's own worker; the background retry used installed dependencies. One intermediate shared-workspace build reported CS0108 in the separately edited coating dock file; the final normal default-output App build and both built-in dependencies completed with **0 warnings / 0 errors** (`build-final.log`). No foreground software was opened or controlled.

Actual off-screen Light/Double Gauss and Dark/123456 images are `final/sfr-Light.png`, `final/sfr-Dark.png`, and corresponding `sfr-page-*.png`. Visual review confirms all five legends and the complete fixed caption sheet fit; PNGs are 2404×1804. The dark fixture deliberately skips sampling checks, so its caption reports unexecuted sampling independently of passed complete-pupil agreement. The light fixture runs the full UI action. `final/*.sfr-experiment.zip` retains current actual frozen payloads; service tests replace the current workspace and verify exactly identical linear arrays and all curve values on replay. Hash-corrupt, missing-reference and inconsistent-reference records are rejected without overwriting a valid file. `output-hashes.json` fingerprints the retained result payloads. Strict documentation checking reports zero broken links, fence issues, local-only links or repeated current-guide paragraph groups (`documentation-check.json`, `docs-check.log`).

Earlier diagnostic paths preserve distinct stages: `directional-before.json` / `probe-before.log` (V2 point-cell PSF), `cell-integral/` / `probe-cell-integral.log` (integrated cells, old small window), and `expanded/` / `probe-expanded.log` (new defaults, before application integration). The diagnostic console program now uses explicit V3 settings and writes `directional-probe.json`; it does not overwrite these historical payloads when rerun into a new output folder.

No new native Zemax/Speos measurements, full-repository test run, installation or release was performed. The severely defocused saved library files retain the phase-undersampling rejection described in the [previous diagnosis](../double-gauss-sfr/README.md). See the [current user workflow](../../../../docs/MONOCHROME_SLANTED_EDGE_SFR.md).
