# Double Gauss FFT energy recovery — 2026-11-21

The default analysis now returns the three field curves and the diffraction-limit curve. Both local comparison galleries and their affected contact sheets have been regenerated from the real Avalonia analysis page. Original diagnostic screenshots and original native images remain available.

## Source and original failure

- Menu example: `Objectives/Double Gauss 28 degree field.zmx`.
- SHA-256: `f994005d633d4af742402469c9e4e2e0c289a57aed3ae876fb3231d1d3f95460` (unchanged).
- All three fields, all three wavelengths, centroid reference, automatic maximum established at **20 µm**, 401 plotted nodes per curve.
- Original default: fixed 64×64 pupil. The first rejected pupil had an adjacent unwrapped phase difference of **0.606616 waves**, above the existing 0.5-wave risk gate. Checking every pupil in the validation sweep finds a maximum of 0.720000 waves at this density.
- Fixed 128 also fails the phase gate. Fixed 256/512/1024 can return numerical curves, but phase resolution alone is not output-convergence certification.

## Change

The workbench now offers the same Automatic / Fixed / Validate requested / Adaptive sampling strategies for diffraction energy as the existing FFT analyses. Automatic recovery runs only after measured phase undersampling. Fixed requests retain their original rejection. Core constructor defaults and frozen low-density input settings are unchanged.

Adaptive validation now handles all fields and polychromatic energy. When a complete dense FFT image no longer fits the existing grid limit, a bounded spectral window evaluates the same finite Fourier sums at the actual physical coordinates. Each wavelength contributes intensity, weighted by the existing spectral and pupil-normalization factors. The total power and common centroid come from complete independent Fourier periods (Parseval and full-image first moments), including power outside the readout. Different wavelengths' complex amplitudes are never added. The local window is not renormalized to one.

The automatically selected original radius interval is fixed before refinement. Every returned curve covers all original nodes; no extrapolation or smaller-radius substitute can receive a convergence certificate. The existing 0.5-wave gate, 0.001 relative RMS target, 1e-7 absolute alternative, aggregate allocations, Fourier-operation limits, cancellation and 100,000,000 nominal-cell validation budget remain enforced. Energy accounts for the actual defined source grids: actual fields plus one ideal field, each at every selected wavelength. The sweep uses 67,092,480 nominal cells.

The typed figure summary displays wavelengths, field count, reference, physical radius, requested/returned pupil density and the actual convergence difference/target. It replaces internal field names and the generic “remaining items” caption. The same analysis view feeds on-screen drawings and exports.

## Numerical evidence

`recovered/Automatic.json` records the full input settings, result, phase diagnostics and validation report. The source and requested settings are retained; automatic output uses **2048×2048**.

| Comparison | Maximum relative RMS across the four curves | Maximum pointwise absolute difference |
| --- | ---: | ---: |
| 1024 → 2048, validation sweep | 0.0008442001 (0.08442%) | 0.001689102 |
| 2048 → independently requested 4096 | 0.0004695099 (0.04695%) | 0.0008485255 |

The relative RMS values satisfy the original 0.001 target. The absolute columns are also retained; a relative RMS target is not a universal pointwise-error guarantee. Full per-curve independent differences are in `independent/comparison-2048-4096.json`; the 4096 raw result is in `independent/4096.json`.

Four independent 256-density controls compare the bounded spectrum with explicitly combined complete 512×512 monochromatic images. They cover actual/ideal data, centroid/chief/vertex references and radial/square integration, matching at every knot within 1e-10. The vertex equality case uses an on-axis field so the requested window lies inside the original independent Fourier period; out-of-period windows retain the existing rejection.

## Verification and known historical differences

**42 focused checks passed, 0 failed, 0 skipped** in `results/energy-recovery-focused.trx`. They include the new controls and default recovery, unchanged fixed rejection, insufficient-budget refusal, typed summary, source immutability, cancellation, existing Fourier validation guards and existing bounded cumulative-energy checks.

An additional **30 historical low-density polychromatic capture checks** were run separately. **20 pass and 10 fail**. The task's four modified Core files were restored to their pre-task computation in an isolated build (`baseline-core` and `baseline.targets`); all 30 verdicts and all 10 failure messages/numerical discrepancies were identical before and after. See `results/polychromatic-pre-task.trx`, `results/energy-recovery.trx` and `results/historical-reference-comparison.json`. No tolerance was relaxed and none of those ten failures is counted as passed. These historical differences are outside this screenshot repair and remain unresolved here.

The default Release App build passed with 0 warnings and 0 errors (`default-build.log`). The final real AnalysisPanel screenshot is `workbench/diffraction-encircled-energy.png`; its footer and curves were visually inspected. This is an off-screen Avalonia run, with no control or restart of the user's desktop app. Temporary build/test runtimes and private capture settings are removed after verification.

Both galleries render all selections and validate every local image link: 213 selections / 391 links in the three-lens gallery and 426 selections / 780 links in the six-lens gallery. Their embedded data matches their JSON; all source prescriptions remain unchanged. `gallery-update.json` records the exact original/current image hashes. The original native reference image SHA-256 remains `53e7b1680e41ed834b812864a4a0dd823a696ddefe86c1eb0b8ae0d1f26affeb`.

No new native numerical capture or 95-lens full matrix was run. The right-side native image still contains its original low-sampling warning; it is preserved evidence, not a converged numerical reference. The independent-density agreement above establishes local self-convergence, not a new external-certification verdict.

Primary feature documentation: [Ansys OpticStudio — Diffraction enclosed energy](https://ansyshelp.ansys.com/public/Views/Secured/Zemax/v25101/en/OpticStudio_User_Guide/OpticStudio_Help/topics/Diffraction_enclosed_energy.html). It documents dependence on OPD slope and sampling, plus the warning when sampling is inadequate. The local 0.5-wave gate remains this implementation's own criterion, not a claimed vendor-private threshold.

## Reproduction

```powershell
dotnet test validation/rechecks/2026-11-21/fft-energy-recovery/focused/EnergyRecovery.csproj -c Release -p:NuGetAudit=false -p:UseArtifactsOutput=true -p:ArtifactsPath="$PWD/validation/rechecks/2026-11-21/fft-energy-recovery/build" --filter 'FullyQualifiedName!~PolychromaticCumulativeEnergyTests' --logger 'trx;LogFileName=energy-recovery-focused.trx' --results-directory validation/rechecks/2026-11-21/fft-energy-recovery/results
```

NuGet audit was disabled for this invocation because the advisory endpoint was unavailable; no vulnerability-audit pass is claimed. `probe/EnergyProbe.csproj` retains raw-density runs, and `capture/EnergyCapture.csproj` links the existing GUI-capture runner. The gallery update script requires the retained successful capture and validated report, verifies the prescription/archive hashes, and preserves native image bytes.
