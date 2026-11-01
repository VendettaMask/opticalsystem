# Binocular Fourier convergence study

`assess-direct` now validates four fresh, completed native `data.json` captures at 128/256 for both configurations. It separately compares the previous Core cohort, the current declared `core-cohort.json`, native convergence and every previous derived reference. `FftFieldCaptureAudit` verifies final execution, complete buffers, source/settings and units; a transient null poll cannot override final success. See [direct results and the still-unconfirmed timeout trigger](../../../docs/BINOCULAR_FOURIER_DIRECT_REFERENCE_2026-10-31.md). Current authority is the direct reference package; the derived-reference stage below retains its historical identity.

The [remaining-frequency stage](../../../docs/BINOCULAR_FOURIER_REMAINING_FREQUENCIES_2026-10-31.md) adds `prepare-remaining`, `compute-remaining32`, `compute-remaining64`, `normalize-native-nodes`, and `assess-remaining`. Failed single-frequency/full-plot requests remain unchanged. The successful native captures use sixty explicit Grid=0 amplitude MTFT/MTFS requests, at ten read-back angular fields in the owned unsaved system.

`NativeOperandReference` performs diagnostic reference normalization only. Its independent dense cubic solver reproduces 42 complete historical native curves (12,600 values) to floating-point accuracy; the rejected natural-right-boundary control remains separate. `derived-curves/*/curve.json` records this provenance and hashes its raw operands and display proof. It is never labeled a new raw analysis buffer and never supplies product calculations. `assess-remaining` validates those identities, the unchanged policy and the explicitly selected `core-cohort.json`; it reads data rather than rerunning optics. Focused tests persist actual Core 32/64 data only when `OSD_BINOCULAR_FOURIER_EVIDENCE` is set.

`tools/validation/Seal-BinocularFourierRemaining.ps1` seals fresh reference/evidence archives and rereads every member hash; it never runs tests or native applications. The earlier high-frequency study below retains its original scope.

This C# diagnostic investigates the original binocular C1/C2 high-frequency MTF field curves. Every optical trace and OPD comes from shared Core or separately captured native ZOS-API data. Experimental quadratures are independent diagnostic controls, never product evaluators.

Build in Release. Commands take `mode repository study-root`. The study uses the frozen original inputs under `artifacts/validation/afocal-mtf-field-20261031` and writes to its own fresh root.

- `prepare`: nine bounded native controls (individual colors, 64/128 sampling, frequency order/repetition, ordinary FFT MTF). Execute generated `native/jobs.json` with `FftProfileNative20261029`; screenshots must remain disabled.
- `probe`: shared Core monochromatic complex OTF sampling study.
- `prepare-points`, `compare-points`: 704 explicit shifted-pupil rays and OPD comparison.
- `endpoints`: bounded shared Core zero-defocus MTF, both configurations and 32–512 sampling; persists completed results after each case.
- `curves`: only 3.2 cycles/mrad, ten calculated field positions, Core 32/128. Reads available native references without replacing them.
- `assess`: reads frozen results and the explicit `single-frequency-native/c{config}-n{sampling}` captures. Checks physical settings/readbacks and uses unchanged comparison-policy tolerances for original-32, reference convergence, Core convergence and final comparisons. It performs no optical calculation and never upgrades the original-32 verdicts.

`NativeFourierOperandCapture` supplies a separate, bounded `fourier-operands` host adapter. Requests explicitly declare sampling, wavelength, field, frequency, grid and data types; it captures native schemas and actual MTFT/MTFS values without saving the source system. The endpoint equivalence to the original field curve was independently verified at 32 sampling.

`rules`, `gauss`, `paraxial`, `clm`, `clm-paraxial`, `clm-inset`, `pupils`, and `prepare-flat` are recorded hypothesis/capability experiments. They did not determine a product correction and must not be used to fit lens-specific runtime constants. Failed and timed-out controls remain in the evidence archive.

Portable regression inputs are in `validation/zemax/2026-r1/binocular-fourier-converged-2026-10-31/reference.zip`. Numerical results, the explicit user decision to use converged native references, and remaining scope are documented in [the report](../../../docs/BINOCULAR_FOURIER_CONVERGENCE_2026-10-31.md).
