# First-order definitions and FFT validity

Build this project using the `SourceRevisionId` in the dated evidence record. Run `DefinitionReference20261103.dll MODE REPOSITORY FRESH_OUTPUT [PLAN_OR_CAPTURE_ROOT]`, optionally using `dotnet exec --additionalprobingpath <NuGet-cache>` to reuse dependencies.

- `crystal`: independent native controls for source configurations, aperture, thickness and the paraxial-ignore flag. EFFL wavelength is operand integer slot 2. Keep real-ray walk-off and the scalar vertex-power convention distinct.
- `convention`: explicitly select `CenteredVertexOrdinaryIndexV1` on the owned unsaved native system, compare five controls and reject the mixed legacy reference. `convention-replay` reuses the capture root after checking source and request identity.
- `fourier`: bounded native captures from `{index,sampling,delta,scale}` jobs. Read back the actual FFT/Huygens pupil sampling, preserve invalid native diagnostics and capture failures, use the exclusive background-native lease and never attach to an existing application.
- `validity`: replay the four original Toroid requests through the user-facing Workbench boundary. Underresolved phase must yield typed `Unavailable`, with no numeric curves.

`ResidualClosure20261103 compare` checks native/Core agreement and each side's sampling convergence with unchanged windows, axes and tolerances. PSF/MTF use 256/512; the Huygens cross section requires 512/1024. Keep the failed 256/512 convergence and the earlier misdeclared 32-sample cross capture as history. Huygens controls certify the declared finite window, not whole-plane encircled energy.

`Replay-PriorControls.ps1` verifies and restores only the previous native inputs needed for 14 convergence checks; the medium-reference runner supplies another 24. `Write-Report.ps1` checks the 252 original verdicts, 156 cross-analysis prior Passes, five convention cases, four availability results, 21 new and 38 prior converged checks, 745 distinct tests and final Core identity. `Seal-Evidence.ps1` binds final sources to portable PDBs, reuses content-addressed archives, fully reads back payloads and retires only owned expanded runs.

See [the scoped report](../../../docs/FIRST_ORDER_FFT_VALIDITY_REPAIR_2026-11-03.md). General Core finite-sum APIs remain available; the necessary phase-sampling check is enforced for Workbench requests and is not itself a convergence certificate. Original 125 Difference and four invalid FFT reference verdicts are preserved.
