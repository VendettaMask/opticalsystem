# Riflescope MTF vs field, 2026-10-31

Pure C# diagnostics using the product's shared Core. These tools never control an interactive OpticStudio instance or the desktop. Native jobs use `CreateNewApplication`, `captureScreenshots=false`, an exclusive background-capture lease and bounded, owned-process cleanup.

The prepared study is `artifacts/validation/riflescope-mtf-field-20261031`. `cases.json` binds the original source and Core snapshot; `preparation.json` binds the unchanged comparison policy. Inputs and frequencies must not be changed to obtain a pass.

Build this project to a fresh output directory. Invoke its DLL with three arguments: mode, absolute repository directory, absolute study directory.

- `compute32`, `compute64`: both methods, all six frequencies, with explicit pupil/image sampling and automatic image pitch. Suffix `-fourier` or `-huygens` selects one method. Each analysis has a three-minute cancellation budget.
- `critical128`: Fourier 3.2 cycles/mrad, both directions, ten field nodes.
- `assess`: completed direct native captures, Core curves, convergence and unchanged result gates. `core-cohort.json` selects the actual Core SHA; missing/unavailable results remain errors.
- `probe-projection`, `probe-psf`: diagnostic counterfactuals. These are not product execution paths or convergence certificates. The PSF probe uses DC normalization for shape diagnosis only; the regression tests use public absolute-intensity output without fitted scaling.
- `window64`, `window128`: independent Huygens pupil sampling with image 64 and fixed 0.04 mrad pitch. These are explicit finite-window controls, not converged references.
- `spectra`: independent native spectral controls and natural-cubic field-display reconstruction. Exact native frequency knots are selected; no frequency interpolation. The reconstruction is verified against the complete native 64-sampling field curves. Its higher-sampling controls have not passed all convergence gates.

`Prepare-Spectra.ps1` creates fresh independent pupil/image native jobs. Run them with the existing `FftProfileNative20261029` runner and the installed 2026 R1 API. Automatic native Huygens image/pupil 256 required six separate frequency jobs; preserve the preceding 120/600-second timeouts. Do not promote incomplete captures or `Close` to `Pass`.

Seal immutable references and evidence with `tools/validation/Seal-RiflescopeMtfField.ps1 -Mode Reference|Evidence`. The reference contract expressly distinguishes converged Fourier authority from Huygens finite-sampling regression. See `docs/RIFLESCOPE_MTF_FIELD_2026-10-31.md` for conclusions and open work.
