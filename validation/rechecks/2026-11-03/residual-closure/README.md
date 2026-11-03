# Residual closure and reference convergence, 2026-11-03

See the [repair report](../../../../docs/RESIDUAL_CLOSURE_REPAIR_2026-11-03.md) and [summary](summary.json). The original six-lens / 252-request scope is 239 Pass / 1 Close / 1 Difference / 11 Incomparable / 0 Error; old passing requests remain passing.

- [Four residuals](four-residuals.json): 107 and 1376 pass their unchanged original requests. The original 108 low-sampling Close remains; its converged protocol passes. The 125 scalar-definition conflict remains open.
- [Eleven reference gaps](eleven-references.json): six crystal PSFs and the annular PSF have converged controls; four Toroid native references remain invalid through the existing 2048 sampling bound. Original invalid requests are never promoted.
- [Crystal PSF controls](crystal-psf-controls.json) and [additional convergence](additional-convergence.json): all 38 checks pass, with complete windows, physical axes, pitch and original gates.
- [Cross-analysis controls](cross-analysis-results.json): all 156 selected prior-Pass requests pass using the final Core; scopes overlap and are not added to the original 252 count.
- [Native upper-bound failures](native-limit-results.json) and [first-order differential controls](first-order-differential-controls.json) retain actual reference limits and the scalar/differential distinction.

599 scoped cases plus 134 distinct existing field-operand cases pass. The normal default App with both built-in modules builds with zero warnings/errors. Final Core SHA256: `a5290b6fd2ff5980f636a5e48517f6c971898605a16ad941a89458753466bd82`. No full 95-example matrix or repository-wide test run is implied.

`archive-index.json` resolves numerical runs, failures, native captures, source records and final owned binaries to one payload per hash. `archive-verification.json` records complete new/reused-entry readback before retiring expanded owned runs. Existing verified archives and active fixtures are reused, including the final coupled-curvature native fixture. Third-party binaries and symbols are dependencies, not duplicated validation payloads. Final source/PDB and App/Core bindings are checked separately. No user application was controlled or closed, and no remote synchronization was performed.
